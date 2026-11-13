using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Opsis.Containers;

/// <summary>The one control channel of the new core (plan 2.4.C phase 5): a named pipe with versioned JSON messages, one request and one response per connection, with limits.</summary>
public static class PipeProtocol
{
    public const int Version = 1;
    public const int MaxMessageBytes = 1 << 20;     // 1 MB: a bigger request is refused
    public static string DefaultName => "opsis-containers-v1-" + Environment.UserName.ToLowerInvariant();

    public static string Request(string cmd, JsonNode? args = null, string? id = null) =>
        new JsonObject { ["v"] = Version, ["id"] = id ?? Guid.NewGuid().ToString("N")[..8], ["cmd"] = cmd, ["args"] = args }.ToJsonString();

    public static string Ok(string? id, JsonNode? data) => new JsonObject { ["v"] = Version, ["id"] = id, ["ok"] = true, ["data"] = data }.ToJsonString();
    public static string Error(string? id, string code, string message) => new JsonObject { ["v"] = Version, ["id"] = id, ["ok"] = false, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString();
}

/// <summary>What the pipe can ask the core to do. The windows layer implements the parts that need windows (restore icons, show / hide) later.</summary>
public interface IContainersApi
{
    JsonNode? Handle(string cmd, JsonObject args);
}

public sealed class PipeException : Exception
{
    public string Code { get; }
    public PipeException(string code, string message) : base(message) { Code = code; }
}

public sealed class PipeServer : IDisposable
{
    private readonly string name; private readonly IContainersApi api; private readonly CancellationTokenSource cts = new(); private Task? loop;
    public int ReadTimeoutMs { get; init; } = 5000;
    public long Requests { get; private set; }

    public PipeServer(string name, IContainersApi api) { this.name = name; this.api = api; }

    public void Start() => loop = Task.Run(() => Accept(cts.Token));

    private async Task Accept(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                var security = new PipeSecurity();
                security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));   // this user only
                server = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 8, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
                await server.WaitForConnectionAsync(ct);
                var s = server; server = null;
                _ = Task.Run(() => Serve(s, ct), ct);       // one connection never blocks the next
            }
            catch (OperationCanceledException) { server?.Dispose(); return; }
            catch (Exception) { server?.Dispose(); await Task.Delay(200, ct).ContinueWith(_ => { }); }
        }
    }

    private async Task Serve(NamedPipeServerStream s, CancellationToken ct)
    {
        using (s)
        {
            string? id = null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(ReadTimeoutMs);
                var line = await ReadLine(s, timeout.Token);
                Requests++;
                JsonObject req;
                try { req = JsonNode.Parse(line) as JsonObject ?? throw new JsonException(); }
                catch (JsonException) { await Write(s, PipeProtocol.Error(null, "bad-json", "the request is not a JSON object")); return; }
                id = req["id"]?.ToString();
                if ((req["v"]?.GetValue<int>() ?? 0) != PipeProtocol.Version) { await Write(s, PipeProtocol.Error(id, "bad-version", $"protocol version {PipeProtocol.Version} expected")); return; }
                var cmd = req["cmd"]?.ToString();
                if (string.IsNullOrEmpty(cmd)) { await Write(s, PipeProtocol.Error(id, "no-command", "cmd is missing")); return; }
                try { await Write(s, PipeProtocol.Ok(id, api.Handle(cmd, req["args"] as JsonObject ?? new JsonObject()))); }
                catch (PipeException pe) { await Write(s, PipeProtocol.Error(id, pe.Code, pe.Message)); }
                catch (Exception ex) { await Write(s, PipeProtocol.Error(id, "internal", ex.Message)); }
            }
            catch (OperationCanceledException) { try { await Write(s, PipeProtocol.Error(id, "timeout", "the request was not complete in time")); } catch (Exception) { } }
            catch (PipeException pe) { try { await Write(s, PipeProtocol.Error(id, pe.Code, pe.Message)); } catch (Exception) { } }
            catch (IOException) { }
        }
    }

    private static async Task<string> ReadLine(Stream s, CancellationToken ct)
    {
        var buf = new byte[4096]; var sb = new MemoryStream();
        while (true)
        {
            var n = await s.ReadAsync(buf, ct);
            if (n == 0) break;
            var nl = Array.IndexOf(buf, (byte)'\n', 0, n);
            sb.Write(buf, 0, nl >= 0 ? nl : n);
            if (sb.Length > PipeProtocol.MaxMessageBytes) throw new PipeException("too-large", "the request is larger than 1 MB");
            if (nl >= 0) break;
        }
        return Encoding.UTF8.GetString(sb.ToArray());
    }

    private static async Task Write(Stream s, string json) { var b = Encoding.UTF8.GetBytes(json + "\n"); await s.WriteAsync(b); await s.FlushAsync(); }

    public void Dispose() { cts.Cancel(); try { loop?.Wait(1000); } catch (Exception) { } cts.Dispose(); }
}

