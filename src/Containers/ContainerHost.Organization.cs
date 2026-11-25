using System.Text.Json;
using MotionDesk.Services;
using Opsis.Containers;

namespace MotionDesk.Containers;

internal sealed partial class ContainerHost
{
    private readonly System.Windows.Forms.Timer _organizationRefreshTimer = new() { Interval = 12000 };
    private readonly CancellationTokenSource _organizationShutdown = new();
    private Dictionary<string, List<ContainerItem>> _organizationRoutes = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<ContainerItem>> _organizationSmartResults = new(StringComparer.OrdinalIgnoreCase);
    private bool _organizationBusy, _organizationRefreshRequested, _organizationForceRules;
    private int _organizationRevision;
    private DesktopOrganization? _organizationPackage;

    private static readonly DesktopOrganization EmptyOrganization = new([], [], null, true, [], RulesEnabled: false);

    internal DesktopOrganization? OrganizationStatus => _organizationPackage;

    internal static OrganizationScreen[] OrganizationScreens() =>
        Screen.AllScreens.Select(screen => new OrganizationScreen(screen.DeviceName, screen.WorkingArea, screen.Bounds, screen.Primary)).ToArray();

    private void StartOrganization()
    {
        _organizationPackage = LayoutImport.Saved(_service.Snapshot());
        _organizationRefreshTimer.Tick += (_, _) => RefreshOrganization(forceRules: false);
        _organizationRefreshTimer.Start();
        RefreshOrganization(forceRules: true);
    }

    internal string ExportCurrentLayout(string name)
    {
        Flush();
        var package = _organizationPackage ?? EmptyOrganization;
        return LayoutImport.Export(package with
        {
            Layouts = [new DesktopLayout(name, _service.Snapshot().Containers)],
            Selected = 0, Issues = []
        });
    }

    internal IReadOnlyList<(int Index, string Name)> OtherLayoutTargets() =>
        _organizationPackage == null ? [] : _organizationPackage.Layouts
            .Select((layout, index) => (Index: index, Name: layout.Name))
            .Where(item => item.Index != _organizationPackage.Selected).ToArray();

    internal bool CopyContainerIntoLayout(string id, int targetIndex)
    {
        var package = _organizationPackage;
        if (package == null || targetIndex < 0 || targetIndex >= package.Layouts.Length || targetIndex == package.Selected)
            return false;
        Flush();
        var model = _service.Snapshot().Containers.FirstOrDefault(c => c.Id == id);
        if (model == null) return false;
        // Deep-copy metadata/references. Never copy, move or rewrite files on disk.
        var clone = JsonSerializer.Deserialize<Container>(JsonSerializer.Serialize(model))!;
        (clone.Extra ??= new())[LayoutImport.OwnerKey] = JsonSerializer.SerializeToElement("opsis-workspace-copy");
        var layouts = (DesktopLayout[])package.Layouts.Clone();
        var destination = layouts[targetIndex];
        var models = destination.Containers.Where(c => c.Id != id).ToList();
        models.Add(clone);
        layouts[targetIndex] = destination with { Containers = models };
        var updated = package with { Layouts = layouts };
        _service.Update(doc =>
        {
            (doc.Extra ??= new())[LayoutImport.PackageKey] = JsonSerializer.SerializeToElement(updated);
            return true;
        });
        _organizationPackage = updated;
        return true;
    }

