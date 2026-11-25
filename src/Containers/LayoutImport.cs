using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Opsis.Containers;

namespace MotionDesk.Containers;

internal sealed record DesktopLayout(string Name, List<Container> Containers, long Shown = 0, int MonitorMatches = 0, Dictionary<string, string>? PriorAssignments = null);
internal sealed record DesktopFileRule(string Name, string Target, JsonElement[] Conditions, bool Priority, bool Enabled, bool Native = false);
internal sealed record SmartFolderSpec(string[] Folders, bool Recursive, bool Any, string? Name, int? Days, int Limit,
    JsonElement[] Conditions, string? Everything = null);
internal sealed record DesktopOrganization(DesktopLayout[] Layouts, DesktopFileRule[] Rules, string? DefaultTarget, bool KeepUpdated,
    string[] Issues, int Selected = 0, bool RulesEnabled = true, bool KeepUnmatched = false);
internal sealed record OrganizationScreen(string Id, Rectangle WorkArea, Rectangle Bounds, bool Primary);

internal static class LayoutImport
{
    internal const string PackageKey = "opsisPecoImport", OwnerKey = "opsisPecoSource", SmartKey = "opsisPecoSmart",
        TabKey = "opsisPecoTab", InboxKey = "opsisPecoInbox";
    internal static DesktopOrganization? Saved(ContainersDocument doc)
    {
        try { return doc.Extra?.TryGetValue(PackageKey, out var raw) == true ? raw.Deserialize<DesktopOrganization>() : null; }
        catch (JsonException) { return null; }
    }

    internal static bool IsOwned(Container c) => c.Extra?.ContainsKey(OwnerKey) == true;
    internal static bool HasSmart(Container c) => c.Extra?.ContainsKey(SmartKey) == true;
    internal static SmartFolderSpec? SmartSpec(Container container) =>
        HasSmart(container) ? container.Extra![SmartKey].Deserialize<SmartFolderSpec>() : null;
    internal static string? TabTarget(ContainerTab tab) =>
        tab.Extra?.TryGetValue(TabKey, out var raw) == true ? AsString(raw) : null;

