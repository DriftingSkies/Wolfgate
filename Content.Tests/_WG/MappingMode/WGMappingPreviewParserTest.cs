using System;
using System.IO;
using System.Linq;
using Content.Server._WG.MappingMode;
using NUnit.Framework;
using Robust.Shared.Maths;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;

namespace Content.Tests._WG.MappingMode;

[TestFixture]
public sealed class WGMappingPreviewParserTest
{
    private const int ChunkSize = 16;

    /// <summary>A version 7 chunk with plating (id 1) on the given cells, space (id 0) on the rest.</summary>
    private static string Chunk(params (int X, int Y)[] plating)
    {
        const int stride = 7;
        var data = new byte[ChunkSize * ChunkSize * stride];
        foreach (var (x, y) in plating)
        {
            data[(y * ChunkSize + x) * stride] = 1;
        }

        return Convert.ToBase64String(data);
    }

    private static MappingDataNode Root(string yaml)
    {
        return (MappingDataNode) DataNodeParser.ParseYamlStream(new StringReader(yaml)).Single().Root;
    }

    private static string MapYaml(string tiles, string version = "7")
    {
        return $@"tilemap:
  0: Space
  1: Plating
entities:
- proto: """"
  entities:
  - uid: 1
    components:
    - type: Map
  - uid: 2
    components:
    - type: Transform
      pos: 3,4
      rot: 1.5 rad
      parent: 1
    - type: MapGrid
      chunks:
        1,0:
          ind: 1,0
          tiles: {tiles}
          version: {version}
- proto: TestWall
  entities:
  - uid: 3
    components:
    - type: Transform
      pos: 17.5,2.5
      parent: 2
  - uid: 4
    components:
    - type: Transform
      pos: 0.5,0.5
      parent: 9
- proto: TestDoor
  entities:
  - uid: 5
    components:
    - type: Transform
      pos: 18.5,2.5
      parent: 2
- proto: TestItem
  entities:
  - uid: 6
    components:
    - type: Transform
      pos: 18.5,2.5
      parent: 2
";
    }

    private static WGMappingPreviewCell Classify(string proto)
    {
        return proto switch
        {
            "TestWall" => WGMappingPreviewCell.Wall,
            "TestDoor" => WGMappingPreviewCell.Opening,
            _ => WGMappingPreviewCell.None,
        };
    }

    [Test]
    public void ReadsFloorBlockersAndPlacement()
    {
        var yaml = MapYaml(Chunk((1, 2), (2, 2)));
        var preview = WGMappingPreviewParser.Parse(Root(yaml), 123, Classify);

        Assert.Multiple(() =>
        {
            Assert.That(preview.Bytes, Is.EqualTo(123));
            Assert.That(preview.Tiles, Is.EqualTo(2));
            Assert.That(preview.Entities, Is.EqualTo(6));
            Assert.That(preview.Grids, Has.Count.EqualTo(1));
        });

        var grid = preview.Grids[0];
        Assert.Multiple(() =>
        {
            Assert.That(grid.Offset.X, Is.EqualTo(3f));
            Assert.That(grid.Offset.Y, Is.EqualTo(4f));
            Assert.That(grid.Rotation.Theta, Is.EqualTo(1.5).Within(1e-6));
            Assert.That(grid.Floor, Is.EqualTo(new[] { new Box2i(17, 2, 19, 3) }), "Chunk 1,0 starts at x = 16.");
            Assert.That(grid.Walls, Is.EqualTo(new[] { new Box2i(17, 2, 18, 3) }), "A wall off the grid isn't drawn.");
            Assert.That(grid.Openings, Is.EqualTo(new[] { new Box2i(18, 2, 19, 3) }));
        });
    }

    [Test]
    public void ShortChunkIsSkipped()
    {
        var yaml = MapYaml(Convert.ToBase64String(new byte[10]));
        var preview = WGMappingPreviewParser.Parse(Root(yaml), 0, Classify);
        Assert.That(preview.Tiles, Is.Zero);
    }

    [Test]
    public void OldChunkFormatReads()
    {
        // Version 5: a two byte id, flags and variant.
        var data = new byte[ChunkSize * ChunkSize * 4];
        data[0] = 1;
        var yaml = MapYaml(Convert.ToBase64String(data), "5");
        var preview = WGMappingPreviewParser.Parse(Root(yaml), 0, Classify);
        Assert.That(preview.Grids[0].Floor, Is.EqualTo(new[] { new Box2i(16, 0, 17, 1) }));
    }

    [Test]
    public void NoGridsGivesEmptyPreview()
    {
        var preview = WGMappingPreviewParser.Parse(Root("tilemap: {}\nentities: []\n"), 0, Classify);
        Assert.That(preview.Grids, Is.Empty);
    }
}