    /// <summary>Create another imported workspace with only the selected container's references,
    /// keeping the current full layout. This is a metadata operation: no user file is copied.</summary>
    internal bool CopyContainerIntoNewLayout(string id, string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80) return false;
        Flush();
        var document = _service.Snapshot();
        var selected = document.Containers.FirstOrDefault(c => c.Id == id);
        if (selected == null) return false;
        var package = _organizationPackage;
        if (package?.Layouts.Length >= 60 ||
            package?.Layouts.Any(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) == true) return false;
        // Deep copies keep paths as references: the originals on disk are untouched.
        Container CloneManaged(Container source)
        {
            var model = JsonSerializer.Deserialize<Container>(JsonSerializer.Serialize(source))!;
            (model.Extra ??= new())[LayoutImport.OwnerKey] = JsonSerializer.SerializeToElement("opsis-workspace");
            return model;
        }
        var clonedTarget = CloneManaged(selected);
        var existingLayouts = package == null
            ? new[] { new DesktopLayout("Current workspace", document.Containers.Select(CloneManaged).ToList()) }
            : package.Layouts;
        if (existingLayouts.Length >= 60) return false;
        var updated = (package ?? EmptyOrganization) with
        {
            Layouts = existingLayouts.Append(new DesktopLayout(name, [clonedTarget])).ToArray(),
            Selected = package?.Selected ?? 0
        };
        try { Backup(force: true); } catch (IOException) { }
        _service.Update(doc =>
        {
            if (package == null)
                foreach (var container in doc.Containers)
                    (container.Extra ??= new())[LayoutImport.OwnerKey] = JsonSerializer.SerializeToElement("opsis-workspace");
            (doc.Extra ??= new())[LayoutImport.PackageKey] = JsonSerializer.SerializeToElement(updated);
            return true;
        });
        _organizationPackage = updated;
        Sync();
        return true;
    }

    internal void ChangeImportedLayout(int layout)
    {
        if (_organizationPackage is not { } active || active.Layouts.Length == 0 || layout == active.Selected) return;
        Flush();
        var layouts = (DesktopLayout[])active.Layouts.Clone();
        layouts[active.Selected] = layouts[active.Selected] with
        {
            Containers = _service.Snapshot().Containers.Where(LayoutImport.IsOwned).ToList()
        };
        ImportLayouts(active with { Layouts = layouts }, layout, switching: true);
    }

    internal void ImportLayouts(DesktopOrganization package, int layout, bool switching = false)
    {
        if (layout < 0 || layout >= package.Layouts.Length) throw new ArgumentOutOfRangeException(nameof(layout));
        Flush();
        Backup(force: true);
        var localRules = _organizationPackage?.Rules.Where(rule => rule.Native).ToArray() ?? [];
        var selected = package with
        {
            Selected = layout,
            Rules = switching ? package.Rules : package.Rules.Concat(localRules)
                .DistinctBy(rule => (rule.Name, rule.Target, string.Join(",", rule.Conditions.Select(c => c.GetRawText())))).ToArray(),
            RulesEnabled = package.RulesEnabled || (_organizationPackage?.RulesEnabled == true && localRules.Length > 0)
        };
        // Exchanging imported layouts leaves the independently created Opsis containers in place.
        var imported = selected.Layouts[layout].Containers
            .Select(c => JsonSerializer.Deserialize<Container>(JsonSerializer.SerializeToElement(c))!).ToList();
        var importedIds = imported.Select(container => container.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _service.Update(doc =>
        {
            doc.Containers.RemoveAll(container => LayoutImport.IsOwned(container) || importedIds.Contains(container.Id));
            doc.Containers.AddRange(imported);
            (doc.Extra ??= new())[LayoutImport.PackageKey] = JsonSerializer.SerializeToElement(selected);
            return true;
        });
        _organizationPackage = selected;
        ReplaceOrganizationWindows(container => LayoutImport.IsOwned(container) || importedIds.Contains(container.Id));
    }

    private void ReplaceOrganizationWindows(Func<Container, bool> replace)
    {
        foreach (var (id, window) in _windows.Where(pair => replace(pair.Value.Model)).ToArray())
        {
            _windows.Remove(id);
            window.Close();
            window.Dispose();
            _dirty.Remove(id);
        }
        _organizationRevision++;
        _organizationRoutes = new(StringComparer.OrdinalIgnoreCase);
        _organizationSmartResults = new(StringComparer.OrdinalIgnoreCase);
        Sync();
        ShowOrganizationResults();
        RefreshOrganization(forceRules: true);
    }

    internal void EnableFileRules(bool enabled) => UpdateFileRules(package => package with { RulesEnabled = enabled });

    internal void SaveFileRules(DesktopFileRule[] rules, bool keepUpdated, string? defaultTarget, bool keepUnmatched) =>
        UpdateFileRules(package => package with
        {
            KeepUpdated = keepUpdated,
            DefaultTarget = defaultTarget,
            KeepUnmatched = keepUnmatched,
            Rules = rules,
            RulesEnabled = (package.Rules.Length == 0 && package.DefaultTarget == null && (rules.Length > 0 || defaultTarget != null)) || package.RulesEnabled
        });

    internal void SetSmartFolder(string id, string title, SmartFolderSpec spec)
    {
        _organizationRevision++;
        Apply(id, container =>
        {
            container.Title = title;
            (container.Extra ??= new())[LayoutImport.SmartKey] = JsonSerializer.SerializeToElement(spec);
            return true;
        });
        RefreshOrganization(forceRules: true);
    }

    private void UpdateFileRules(Func<DesktopOrganization, DesktopOrganization> change)
    {
        var package = _organizationPackage ?? EmptyOrganization;
        var updated = change(package);
        _service.Update(doc => { (doc.Extra ??= new())[LayoutImport.PackageKey] = JsonSerializer.SerializeToElement(updated); return true; });
        _organizationPackage = updated;
        _organizationRevision++;
        if (!updated.RulesEnabled)
        {
            _organizationRoutes = new(StringComparer.OrdinalIgnoreCase);
            ShowOrganizationResults();
        }
        RefreshOrganization(forceRules: updated.RulesEnabled);
    }

    internal async void RefreshOrganization(bool forceRules)
    {
        if (_organizationShutdown.IsCancellationRequested) return;
        var package = _organizationPackage ?? EmptyOrganization;
        if (_organizationBusy)
        {
            _organizationRefreshRequested |= forceRules;
            _organizationForceRules |= forceRules;
            return;
        }
        _organizationBusy = true;
        int revision = _organizationRevision;
        var token = _organizationShutdown.Token;
        try
        {
            var models = _service.Snapshot().Containers;
            var specs = models.Where(LayoutImport.HasSmart)
                .Select(c => (c.Id, Spec: c.Extra![LayoutImport.SmartKey].Deserialize<SmartFolderSpec>())).ToArray();
            if (specs.Length == 0 && !package.RulesEnabled && _organizationSmartResults.Count == 0) return;
            var prior = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (package.KeepUnmatched)
            {
                var initial = package.Layouts.ElementAtOrDefault(package.Selected)?.PriorAssignments;
                if (initial != null) foreach (var (path, target) in initial) prior[path] = target;
                foreach (var (target, items) in _organizationRoutes)
                    foreach (var item in items) prior[item.Path] = target;
            }
            bool route = package.RulesEnabled && (forceRules || package.KeepUpdated);
            var result = await Task.Run(() =>
            {
                var now = DateTime.UtcNow;
                var smart = specs.Where(a => a.Spec != null)
                    .ToDictionary(a => a.Id, a => DesktopFiles.Search(a.Spec!, now, token), StringComparer.OrdinalIgnoreCase);
                var rules = route ? DesktopFiles.Route(package, models,
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), token, prior) : null;
                return (smart, rules);
            }, token);
            if (token.IsCancellationRequested || revision != _organizationRevision) return;
            _organizationSmartResults = result.smart;
            if (result.rules != null) _organizationRoutes = result.rules;
            ShowOrganizationResults();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { RotatingLogger.Write("Desktop organization refresh: " + ex.Message); }
        finally
        {
            _organizationBusy = false;
            if (_organizationRefreshRequested && !token.IsCancellationRequested)
            {
                bool pending = _organizationForceRules;
                _organizationRefreshRequested = _organizationForceRules = false;
                RefreshOrganization(pending);
            }
        }
    }

    private void ShowOrganizationResults()
    {
        foreach (var w in _windows.Values) ApplyOrganizationResults(w);
    }

    private void ApplyOrganizationResults(ContainerWindow window)
    {
        _organizationSmartResults.TryGetValue(window.Model.Id, out var smart);
        window.SetOrganizationItems(smart, _organizationRoutes);
    }

    private void StopOrganization()
    {
        _organizationRefreshTimer.Dispose();
        _organizationShutdown.Cancel();
        _organizationShutdown.Dispose();
    }
}