public static class PipeClient
{
    /// <summary>One request, one response, with a timeout. Returns the parsed response object ({ok, data|error}).</summary>
    public static JsonObject Send(string pipeName, string cmd, JsonNode? args = null, int timeoutMs = 3000)
    {
        using var c = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        c.Connect(timeoutMs);
        var req = Encoding.UTF8.GetBytes(PipeProtocol.Request(cmd, args) + "\n");
        c.Write(req, 0, req.Length); c.Flush();
        using var cts = new CancellationTokenSource(timeoutMs);
        var sb = new StringBuilder(); var buf = new byte[4096];
        while (true)
        {
            var n = c.ReadAsync(buf, cts.Token).AsTask().GetAwaiter().GetResult();
            if (n == 0) break;
            sb.Append(Encoding.UTF8.GetString(buf, 0, n));
            if (sb.ToString().Contains('\n')) break;
        }
        return JsonNode.Parse(sb.ToString().Trim()) as JsonObject ?? throw new IOException("bad response");
    }
}

/// <summary>The commands the core answers now (phase 4): enough to inspect and change the data from a script or an AI agent, and to dry-run the legacy import.</summary>
public sealed class ContainersService : IContainersApi
{
    private readonly ContainersStore store; private readonly Func<IReadOnlyList<MonitorInfo>> monitors; private readonly object gate = new();
    public ContainersService(ContainersStore store, Func<IReadOnlyList<MonitorInfo>> monitors) { this.store = store; this.monitors = monitors; }

    /// <summary>Raised (on the pipe thread) after a command changed the stored data; a host re-reads and updates its windows.</summary>
    public event Action? Mutated;

    /// <summary>Lets the host change the document under the SAME lock as the pipe commands (no lost updates). <paramref name="change"/> returns true when it changed something.</summary>
    public void Update(Func<ContainersDocument, bool> change)
    {
        lock (gate) { var doc = store.Load(); if (change(doc)) store.Save(doc); }
    }

    public ContainersDocument Snapshot() { lock (gate) return store.Load(); }
    public string StorePath => store.Path;