    internal static DesktopOrganization Read(string path, IReadOnlyList<OrganizationScreen> screens)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 20 * 1024 * 1024) throw new InvalidDataException("Layout configuration exceeds 20 MB.");
        using var source = JsonDocument.Parse(stream);
        return Read(source.RootElement, screens);
    }

    private const string ExportFormat = "opsis-desktop-layouts";
    internal static string Export(DesktopOrganization organization) => JsonSerializer.Serialize(
        new { format = ExportFormat, version = 1, organization }, new JsonSerializerOptions { WriteIndented = true });

    private static DesktopOrganization ReadExport(JsonElement root)
    {
        if (Number(root, "version", 0) != 1 || Get(root, "organization").ValueKind != JsonValueKind.Object ||
            Get(root, "organization").Deserialize<DesktopOrganization>() is not { } saved ||
            saved.Layouts is not { Length: > 0 and <= 100 } || saved.Rules is not { Length: <= 1000 })
            throw new InvalidDataException("Invalid or unsupported Opsis layout file.");
        foreach (var layout in saved.Layouts)
        {
            if (layout?.Containers is not { Count: <= 500 })
                throw new InvalidDataException("Invalid container list.");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var container in layout.Containers)
            {
                if (container == null || string.IsNullOrWhiteSpace(container.Id) || !ids.Add(container.Id) ||
                    container.Geometry == null || container.Appearance == null || container.TitleBar == null ||
                    container.Tabs == null || container.Items == null ||
                    container.Tabs.Any(tab => tab?.Items == null) ||
                    container.Items.Concat(container.Tabs.SelectMany(tab => tab.Items))
                        .Any(item => item == null || !DesktopFiles.IsLocalPath(item.Path)) ||
                    (container.Kind == ContainerKind.Portal && !DesktopFiles.IsLocalPath(container.Source)) ||
                    (HasSmart(container) && (SmartSpec(container) is not { Folders: not null, Conditions: not null } spec ||
                        spec.Folders.Any(folder => !DesktopFiles.IsLocalPath(folder)))))
                    throw new InvalidDataException("The container list contains invalid or non-local references.");
                (container.Extra ??= new())[OwnerKey] = JsonSerializer.SerializeToElement(true);
            }
        }

        if (saved.Rules.Any(rule => rule == null || string.IsNullOrWhiteSpace(rule.Target) ||
            rule.Conditions is not { Length: > 0 } || !rule.Conditions.All(DesktopFiles.Supports)))
            throw new InvalidDataException("The layout file contains invalid rules.");
        return saved with { Issues = [], Selected = Math.Clamp(saved.Selected, 0, saved.Layouts.Length - 1) };
    }

    internal static DesktopOrganization Read(JsonElement root, IReadOnlyList<OrganizationScreen> screens)
    {
        if (String(root, "format") == ExportFormat) return ReadExport(root);
        if (root.ValueKind != JsonValueKind.Object || Get(root, "layouts").ValueKind != JsonValueKind.Array || screens.Count == 0)
            throw new InvalidDataException("Expected a JSON configuration or backup with layouts.");
        var issues = new HashSet<string>(StringComparer.Ordinal);
        var items = Get(root, "items");
        var layouts = new List<DesktopLayout>();
        var ruleSet = Get(root, "rules");
        AppendLayouts(root, "Imported layout");
        int mainCount = layouts.Count;
        foreach (var snap in Array(root, "snapshots").Take(60))
            AppendLayouts(snap, "Snapshot · " + (String(snap, "name") ?? "Snapshot"));
        if (layouts.Count == 0) throw new InvalidDataException("The file has no importable layouts.");
        var selected = (mainCount > 0 ? layouts.Take(mainCount) : layouts)
            .Select((layout, index) => (layout, index)).OrderByDescending(x => x.layout.MonitorMatches)
            .ThenByDescending(x => x.layout.Shown).First().index;
        var ruleRows = ruleSet.ValueKind == JsonValueKind.Array ? ruleSet.EnumerateArray().ToArray() : Array(ruleSet, "list");
        var rules = new List<DesktopFileRule>();
        foreach (var rule in ruleRows.Take(500))
        {
            var name = String(rule, "name") ?? "Rule";
            var id = Target(Get(rule, "target"));
            var conditions = Array(rule, "allOf").Select(value => value.Clone()).ToArray();
            if (id == null || conditions.Length == 0 || !conditions.All(DesktopFiles.Supports))
            {
                issues.Add($"Rule requires an unsupported condition or target: {name}");
                continue;
            }
            rules.Add(new(name, id, conditions, String(rule, "priorityClass") == "target", Bool(rule, "enabled", true)));
        }
        return new(layouts.ToArray(), rules.OrderByDescending(r => r.Priority).ToArray(), Target(Get(ruleSet, "defaultTarget")),
            Bool(ruleSet, "keepUpdated"), issues.Order().ToArray(), selected, KeepUnmatched: String(ruleSet, "unmatched") == "stay");

        void AppendLayouts(JsonElement section, string name)
        {
            int n = 0;
            var notes = Get(section, "notes").ValueKind == JsonValueKind.Object ? Get(section, "notes") : Get(root, "notes");
            foreach (var layout in Array(section, "layouts").Take(24))
            {
                if (Get(layout, "fences").ValueKind != JsonValueKind.Array) continue;
                var fingerprint = Array(layout, "fingerprint");
                var rows = Array(layout, "fences").Where(f => String(f, "id") is { Length: > 0 }).Take(300).ToArray();
                var models = new Dictionary<string, Container>(StringComparer.OrdinalIgnoreCase);
                var prior = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var fence in rows)
                {
                    string id = String(fence, "id")!;
                    if (models.ContainsKey(id)) continue;
                    var geometry = Get(fence, "geometry");
                    var rect = Place(geometry, fingerprint, screens);
                    var normalized = LegacyImporter.Normalize(rect.X, rect.Y, rect.Width, rect.Height,
                        screens.Select(screen => new MonitorInfo(screen.Id, screen.Bounds, screen.Primary)).ToArray());
                    var kind = String(fence, "kind");
                    var tool = String(Get(fence, "tool"), "kind");
                    var source = String(Get(fence, "source"), "path");
                    var c = new Container
                    {
                        Id = StableId(id), Title = String(fence, "title") ?? "Container",
                        Geometry = normalized, Extra = new(), Locked = Bool(fence, "locked"), RolledUp = Bool(fence, "rolledUp"),
                        ExcludeFromQuickHide = Bool(fence, "excludeFromQuickHide"),
                        UnrolledHeight = Math.Clamp((int)Number(fence, "expandedH", rect.Height), 64, 16000),
                        Kind = tool == "note" ? ContainerKind.Note :
                            kind == "folderPortal" && DesktopFiles.IsLocalPath(source) ? ContainerKind.Portal : ContainerKind.Data
                    };
                    c.Extra![OwnerKey] = JsonSerializer.SerializeToElement(id);
                    if (kind == "inbox") c.Extra[InboxKey] = JsonSerializer.SerializeToElement(true);
                    if (c.Kind == ContainerKind.Note) c.Source = String(notes, id);
                    if (c.Kind == ContainerKind.Portal) c.Source = source;
                    if (tool is "clock" or "script") issues.Add($"Tool not imported: {c.Title} ({tool})");
                    if (kind == "folderPortal" && c.Kind != ContainerKind.Portal) issues.Add($"Portal needs a local folder: {c.Title}");
                    var appearance = Get(fence, "appearance");
                    var view = Get(fence, "view");
                    if (String(view, "layout") is ("list" or "details") and var displayMode)
                        (c.Extra ??= new())["opsisItemView"] = JsonSerializer.SerializeToElement(displayMode);
                    if (Array(fence, "virtualDesktops").Length > 0) issues.Add($"Virtual-desktop binding not imported: {c.Title}");
                    c.Appearance.IconSize = Math.Clamp((int)Number(view, "iconSize", 48), 16, 128);
                    c.Appearance.FrameColor = ColorValue(appearance, "tintRgb");
                    c.Appearance.TintOpacity = Math.Clamp((int)Number(appearance, "opacity", 32), 0, 100);
                    c.TitleBar.Color = ColorValue(appearance, "titleRgb") ?? c.TitleBar.Color;
                    c.TitleBar.FontSize = String(appearance, "titleSize") switch
                    {
                        "small" => 8, "normal" => 10, "large" => 13, "extraLarge" => 16,
                        _ => Math.Clamp((float)Number(appearance, "titleSize", c.TitleBar.FontSize), 6, 36)
                    };
                    c.Appearance.ItemFontSize = Math.Clamp((float)Number(appearance, "textSize", c.Appearance.ItemFontSize), 6, 24);
                    var smart = Get(fence, "smart");
                    if (smart.ValueKind != JsonValueKind.Object && String(Get(fence, "source"), "kind") == "folder" && c.Kind == ContainerKind.Data)
                        smart = JsonSerializer.SerializeToElement(new { folders = new[] { source }, subfolders = Bool(Get(fence, "source"), "recursive"), name = String(Get(fence, "source"), "filter") });
                    if (smart.ValueKind == JsonValueKind.Object)
                    {
                        c.Kind = ContainerKind.Data;
                        var spec = Smart(smart);
                        if (spec.Folders.Length == 0 || !string.IsNullOrWhiteSpace(spec.Everything) || !spec.Conditions.All(DesktopFiles.Supports))
                            issues.Add($"Smart container requires a supported local folder and filters: {c.Title}");
                        c.Extra[SmartKey] = JsonSerializer.SerializeToElement(spec);
                    }
                    var references = Array(fence, "items");
                    foreach (var assigned in references.Where(item => Get(item, "assignedBy").ValueKind == JsonValueKind.Object))
                        if (String(assigned, "itemId") is { } key && ReadItem(Get(items, key), key) is { } item)
                            prior.TryAdd(item.Path, c.Id);
                    c.Items = references
                        .Where(item => Get(item, "assignedBy").ValueKind != JsonValueKind.Object)
                        .OrderBy(item => Number(item, "manualIndex", double.MaxValue))
                        .Select(reference => String(reference, "itemId")).OfType<string>()
                        .Select(itemId => ReadItem(Get(items, itemId), itemId)).Where(item => item != null).Cast<ContainerItem>()
                        .DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase).Take(500).ToList();
                    models.Add(id, c);
                }

                // The Opsis tab UI can hold static items. Portals and smart tabs retain independent windows.
                var hosted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var fence in rows)
                {
                    string original = String(fence, "id")!;
                    if (!models.TryGetValue(original, out var host) || host.Kind != ContainerKind.Data || HasSmart(host)
                        || String(fence, "tabHost") != null) continue;
                    var children = rows.Where(row => String(row, "tabHost") == original)
                        .Select(row => String(row, "id")!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    var valid = children.Where(id => models.TryGetValue(id, out var child) && child.Kind == ContainerKind.Data
                        && !HasSmart(child)).ToArray();
                    if (valid.Length != children.Length) issues.Add($"Smart/portal tab imported as a separate container: {host.Title}");
                    if (valid.Length == 0) continue;
                    var natural = new[] { original }.Concat(valid).ToArray();
                    var order = Array(fence, "tabOrder").Select(value => AsString(value)).OfType<string>()
                        .Where(id => natural.Contains(id)).Concat(natural).Distinct().ToArray();
                    host.Tabs = order.Select(id =>
                    {
                        var tab = new ContainerTab { Name = models[id].Title, Items = models[id].Items, Extra = new() };
                        tab.Extra![TabKey] = JsonSerializer.SerializeToElement(StableId(id));
                        return tab;
                    }).ToList();
                    host.Items = [];
                    host.CurrentTab = Math.Max(0, System.Array.IndexOf(order, String(fence, "activeTab") ?? original));
                    host.TabsEnabled = true;
                    hosted.UnionWith(valid);
                }
                var imported = models.Where(pair => !hosted.Contains(pair.Key)).Select(pair => pair.Value).ToList();
                if (imported.Count > 0)
                    layouts.Add(new($"{name} · {++n}", imported, (long)Number(layout, "shown", 0), fingerprint.Count(identity =>
                        screens.Any(screen => screen.Id.Equals(String(identity, "gdiName") ?? String(identity, "devicePath"),
                            StringComparison.OrdinalIgnoreCase))), prior));
            }
        }
    }

    private static SmartFolderSpec Smart(JsonElement smart)
    {
        var folders = Get(smart, "folders");
        var values = folders.ValueKind == JsonValueKind.Array ? folders.EnumerateArray().Select(AsString).OfType<string>() :
            new[] { AsString(folders) ?? String(smart, "folder") }.OfType<string>();
        return new(values.Where(DesktopFiles.IsLocalPath).Distinct(StringComparer.OrdinalIgnoreCase).Take(24).ToArray(),
            Bool(smart, "subfolders", true), String(smart, "match") == "any", String(smart, "name"),
            Get(smart, "withinDays").TryGetInt32Safe(out int days) ? Math.Clamp(days, 0, 36500) : null,
            Math.Clamp((int)Number(smart, "limit", 200), 1, 500),
            Array(smart, "allOf").Select(value => value.Clone()).ToArray(), String(smart, "everything"));
    }

    internal static Rectangle Place(JsonElement geometry, JsonElement[] fingerprint, IReadOnlyList<OrganizationScreen> screens)
    {
        var monitor = String(geometry, "monitor");
        var identity = fingerprint.FirstOrDefault(value => String(value, "devicePath") == monitor);
        var mapped = String(identity, "gdiName") ?? monitor;
        var area = (screens.FirstOrDefault(s => s.Id.Equals(mapped, StringComparison.OrdinalIgnoreCase))
            ?? screens.FirstOrDefault(s => s.Primary) ?? screens[0]).WorkArea;
        double workW = Math.Clamp(Number(geometry, "workW", area.Width), 1, 16000);
        double workH = Math.Clamp(Number(geometry, "workH", area.Height), 1, 16000);
        double oldW = Math.Clamp(Number(geometry, "w", 320), 80, workW < 80 ? 80 : workW);
        double oldH = Math.Clamp(Number(geometry, "h", 240), 28, workH < 28 ? 28 : workH);
        double x = Number(geometry, "x", 24), y = Number(geometry, "y", 24);
        double scale = Math.Clamp(Math.Min(area.Width / workW, area.Height / workH), .20, 8);
        int w = (int)Math.Clamp(Math.Round(oldW * scale), Math.Min(80, area.Width), area.Width);
        int h = (int)Math.Clamp(Math.Round(oldH * scale), Math.Min(28, area.Height), area.Height);
        string anchor = String(geometry, "anchor") ?? "leftTop";
        double left = anchor.StartsWith("right", StringComparison.Ordinal) ? area.Width - w - (workW - x - oldW) * scale
            : anchor.StartsWith("left", StringComparison.Ordinal) ? x * scale : (x + oldW / 2) / workW * area.Width - w / 2d;
        double top = anchor.EndsWith("Bottom", StringComparison.Ordinal) ? area.Height - h - (workH - y - oldH) * scale
            : anchor.EndsWith("Top", StringComparison.Ordinal) ? y * scale : (y + oldH / 2) / workH * area.Height - h / 2d;
        return new Rectangle(area.X + (int)Math.Clamp(Math.Round(left), 0, area.Width - w),
            area.Y + (int)Math.Clamp(Math.Round(top), 0, area.Height - h), w, h);
    }

    private static ContainerItem? ReadItem(JsonElement value, string id)
    {
        var path = String(value, "path") ?? String(Get(value, "key"), "path") ?? AsString(Get(value, "key"));
        return DesktopFiles.IsLocalPath(path) ? new ContainerItem
        {
            Id = StableId(id), Path = path!, DisplayName = String(value, "displayName") ?? Path.GetFileName(path!),
            IsFolder = Bool(value, "isFolder")
        } : null;
    }

    internal static string StableId(string id) => new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("Opsis.Peco" + "Fence:" + id.ToLowerInvariant()))[..16]).ToString();
    private static string? Target(JsonElement value) => AsString(value) == "inbox" ? "inbox"
        : String(value, "fence") is { } fence ? StableId(fence) : null;

    private static string? ColorValue(JsonElement node, string key)
    {
        var value = Get(node, key);
        if (AsString(value) is { Length: 7 } hex && hex[0] == '#' && hex.AsSpan(1).ToString().All(Uri.IsHexDigit)) return hex;
        return value.TryGetInt32Safe(out int number) && number is >= 0 and <= 0xFFFFFF ? $"#{number:X6}" : null;
    }

    internal static JsonElement Get(JsonElement node, string key) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var value) ? value : default;
    internal static string? AsString(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    internal static string? String(JsonElement node, string key) => AsString(Get(node, key));
    internal static double Number(JsonElement node, string key, double fallback) => Get(node, key) is { ValueKind: JsonValueKind.Number } value
        && value.TryGetDouble(out double number) && double.IsFinite(number) ? number : fallback;
    internal static bool Bool(JsonElement node, string key, bool fallback = false) => Get(node, key).ValueKind switch
    { JsonValueKind.True => true, JsonValueKind.False => false, _ => fallback };
    internal static JsonElement[] Array(JsonElement node, string key) => Get(node, key) is { ValueKind: JsonValueKind.Array } value
        ? value.EnumerateArray().ToArray() : [];
    internal static bool TryGetInt32Safe(this JsonElement node, out int value)
    {
        value = 0;
        return node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out value);
    }
}
