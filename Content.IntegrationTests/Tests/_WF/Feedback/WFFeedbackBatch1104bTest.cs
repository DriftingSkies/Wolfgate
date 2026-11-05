#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.CustomMarkings;
using Content.Server.Power.Components;
using Content.Server._WF.Biotype;
using Content.Shared._Mono.Claws.Components;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._WF.Biotype;
using Content.Shared.Construction.Conditions;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Interaction;
using Content.Shared._WF.CustomMarkings;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Content.Server.Station.Systems;
using Content.Shared.Station.Components;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.IntegrationTests.Tests._WF.Feedback;

/// <summary>Player reports from the late 2026-11-04 feedback export.</summary>
[TestFixture]
public sealed class WFFeedbackBatch1104bTest
{
    /// <summary>A job with no role loadout, which can't be validated without a session.</summary>
    private const string Job = "Diplomat";

    /// <summary>
    /// Report 477: with no spawner of their own, a player falls back onto the station's other spawn points, but never
    /// onto another job's (the godfather's) while one is left.
    /// </summary>
    [Test]
    public async Task FallbackSkipsOtherJobsSpawners()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var station = em.SpawnEntity(null, MapCoordinates.Nullspace);
            em.EnsureComponent<StationMemberComponent>(map.Grid).Station = station;
            // A spawner off the grid belongs to no station, so the free one gets a tile of its own.
            server.System<SharedMapSystem>().SetTile(map.Grid, new Vector2i(1, 0), map.Tile.Tile);
            var godfather = new Vector2(0.5f, 0.5f);
            var other = new Vector2(1.5f, 0.5f);
            em.SpawnEntity("SpawnPointGodfather", new EntityCoordinates(map.Grid, godfather));
            var observer = em.SpawnEntity("SpawnPointObserver", new EntityCoordinates(map.Grid, other));

            for (var i = 0; i < 12; i++)
            {
                var ev = new PlayerSpawningEvent(Job, null, station);
                em.EventBus.RaiseEvent(EventSource.Local, ev);
                Assert.That(ev.SpawnResult, Is.Not.Null, "The player wasn't spawned anywhere.");
                var at = em.GetComponent<TransformComponent>(ev.SpawnResult!.Value).LocalPosition;
                Assert.That(Vector2.Distance(at, other), Is.LessThan(0.1f), $"The player spawned at {at}, not the free spawn point.");
                em.DeleteEntity(ev.SpawnResult);
            }

