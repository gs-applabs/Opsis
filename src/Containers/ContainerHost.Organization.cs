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
