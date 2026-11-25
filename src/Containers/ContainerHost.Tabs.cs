using System.Text.Json;
using Opsis.Containers;

namespace MotionDesk.Containers;

internal sealed partial class ContainerHost
{
    internal static bool SupportsTabs(Container item) =>
        item.Kind == ContainerKind.Data && !LayoutImport.HasSmart(item);

    internal static void EnsureTabTargets(Container model)
    {
        for (int i = 0; i < model.Tabs.Count; i++)
        {
            var tab = model.Tabs[i];
            if (LayoutImport.TabTarget(tab) != null) continue;
            string target = i == 0 ? model.Id : Guid.NewGuid().ToString();
            (tab.Extra ??= new())[LayoutImport.TabKey] = JsonSerializer.SerializeToElement(target);
        }
    }

    private static List<ContainerTab> TabsOf(Container model)
    {
        if (model.TabsEnabled && model.Tabs.Count > 0)
        {
            EnsureTabTargets(model);
            return model.Tabs;
        }
        return [new ContainerTab
        {
            Name = model.Title,
            Items = model.Items,
            Extra = new() { [LayoutImport.TabKey] = JsonSerializer.SerializeToElement(model.Id) }
        }];
    }

    internal void MergeIntoTabs(string sourceId, string destinationId)
    {
        if (sourceId == destinationId) return;
        Flush();
        bool changed = false;
        _service.Update(document =>
        {
            var source = document.Containers.FirstOrDefault(item => item.Id == sourceId);
            var destination = document.Containers.FirstOrDefault(item => item.Id == destinationId);
            if (source == null || destination == null || !SupportsTabs(source) || !SupportsTabs(destination)) return false;
            var tabs = TabsOf(destination).ToList();
            destination.CurrentTab = tabs.Count;
            tabs.AddRange(TabsOf(source));
            destination.Tabs = tabs;
            destination.TabsEnabled = true;
            destination.Items = [];
            document.Containers.Remove(source);
            changed = true;
            return true;
        });
        if (changed) ReplaceOrganizationWindows(container => container.Id == sourceId || container.Id == destinationId);
    }

    internal ContainerWindow? TabTargetAt(Point point)
    {
        var order = DesktopZOrder.Chain();
        return _windows.Values.Where(window => window.IsHandleCreated && window.TitleStripContains(point))
            .OrderBy(window => { int i = order.IndexOf(window.Handle); return i < 0 ? int.MaxValue : i; }).FirstOrDefault();
    }

    internal void TransferTab(string sourceId, int index, string targetId, int slot)
    {
        if (sourceId == targetId) return;
        Flush();
        bool changed = false;
        _service.Update(document =>
        {
            var source = document.Containers.FirstOrDefault(item => item.Id == sourceId);
            var target = document.Containers.FirstOrDefault(item => item.Id == targetId);
            if (source == null || target == null || !SupportsTabs(source) || !SupportsTabs(target) ||
                !source.TabsEnabled || index < 0 || index >= source.Tabs.Count) return false;
            EnsureTabTargets(source);
            var tab = source.Tabs[index];
            var targetTabs = TabsOf(target).ToList();
            int at = Math.Clamp(slot, 0, targetTabs.Count);
            if (!target.TabsEnabled || target.Tabs.Count == 0) at = targetTabs.Count;
            targetTabs.Insert(at, tab);
            target.Tabs = targetTabs;
            target.TabsEnabled = true;
            target.Items = [];
            target.CurrentTab = at;
            source.Tabs.RemoveAt(index);
            if (source.Tabs.Count == 0) document.Containers.Remove(source);
            else source.CurrentTab = Math.Clamp(source.CurrentTab > index ? source.CurrentTab - 1 : source.CurrentTab, 0, source.Tabs.Count - 1);
            changed = true;
            return true;
        });
        if (changed) ReplaceOrganizationWindows(container => container.Id == sourceId || container.Id == targetId);
    }

    internal void DetachTab(string containerId, int index, Point? placement = null)
    {
        Flush();
        bool changed = false;
        _service.Update(document =>
        {
            var source = document.Containers.FirstOrDefault(item => item.Id == containerId);
            if (source == null || !SupportsTabs(source) || !source.TabsEnabled || source.Tabs.Count <= 1 ||
                index < 0 || index >= source.Tabs.Count) return false;
            EnsureTabTargets(source);
            var tab = source.Tabs[index];
            var clone = JsonSerializer.Deserialize<Container>(JsonSerializer.SerializeToElement(source))!;
            string? previousTarget = LayoutImport.TabTarget(tab);
            clone.Id = previousTarget != null && document.Containers.All(item => item.Id != previousTarget)
                ? previousTarget : Guid.NewGuid().ToString();
            clone.Title = tab.Name;
            clone.Items = tab.Items;
            clone.Tabs = [];
            clone.TabsEnabled = false;
            clone.CurrentTab = 0;
            clone.RolledUp = false;
            clone.Hidden = false;
            var raw = LegacyImporter.Denormalize(source.Geometry, Monitors());
            var position = placement ?? new Point(raw.X + 38, raw.Y + 38);
            clone.Geometry = LegacyImporter.Normalize(position.X, position.Y, raw.Width, raw.Height, Monitors());

            source.Tabs.RemoveAt(index);
            source.CurrentTab = Math.Clamp(source.CurrentTab > index ? source.CurrentTab - 1 : source.CurrentTab, 0, source.Tabs.Count - 1);
            document.Containers.Add(clone);
            changed = true;
            return true;
        });
        if (changed) ReplaceOrganizationWindows(container => container.Id == containerId);
    }
}
