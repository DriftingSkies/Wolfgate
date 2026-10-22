using System.Collections.Generic;
using System.IO;
using System.Linq;
using Content.Server._WF.MappingMode;
using NUnit.Framework;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Tests._WF.MappingMode;

[TestFixture]
public sealed class WFMappingUploadValidatorTest
{
    // A map (1) with a grid (2) holding a locker (6). In the locker sit an entity of a missing prototype (3) and a
    // wrench (7); a crowbar (4) is parented to the missing one, and a screwdriver (5) to the crowbar.
    private const string Map = @"meta:
  format: 7
  category: Map
  entityCount: 7
maps:
- 1
grids:
- 2
orphans: []
nullspace: []
tilemap: {}
entities:
- proto: """"
  entities:
  - uid: 1
    components:
    - type: Map
  - uid: 2
    components:
    - type: Transform
      parent: 1
- proto: WFMissingThing
  entities:
  - uid: 3
    components:
    - type: Transform
      parent: 6
- proto: Crowbar
  entities:
  - uid: 4
    components:
    - type: Transform
      parent: 3
- proto: Screwdriver
  entities:
  - uid: 5
    components:
    - type: Transform
      parent: 4
- proto: Locker
  entities:
  - uid: 6
    components:
    - type: Transform
      parent: 2
    - type: ContainerContainer
      containers:
        entity_storage: !type:Container
          ents:
          - 3
          - 7
        paper_label: !type:ContainerSlot
          ent: 3
- proto: Wrench
  entities:
  - uid: 7
    components:
    - type: Transform
      parent: 6
";

    private static MappingDataNode Parse()
    {
        return (MappingDataNode) DataNodeParser.ParseYamlStream(new StringReader(Map)).Single().Root!;
    }

    private static List<string> Protos(MappingDataNode root)
    {
        return ((SequenceDataNode) root["entities"]).Cast<MappingDataNode>()
            .Select(g => ((ValueDataNode) g["proto"]).Value)
            .ToList();
    }

    private static List<int> Uids(MappingDataNode root)
    {
        return ((SequenceDataNode) root["entities"]).Cast<MappingDataNode>()
            .SelectMany(g => ((SequenceDataNode) g["entities"]).Cast<MappingDataNode>())
            .Select(e => int.Parse(((ValueDataNode) e["uid"]).Value))
            .Order()
            .ToList();
    }

    [Test]
    public void StripsMissingAndTheirChildren()
    {
        var root = Parse();
        WFMappingUploadValidator.StripMissing(root, new HashSet<string> { "WFMissingThing" }, out var dropped);

        var locker = ((SequenceDataNode) root["entities"]).Cast<MappingDataNode>()
            .Single(g => ((ValueDataNode) g["proto"]).Value == "Locker");
        var lockerEntity = (MappingDataNode) ((SequenceDataNode) locker["entities"])[0];
        var containers = (MappingDataNode) ((MappingDataNode) ((SequenceDataNode) lockerEntity["components"])[1])["containers"];
        var storage = (SequenceDataNode) ((MappingDataNode) containers["entity_storage"])["ents"];
        var slot = (ValueDataNode) ((MappingDataNode) containers["paper_label"])["ent"];

        Assert.Multiple(() =>
        {
            Assert.That(dropped, Is.EqualTo(3), "The missing one, its child and its grandchild.");
            Assert.That(Uids(root), Is.EqualTo(new[] { 1, 2, 6, 7 }));
            Assert.That(Protos(root), Is.EqualTo(new[] { "", "Locker", "Wrench" }), "Emptied groups go too.");
            Assert.That(storage.Cast<ValueDataNode>().Select(v => v.Value), Is.EqualTo(new[] { "7" }));
            Assert.That(slot.IsNull, Is.True);
            Assert.That(((ValueDataNode) ((MappingDataNode) root["meta"])["entityCount"]).Value, Is.EqualTo("4"));
            Assert.That(((SequenceDataNode) root["maps"]).Count, Is.EqualTo(1));
            Assert.That(((SequenceDataNode) root["grids"]).Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void DroppedGridTakesItsContents()
    {
        var root = Parse();
        var grid = (MappingDataNode) ((SequenceDataNode) ((MappingDataNode) ((SequenceDataNode) root["entities"])[0])["entities"])[1];
        ((SequenceDataNode) ((MappingDataNode) ((SequenceDataNode) root["entities"])[0])["entities"]).Remove(grid);
        ((SequenceDataNode) root["entities"]).Add(new MappingDataNode
        {
            { "proto", new ValueDataNode("WFMissingGrid") },
            { "entities", new SequenceDataNode(grid) },
        });

        WFMappingUploadValidator.StripMissing(root, new HashSet<string> { "WFMissingGrid" }, out var dropped);

        Assert.Multiple(() =>
        {
            Assert.That(dropped, Is.EqualTo(6));
            Assert.That(Uids(root), Is.EqualTo(new[] { 1 }));
            Assert.That(((SequenceDataNode) root["grids"]).Count, Is.Zero);
        });
    }

    [Test]
    public void NothingMissingChangesNothing()
    {
        var root = Parse();
        WFMappingUploadValidator.StripMissing(root, new HashSet<string> { "WFNotInTheFile" }, out var dropped);

        Assert.Multiple(() =>
        {
            Assert.That(dropped, Is.Zero);
            Assert.That(Uids(root), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6, 7 }));
            Assert.That(((ValueDataNode) ((MappingDataNode) root["meta"])["entityCount"]).Value, Is.EqualTo("7"));
        });
    }
}