    private string SnapshotDir => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(store.Path)) ?? ".", "container-snapshots");

    private static readonly HashSet<string> Mutating = new() { "create", "delete", "move", "set-title-style", "show", "hide", "import-legacy", "snapshot-load" };

    public JsonNode? Handle(string cmd, JsonObject args)
    {
        var result = HandleCore(cmd, args);
        if (Mutating.Contains(cmd)) { try { Mutated?.Invoke(); } catch (Exception) { } }
        return result;
    }

    private JsonNode? HandleCore(string cmd, JsonObject args)
    {
        lock (gate)
        {
            switch (cmd)
            {
                case "ping": return new JsonObject { ["pong"] = true, ["protocol"] = PipeProtocol.Version, ["schema"] = ContainersStore.CurrentVersion };
                case "list":
                    return new JsonArray(store.Load().Containers.Select(c => (JsonNode)new JsonObject { ["id"] = c.Id, ["title"] = c.Title, ["kind"] = c.Kind.ToString(), ["items"] = c.Items.Count + c.Tabs.Sum(t => t.Items.Count), ["hidden"] = c.Hidden }).ToArray());
                case "get":
                    return JsonSerializer.SerializeToNode(Find(store.Load(), args), ContainersStore.Json);
                case "create":
                {
                    var doc = store.Load();
                    var c = new Container { Title = args["title"]?.ToString() ?? "New container", Kind = Enum.TryParse<ContainerKind>(args["kind"]?.ToString(), true, out var k) ? k : ContainerKind.Data };
                    c.Geometry = LegacyImporter.Normalize(Int(args, "x", 100), Int(args, "y", 100), Int(args, "width", 320), Int(args, "height", 240), monitors());
                    doc.Containers.Add(c); store.Save(doc);
                    return new JsonObject { ["id"] = c.Id };
                }
                case "delete":
                {
                    var doc = store.Load(); var c = Find(doc, args);
                    doc.Containers.Remove(c); store.Save(doc);
                    return new JsonObject { ["deleted"] = c.Id };
                }
                case "move":
                {
                    var doc = store.Load(); var c = Find(doc, args);
                    c.Geometry = LegacyImporter.Normalize(Int(args, "x", c.Geometry.X), Int(args, "y", c.Geometry.Y), Int(args, "width", c.Geometry.Width), Int(args, "height", c.Geometry.Height), monitors());
                    store.Save(doc); return JsonSerializer.SerializeToNode(c.Geometry, ContainersStore.Json);
                }
                case "show":
                case "hide":
                {
                    var doc = store.Load(); var c = Find(doc, args); c.Hidden = cmd == "hide";
                    store.Save(doc); return new JsonObject { ["id"] = c.Id, ["hidden"] = c.Hidden };
                }
                case "snapshot-save":
                {
                    var name = args["name"]?.ToString() ?? throw new PipeException("bad-args", "name is required");
                    ContainerSnapshots.Save(store.Load(), SnapshotDir, name); return new JsonObject { ["saved"] = ContainerSnapshots.Sanitize(name) };
                }
                case "snapshot-list": return new JsonArray(ContainerSnapshots.List(SnapshotDir).Select(n => (JsonNode)JsonValue.Create(n)!).ToArray());
                case "snapshot-delete":
                    return new JsonObject { ["deleted"] = ContainerSnapshots.Delete(SnapshotDir, args["name"]?.ToString() ?? throw new PipeException("bad-args", "name is required")) };
                case "snapshot-load":
                {
                    var name = args["name"]?.ToString() ?? throw new PipeException("bad-args", "name is required");
                    var snap = ContainerSnapshots.Load(SnapshotDir, name) ?? throw new PipeException("not-found", $"no snapshot '{name}'");
                    var cur = store.Load(); snap.Rules = cur.Rules;      // rules belong to the installation, not to a layout
                    store.Save(snap); return new JsonObject { ["loaded"] = ContainerSnapshots.Sanitize(name), ["containers"] = snap.Containers.Count };
                }
                case "rules-list": return JsonSerializer.SerializeToNode(store.Load().Rules, ContainersStore.Json);
                case "set-title-style":
                {
                    var doc = store.Load(); var c = Find(doc, args); var t = c.TitleBar;
                    if (args["color"] is { } col) t.Color = col.ToString();
                    if (args["opacity"] is not null) t.Opacity = Math.Clamp(Int(args, "opacity", t.Opacity), 0, 100);
                    if (args["height"] is not null) t.Height = Math.Clamp(Int(args, "height", t.Height), 16, 96);
                    if (args["fontSize"] is not null) t.FontSize = Math.Clamp(args["fontSize"]!.GetValue<float>(), 6f, 36f);
                    if (args["fontFamily"] is { } ff) t.FontFamily = ff.ToString();
                    store.Save(doc); return JsonSerializer.SerializeToNode(t, ContainersStore.Json);
                }
                case "import-legacy":
                {
                    var root = args["root"]?.ToString() ?? throw new PipeException("bad-args", "root is required");
                    var (docs, report) = LegacyImporter.Import(root, monitors());
                    var dry = args["dryRun"]?.GetValue<bool>() ?? true;
                    if (!dry && docs.Count > 0) store.Save(docs[0]);        // the first (Default) profile; other profiles come with the profile manager
                    return new JsonObject { ["dryRun"] = dry, ["profiles"] = report.Profiles, ["containers"] = report.Containers, ["items"] = report.Items, ["tabs"] = report.Tabs,
                        ["hiddenOriginals"] = report.HiddenOriginals, ["warnings"] = new JsonArray(report.Warnings.Select(w => (JsonNode)JsonValue.Create(w)!).ToArray()), ["notImported"] = new JsonArray(report.NotImported.Take(50).Select(w => (JsonNode)JsonValue.Create(w)!).ToArray()) };
                }
                default: throw new PipeException("unknown-command", $"unknown command '{cmd}'");
            }
        }
    }

    private static Container Find(ContainersDocument doc, JsonObject args)
    {
        var id = args["id"]?.ToString() ?? throw new PipeException("bad-args", "id is required");
        return doc.Containers.FirstOrDefault(c => c.Id == id) ?? throw new PipeException("not-found", $"no container '{id}'");
    }

    private static int Int(JsonObject a, string k, int d) => a[k] is JsonValue v && v.TryGetValue<int>(out var i) ? i : d;
}
