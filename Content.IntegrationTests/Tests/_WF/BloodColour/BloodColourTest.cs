using System.Linq;
using Content.Server._WF.BloodColour;
using Content.Server._WF.Wolfmed.Gore;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Database;
using Content.Shared._WF.BloodColour;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WF.BloodColour;

/// <summary>
/// The profile keeps and saves a blood colour, a loaded mob's blood takes it (and gore with it), and clearing it gives
/// the reagent's colour back.
/// </summary>
[TestFixture]
[TestOf(typeof(WFBloodColourSystem))]
public sealed class BloodColourTest
{
    private static readonly Color Green = Color.FromHex("#20C040");
    private static readonly Color Blue = Color.FromHex("#2040E0");

    [Test]
    public async Task ProfileTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var db = GetDb(server);
        var user = new NetUserId(Guid.NewGuid());
        HumanoidCharacterProfile green = default!;

        await server.WaitAssertion(() =>
        {
            var human = HumanoidCharacterProfile.RandomWithSpecies("Human");
            green = (HumanoidCharacterProfile) human.WithBloodColor(Green.WithAlpha(0.2f))
                .Validated(pair.Player!, IoCManager.Instance!);

            Assert.Multiple(() =>
            {
                Assert.That(human.BloodColor, Is.Null, "Random profiles should keep the species' blood.");
                Assert.That(green.BloodColor, Is.EqualTo(Green), "The colour should be kept, made opaque.");
                Assert.That(green.Clone().BloodColor, Is.EqualTo(Green), "Copies drop the colour.");
                Assert.That(green.MemberwiseEquals(green.WithBloodColor(Blue)), Is.False);
                Assert.That(green.MemberwiseEquals(green.WithBloodColor(null)), Is.False);
                Assert.That(WFBloodColourRules.FromStored(WFBloodColourRules.ToStored(Green)), Is.EqualTo(Green));
                Assert.That(WFBloodColourRules.FromStored("nonsense"), Is.Null);
            });
        });

        await db.InitPrefsAsync(user, green);
        var prefs = await db.GetPlayerPreferencesAsync(user);
        var loaded = (HumanoidCharacterProfile) prefs!.Characters.Single().Value;
        Assert.That(loaded.BloodColor, Is.EqualTo(Green), "The colour was not saved.");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ApplyTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var humanoid = entMan.System<SharedHumanoidAppearanceSystem>();
            var solutions = entMan.System<SharedSolutionContainerSystem>();
            var bloodstreams = entMan.System<BloodstreamSystem>();
            var gore = entMan.System<WolfmedGoreSystem>();

            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var stream = entMan.GetComponent<BloodstreamComponent>(mob);
            var own = proto.Index<ReagentPrototype>(stream.BloodReagent).SubstanceColor;
            var profile = HumanoidCharacterProfile.RandomWithSpecies("Human");

            Color BloodColour()
            {
                Assert.That(solutions.ResolveSolution(mob, stream.BloodSolutionName, ref stream.BloodSolution, out var blood));
                return blood!.GetColor(proto);
            }

            humanoid.LoadProfile(mob, profile.WithBloodColor(Green));
            Assert.Multiple(() =>
            {
                Assert.That(BloodColour(), Is.EqualTo(Green), "The bloodstream was not repainted.");
                Assert.That(gore.GetBloodColor(mob), Is.EqualTo(Green), "Wolfmed gore ignores the colour.");
                Assert.That(entMan.GetComponent<WFBloodColourComponent>(mob).Color, Is.EqualTo(Green));
            });

            // Fresh blood is made with the colour too, and a second load leaves one colour on the blood, not two.
            humanoid.LoadProfile(mob, profile.WithBloodColor(Blue));
            bloodstreams.TryModifyBloodLevel(mob, -20);
            bloodstreams.TryModifyBloodLevel(mob, 20);
            Assert.Multiple(() =>
            {
                Assert.That(BloodColour(), Is.EqualTo(Blue));
                Assert.That(stream.BloodSolution!.Value.Comp.Solution.Contents
                    .All(r => r.Reagent.EnsureReagentData().OfType<WFBloodColourData>().Count() == 1));
            });

            humanoid.LoadProfile(mob, profile.WithBloodColor(null));
            Assert.Multiple(() =>
            {
                Assert.That(BloodColour(), Is.EqualTo(own), "Clearing the colour should give the reagent's back.");
                Assert.That(entMan.HasComponent<WFBloodColourComponent>(mob), Is.False);
            });

            entMan.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SeveredPartTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.System<SharedHumanoidAppearanceSystem>()
                .LoadProfile(mob, HumanoidCharacterProfile.RandomWithSpecies("Human").WithBloodColor(Green));

            var body = entMan.System<SharedBodySystem>();
            var arm = body.GetBodyChildrenOfType(mob, BodyPartType.Arm).First();
            var hand = body.GetBodyPartChildren(arm.Id, arm.Component).Skip(1).Select(p => p.Id).First();
            Assert.That(entMan.System<WolfmedBodySystem>().TryDetachPart(arm.Id));

            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<WFBloodColourComponent>(arm.Id).Color, Is.EqualTo(Green),
                    "A severed arm should keep the body's blood colour.");
                Assert.That(entMan.GetComponent<WFBloodColourComponent>(hand).Color, Is.EqualTo(Green),
                    "The hand on a severed arm should keep it too.");
            });

            entMan.DeleteEntity(arm.Id);
            entMan.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    private static ServerDbSqlite GetDb(RobustIntegrationTest.ServerIntegrationInstance server)
    {
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var opsLog = server.ResolveDependency<ILogManager>().GetSawmill("db.ops");
        var builder = new DbContextOptionsBuilder<SqliteServerDbContext>();
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        builder.UseSqlite(conn);
        return new ServerDbSqlite(() => builder.Options, true, cfg, true, opsLog);
    }
}
