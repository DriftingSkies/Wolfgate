using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client.Salvage.UI;
using Content.Server._Mono.Cleanup;
using Content.Shared.Salvage;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Station.Components;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;

namespace Content.IntegrationTests.Tests._WF.EngineCompat;

/// <summary>Covers content that broke on engine v291.</summary>
[TestFixture]
public sealed class EngineV291RegressionTest
{
    /// <summary>Space cleanup's reflected TestOverlap call fills every parameter and detects an overlap.</summary>
    [Test]
    public async Task SpaceCleanupOverlapCheckRuns()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var cleanup = server.EntMan.System<SpaceCleanupSystem>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var method = (MethodInfo) typeof(SpaceCleanupSystem).GetField("_testOverlap", flags)!.GetValue(cleanup)!;
            var manifold = typeof(SpaceCleanupSystem).GetField("_manifold", flags)!.GetValue(cleanup);
            var args = (object[]) typeof(SpaceCleanupSystem).GetField("_testOverlapArgs", flags)!.GetValue(cleanup)!;
            Assert.That(args, Has.Length.EqualTo(method.GetParameters().Length));

            args[0] = new PolygonShape();
            ((PolygonShape) args[0]!).SetAsBox(0.5f, 0.5f);
            args[1] = 0;
            args[2] = new PhysShapeCircle(0.001f);
            args[3] = 0;
            args[4] = new Transform(Vector2.Zero, 0f);
            args[5] = new Transform(new Vector2(0.1f, 0.1f), 0f);
            Assert.That(method.Invoke(manifold, args), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A station's expedition data gets offers, and the client console window lists them.</summary>
    [Test]
    public async Task ExpeditionOffersReachTheConsole()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        SalvageExpeditionDataComponent data = default!;

        await server.WaitPost(() =>
        {
            var station = server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            server.EntMan.EnsureComponent<StationDataComponent>(station);
            data = server.EntMan.EnsureComponent<SalvageExpeditionDataComponent>(station);
        });
        await server.WaitRunTicks(5);

        List<SalvageMissionParams> missions = default!;
        await server.WaitAssertion(() =>
        {
            missions = data.Missions.Values.ToList();
            Assert.That(missions, Is.Not.Empty);
        });

        await client.WaitAssertion(() =>
        {
            var salvage = client.EntMan.System<SharedSalvageSystem>();
            foreach (var mission in missions)
            {
                Assert.DoesNotThrow(() => salvage.GetMissionDescription(salvage.GetMission(mission.MissionType, mission.Difficulty, mission.Seed)));
            }

            var window = new SalvageExpeditionWindow();
            window.UpdateState(new SalvageExpeditionConsoleState(TimeSpan.Zero, false, false, false, 0, missions));
            Assert.That(window.FindControl<BoxContainer>("Container").ChildCount, Is.EqualTo(missions.Count));
            window.Dispose();
        });

        await pair.CleanReturnAsync();
    }
}
