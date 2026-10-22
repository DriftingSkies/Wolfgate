#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client.Guidebook;
using Content.Client.Guidebook.Controls;
using Content.Client.Guidebook.Richtext;
using Content.Shared._WF.MappingMode;
using Content.Shared.Guidebook;
using Robust.Client.UserInterface;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>The mapping guide's pages parse without an inline error, and every page hangs off the root.</summary>
[TestFixture]
public sealed class WFMappingGuideTest
{
    [Test]
    public async Task PagesParseCleanly()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        await client.WaitIdleAsync();
        var prototypes = client.ResolveDependency<IPrototypeManager>();
        var resources = client.ResolveDependency<IResourceManager>();
        var parser = client.ResolveDependency<DocumentParsingManager>();

        var root = prototypes.Index(WFMappingGuide.Root);
        var pages = new List<ProtoId<GuideEntryPrototype>> { WFMappingGuide.Root };
        pages.AddRange(root.Children);

        var ours = prototypes.EnumeratePrototypes<GuideEntryPrototype>()
            .Where(p => p.ID.StartsWith("WFMappingGuide"))
            .Select(p => new ProtoId<GuideEntryPrototype>(p.ID))
            .ToList();
        Assert.That(ours, Is.EquivalentTo(pages), "Every mapping guide page is the root or one of its children.");

        foreach (var id in pages)
        {
            await client.WaitAssertion(() =>
            {
                var page = prototypes.Index(id);
                using var reader = resources.ContentFileReadText(page.Text);
                var document = new Document();
                Assert.That(parser.TryAddMarkup(document, reader.ReadToEnd()), $"{id} doesn't parse.");
                Assert.That(Errors(document), Is.Empty, $"{id} has a guidebook error in it.");
                if (id != WFMappingGuide.Root)
                    Assert.That(page.Children, Is.Empty, $"{id} is a leaf page.");
            });

            await client.WaitRunTicks(1);
        }

        await pair.CleanReturnAsync();
    }

    private static List<GuidebookError> Errors(Control control)
    {
        var found = new List<GuidebookError>();
        foreach (var child in control.Children)
        {
            if (child is GuidebookError error)
                found.Add(error);

            found.AddRange(Errors(child));
        }

        return found;
    }
}
