using Opsis.Containers;
using Xunit;

namespace Opsis.Containers.Tests;

public sealed class SearchTests
{
    [Fact]
    public void FindsByNameAcrossContainersAndTabsAndPutsPrefixMatchesFirst()
    {
        var doc = new ContainersDocument();
        var a = new Container { Id = "a", Title = "Tools" }; a.Items.Add(new ContainerItem { Path = @"C:\x\Notepad.exe", DisplayName = "Notepad" }); a.Items.Add(new ContainerItem { Path = @"C:\x\My notes.txt", DisplayName = "My notes" });
        var b = new Container { Id = "b", Title = "Docs", TabsEnabled = true }; b.Tabs.Add(new ContainerTab { Name = "T", Items = { new ContainerItem { Path = @"C:\d\Notes 2026.docx" } } });
        doc.Containers.Add(a); doc.Containers.Add(b); doc.Containers.Add(new Container { Id = "p", Kind = ContainerKind.Portal, Items = { new ContainerItem { Path = @"C:\z\notes.md" } } });
        var hits = ContainerSearch.Find(doc, "note");
        Assert.Equal(3, hits.Count);                                       // the portal is not searched (a live view, not data)
        Assert.StartsWith("Note", hits[0].Item.DisplayName.Length > 0 ? hits[0].Item.DisplayName : System.IO.Path.GetFileName(hits[0].Item.Path));
        Assert.Empty(ContainerSearch.Find(doc, "  "));
    }
}
