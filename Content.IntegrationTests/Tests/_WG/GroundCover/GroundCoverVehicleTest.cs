#nullable enable
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WG.GroundCover;
using Content.Shared._WG.GroundCover;
using Content.Shared._WG.Vehicles.Core;
using Content.Shared._WG.WolfWeather;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using ClientGroundCoverSystem = Content.Client._WG.GroundCover.WGGroundCoverSystem;

namespace Content.IntegrationTests.Tests._WG.GroundCover;

/// <summary>Someone inside a vehicle looks out on the world's weather: the snow around the hull reaches them.</summary>
[TestFixture]
[TestOf(typeof(WGGroundCoverSystem))]
public sealed class GroundCoverVehicleTest
{
    private const string Chassis = "WGInteriorTestVehicle";
    private static readonly Vector2 VehicleSpot = new(5.5f, 5.5f);
    private static readonly Vector2i Beside = new(1, 1);
    private static readonly Vector2i Behind = new(-3, -3);

    [Test]
    public async Task SnowReachesSomeoneInsideAVehicle()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid mob = default, vehicle = default;

        await server.WaitPost(() =>
        {
            GroundCoverTest.LayOpenGround(pair, map.Grid);
            entMan.EnsureComponent<WGGroundWeatherComponent>(map.MapUid);
            vehicle = entMan.SpawnEntity(Chassis, new EntityCoordinates(map.Grid, VehicleSpot));
            mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, VehicleSpot + new Vector2(1f, 0f)));
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);
            server.System<WGGroundCoverSystem>().SetDepth(map.Grid, Beside, 150);
        });

        await pair.RunTicksSync(pair.SecondsToTicks(2));
        await pair.Client.WaitAssertion(() => Assert.That(ClientDepth(pair, map, Beside), Is.EqualTo(150),
            "Snow beside a player outside never reached them."));

        // In through the doorway.
        await server.WaitPost(() => server.System<SharedInteractionSystem>().InteractionActivate(mob, vehicle));
        await pair.RunTicksSync(30);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.Not.EqualTo(map.MapUid),
                "The player never reached the interior.");
            Assert.That(server.System<SharedWolfWeatherSystem>().GetSkyMap(mob), Is.EqualTo(map.MapUid),
                "Inside a vehicle the sky should be the world the hull is on.");
        });

        // Snow that falls now, on ground the interior's own map has none of, still reaches them.
        await server.WaitPost(() => server.System<WGGroundCoverSystem>().SetDepth(map.Grid, Behind, 120));
        await pair.RunTicksSync(pair.SecondsToTicks(3));
        await pair.Client.WaitAssertion(() => Assert.That(ClientDepth(pair, map, Behind), Is.EqualTo(120),
            "Someone inside a vehicle wasn't sent the snow around its hull."));

        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, null));
        await pair.CleanReturnAsync();
    }

    private static int ClientDepth(TestPair pair, TestMapData map, Vector2i tile)
    {
        var grid = pair.Client.EntMan.GetEntity(pair.Server.EntMan.GetNetEntity(map.Grid.Owner));
        return pair.Client.System<ClientGroundCoverSystem>().GetDepth(grid, tile);
    }
}