            // With only another job's spawner left, they still spawn rather than nowhere.
            em.DeleteEntity(observer);
            var last = new PlayerSpawningEvent(Job, null, station);
            em.EventBus.RaiseEvent(EventSource.Local, last);
            Assert.That(last.SpawnResult, Is.Not.Null, "With only the godfather's spawner, the player spawned nowhere.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Report 467: trash set on a running reclaimer is reclaimed into steel, and the client plays no crushing loop of
    /// its own that never stops.
    /// </summary>
    [Test]
    public async Task ReclaimerEatsTrashWithoutStuckSound()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid trash = default;

        await server.WaitAssertion(() =>
        {
            var viewer = em.SpawnEntity("MobHuman", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, viewer);
            var reclaimer = em.SpawnEntity("MaterialReclaimer", map.GridCoords);
            em.GetComponent<ApcPowerReceiverComponent>(reclaimer).NeedsPower = false;
        });
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() => trash = em.SpawnEntity("FoodPacketCheesieTrash", map.GridCoords));
        await pair.RunTicksSync(150);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(trash), Is.True, "The reclaimer never took the trash.");
            var steel = em.AllEntities<MetaDataComponent>().Count(e => e.Comp.EntityPrototype?.ID.StartsWith("SheetSteel") == true);
            Assert.That(steel, Is.GreaterThan(0), "The trash gave no steel.");
        });

        await pair.Client.WaitAssertion(() =>
        {
            var cem = pair.Client.EntMan;
            var loops = 0;
            foreach (var audio in cem.AllEntities<AudioComponent>())
            {
                string file = audio.Comp.FileName;
                if (cem.IsClientSide(audio.Owner) && file.Contains("crushing"))
                    loops++;
            }
            Assert.That(loops, Is.Zero, "The client plays its own crushing sound.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Report 411: custom markings draw over the arms the Prybar Prosthetics trait fits at spawn, as they do over
    /// augments. With the reach limit off, nothing is cut from a whole body.
    /// </summary>
    private const string ArmTattoo = "TattooCampbellLeftArm";

    [Test]
    public async Task CustomMarkingsDrawOverPrybarArms()
    {
        var hash = new string('d', CustomMarkingRules.HashLength);
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        EntityUid body = default;

        await server.WaitPost(() => server.CfgMan.SetCVar(CustomMarkingCVars.BodyReach, false));
        await client.WaitPost(() =>
        {
            var art = new CustomMarkingArt();
            for (var facing = 0; facing < CustomMarkingRules.Facings; facing++)
            {
                for (var y = 0; y < CustomMarkingRules.FrameSize; y++)
                {
                    for (var x = 0; x < CustomMarkingRules.FrameSize; x++)
                    {
                        art.SetPixel(0, facing, x, y, new Rgba32(0, 0, 0, 255));
                    }
                }
            }

            CustomMarkingResources.For(client.ResolveDependency<IResourceCache>()).Store(hash, art.ToPng());
        });

        await server.WaitPost(() =>
        {
            var em = server.EntMan;
            body = em.SpawnEntity("MobHuman", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, body);
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human")
                .WithCustomMarkings(new List<CustomMarking> { new(hash, CustomMarkingPlacement.Skin) });
            profile = profile.WithCharacterAppearance(profile.Appearance.WithMarkings(new List<Marking> { new(ArmTattoo, new List<Color> { Color.Black }) }));
            server.System<Content.Server.Humanoid.HumanoidAppearanceSystem>().LoadProfile(body, profile);
            em.AddComponent<PrybarProstheticsComponent>(body);
            // The Markings-tab marking on the swapped arm stays too.
            Assert.That(em.GetComponent<HumanoidAppearanceComponent>(body).MarkingSet.Markings.Values
                .Any(list => list.Any(m => m.MarkingId == ArmTattoo)), Is.True, "the arm's marking was dropped");
        });
        await pair.RunTicksSync(30);

        await client.WaitAssertion(() =>
        {
            var cem = client.EntMan;
            var uid = cem.GetEntity(server.EntMan.GetNetEntity(body));
            var sprite = cem.GetComponent<SpriteComponent>(uid);
            var sprites = cem.System<SpriteSystem>();
            foreach (var limb in new[] { HumanoidVisualLayers.LArm, HumanoidVisualLayers.RArm, HumanoidVisualLayers.LHand, HumanoidVisualLayers.RHand })
            {
                Assert.That(sprites.LayerMapTryGet((uid, sprite), limb, out var limbIndex, false), Is.True);
                Assert.That(sprite[limbIndex].Visible, Is.True, $"{limb} isn't drawn");
            }

            Assert.That(cem.System<CustomMarkingSystem>().TryGetArt(hash, out var whole), Is.True);
            Assert.That(sprites.LayerMapTryGet((uid, sprite), CustomMarkingSystem.LayerKey(0), out var index, false), Is.True, "the marking isn't drawn");
            Assert.That(((SpriteComponent.Layer) sprite[index]).ActualRsi, Is.SameAs(whole), "part of the marking was cut off");
            Assert.That(sprite[index].Visible, Is.True, "the marking is hidden");
            foreach (var arm in new[] { HumanoidVisualLayers.LArm, HumanoidVisualLayers.RArm })
            {
                Assert.That(sprites.LayerMapTryGet((uid, sprite), arm, out var armIndex, false), Is.True);
                Assert.That(index, Is.GreaterThan(armIndex), $"the marking draws under the prosthetic {arm}");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Report 336: a shelf (a wallmount) can go on a wooden wall as on a steel one, whichever wooden wall it is.
    /// </summary>
    [TestCase("WallSolid")]
    [TestCase("WallWood")]
    [TestCase("N14WallWoodWhite")]
    [TestCase("N14WallWoodLog")]
    [TestCase("N14WallWoodSlanted")]
    public async Task WallmountsGoOnWoodenWalls(string wall)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid user = default;

        await server.WaitPost(() =>
        {
            server.System<SharedMapSystem>().SetTile(map.Grid, new Vector2i(0, 1), map.Tile.Tile);
            em.SpawnEntity(wall, new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            user = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 0.5f, 1.5f));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var condition = new WallmountCondition();
            Assert.That(condition.Condition(user, new EntityCoordinates(map.Grid, 0.5f, 0.5f), Direction.North), Is.True,
                $"a shelf can't go on {wall}");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Report 289: wirecutters trim a reptilian's claws, a synthetic one's too, on themselves.
    /// </summary>
    [TestCase(WFBiotype.Organic)]
    [TestCase(WFBiotype.Synthetic)]
    public async Task ReptiliansTrimTheirOwnClaws(WFBiotype biotype)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid lizard = default;

        await server.WaitPost(() =>
        {
            lizard = em.SpawnEntity("MobReptilian", map.GridCoords);
            var species = server.ProtoMan.Index<SpeciesPrototype>("Reptilian");
            server.System<WFBiotypeSystem>().Apply(lizard, biotype, species);
            var claws = em.GetComponent<ClawsComponent>(lizard);
            claws.ClawStage = claws.Claws[2];
            em.Dirty(lizard, claws);
            var cutters = em.SpawnEntity("Wirecutter", map.GridCoords);
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(lizard, cutters), Is.True);
            server.System<SharedInteractionSystem>().UserInteraction(lizard, map.GridCoords, lizard);
        });
        await pair.RunSeconds(6);

        await server.WaitAssertion(() =>
        {
            var claws = em.GetComponent<ClawsComponent>(lizard);
            Assert.That(claws.ClawStage, Is.EqualTo(claws.Claws[1]), "the claws weren't trimmed");
        });

        await pair.CleanReturnAsync();
    }
}
