using System.IO;
using System.Linq;
using Content.Server._WG.MappingMode;
using Content.Shared._NF.Shipyard.Prototypes;
using NUnit.Framework;
using YamlDotNet.RepresentationModel;

namespace Content.Tests._WG.MappingMode;

[TestFixture]
public sealed class WGMappingVesselYamlTest
{
    [TestCase("test", "WGTest", "test")]
    [TestCase("my cool-ship 2", "WGMyCoolShip2", "my_cool_ship_2")]
    [TestCase("MMC Dredger", "WGMMCDredger", "mmc_dredger")]
    [TestCase("  Bob_s   Barge ", "WGBobSBarge", "bob_s_barge")]
    [TestCase("___", "WGShip", "ship")]
    public void IdsAndFileNames(string mapName, string id, string fileName)
    {
        Assert.Multiple(() =>
        {
            Assert.That(WGMappingVesselYaml.VesselId(mapName), Is.EqualTo(id));
            Assert.That(WGMappingVesselYaml.FileName(mapName), Is.EqualTo(fileName));
            Assert.That(WGMappingVesselYaml.ShuttlePath(mapName), Is.EqualTo($"/SharedMaps/_WG/Community/{fileName}.yml"));
        });
    }

    [TestCase(0, VesselSize.Micro)]
    [TestCase(49, VesselSize.Micro)]
    [TestCase(50, VesselSize.Small)]
    [TestCase(199, VesselSize.Small)]
    [TestCase(200, VesselSize.Medium)]
    [TestCase(499, VesselSize.Medium)]
    [TestCase(500, VesselSize.Large)]
    public void SizeFromTiles(int tiles, VesselSize size)
    {
        Assert.That(WGMappingVesselYaml.SizeFor(tiles), Is.EqualTo(size));
    }

    [TestCase(0, 1000)]
    [TestCase(double.NaN, 1000)]
    [TestCase(400, 1000)]
    [TestCase(7999, 10000)]
    [TestCase(15000, 19000)]
    [TestCase(60000, 75000)]
    [TestCase(60001, 76000)]
    [TestCase(80000, 100000)]
    [TestCase(80001, 105000)]
    public void PriceRoundsUpOverTheAppraisal(double appraisal, int price)
    {
        Assert.That(WGMappingVesselYaml.PriceFor(appraisal), Is.EqualTo(price));
    }

    [TestCase("PortableGeneratorSuperPacmanShuttle", VesselEngine.Uranium)]
    [TestCase("PortableGeneratorPacmanShuttle", VesselEngine.Plasma)]
    [TestCase("PortableGeneratorJrPacman", VesselEngine.Welding)]
    [TestCase("PortableGeneratorDKJrShuttle", VesselEngine.Bananium)]
    [TestCase("GeneratorCRPinchShuttle", VesselEngine.Plasma)]
    [TestCase("AmeController", VesselEngine.AME)]
    [TestCase("GeneratorWallmountAPU", VesselEngine.APU)]
    [TestCase("GeneratorRTG", VesselEngine.RTG)]
    [TestCase("NuclearReactorSmall", VesselEngine.NFR)]
    [TestCase("SolarPanel", VesselEngine.Solar)]
    public void EnginesFromIds(string id, VesselEngine engine)
    {
        Assert.That(WGMappingVesselYaml.EngineFor(id), Is.EqualTo(engine));
    }

    [Test]
    public void NotEverythingIsAnEngineOrMiningGear()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WGMappingVesselYaml.EngineFor("AmeJar"), Is.Null);
            Assert.That(WGMappingVesselYaml.EngineFor("Thruster"), Is.Null);
            Assert.That(WGMappingVesselYaml.IsMiningId("OreProcessor"), Is.True);
            Assert.That(WGMappingVesselYaml.IsMiningId("MiningDrill"), Is.True);
            Assert.That(WGMappingVesselYaml.IsMiningId("GasMiningDrill"), Is.False);
            Assert.That(WGMappingVesselYaml.IsMiningId("WallSolid"), Is.False);
        });
    }

    [Test]
    public void ClassesFollowWhatWasFound()
    {
        Assert.That(WGMappingVesselYaml.ClassesFor(new WGMappingVesselScan()), Is.EqualTo(new[] { VesselClass.Civilian }));
        Assert.That(WGMappingVesselYaml.ClassesFor(new WGMappingVesselScan { CargoPallet = true, MountedGuns = true }),
            Is.EqualTo(new[] { VesselClass.Cargo, VesselClass.Mercenary }));
        Assert.That(WGMappingVesselYaml.ClassesFor(new WGMappingVesselScan { Mining = true }),
            Is.EqualTo(new[] { VesselClass.Salvage }));
    }

    [Test]
    public void BuildWritesAVesselAndItsGameMap()
    {
        var scan = new WGMappingVesselScan { Tiles = 120, Appraisal = 9000, Mining = true };
        scan.Engines.Add(VesselEngine.Uranium);
        scan.Engines.Add(VesselEngine.AME);
        var text = WGMappingVesselYaml.Build("Bob's Barge", scan,
            new WGMappingVesselText(new[] { "line one", "line\ntwo" }, "It's a: barge"));

        var lines = text.Split('\n');
        Assert.That(lines[0], Is.EqualTo("# line one"));
        Assert.That(lines[1], Is.EqualTo("# line two"), "A header line can't break out of its comment.");

        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        var root = (YamlSequenceNode) stream.Documents[0].RootNode;
        Assert.That(root.Children, Has.Count.EqualTo(2));

        var vessel = (YamlMappingNode) root.Children[0];
        string Scalar(YamlMappingNode node, string key) => ((YamlScalarNode) node[new YamlScalarNode(key)]).Value!;
        Assert.Multiple(() =>
        {
            Assert.That(Scalar(vessel, "type"), Is.EqualTo("vessel"));
            Assert.That(Scalar(vessel, "id"), Is.EqualTo("WGBobSBarge"));
            Assert.That(Scalar(vessel, "name"), Is.EqualTo("Bob's Barge"));
            Assert.That(Scalar(vessel, "description"), Is.EqualTo("It's a: barge"));
            Assert.That(Scalar(vessel, "price"), Is.EqualTo("11500"));
            Assert.That(Scalar(vessel, "category"), Is.EqualTo("Small"));
            Assert.That(Scalar(vessel, "group"), Is.EqualTo("Shipyard"));
            Assert.That(Scalar(vessel, "shuttlePath"), Is.EqualTo("/SharedMaps/_WG/Community/bob_s_barge.yml"));
            var classes = (YamlSequenceNode) vessel[new YamlScalarNode("class")];
            Assert.That(classes.Children.Select(c => ((YamlScalarNode) c).Value), Is.EqualTo(new[] { "Salvage" }));
            var engines = (YamlSequenceNode) vessel[new YamlScalarNode("engine")];
            Assert.That(engines.Children.Select(c => ((YamlScalarNode) c).Value), Is.EqualTo(new[] { "AME", "Uranium" }));

            var gameMap = (YamlMappingNode) root.Children[1];
            Assert.That(Scalar(gameMap, "type"), Is.EqualTo("gameMap"));
            Assert.That(Scalar(gameMap, "id"), Is.EqualTo("WGBobSBarge"));
            Assert.That(Scalar(gameMap, "mapPath"), Is.EqualTo("/SharedMaps/_WG/Community/bob_s_barge.yml"));
        });
    }
}
