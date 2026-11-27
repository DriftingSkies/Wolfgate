using Content.Server._WG.Farming;
using Content.Server.Botany.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Interaction;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.Farming;

/// <summary>A plant under a roof on a world grows by day like one in the open, and rests at night without a lamp; a landed ship keeps its own lights.</summary>
[TestFixture]
public sealed class GreenhouseRoofTest
{
    [Test]
    public async Task RoofedPlantGrowsByDay()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var map = await pair.CreateTestMap();
        EntityUid tray = default;
        Entity<LightCycleComponent> cycle = default;

        // The offset that puts the world at its brightest, or its darkest, right now.
        TimeSpan OffsetFor(bool day)
        {
            var seconds = (float) cycle.Comp.Duration.TotalSeconds;
            var best = 0f;
            var bestLight = day ? double.MinValue : double.MaxValue;
            for (var t = 0f; t < seconds; t += seconds / 48f)
            {
                var light = SharedLightCycleSystem.CalculateLightLevel(cycle.Comp, t);
                if (day ? light > bestLight : light < bestLight)
                    (best, bestLight) = (t, light);
            }

            return TimeSpan.FromSeconds(best) - timing.CurTime;
        }

        await server.WaitAssertion(() =>
        {
            var comp = entMan.EnsureComponent<LightCycleComponent>(map.MapUid);
            comp.Enabled = true;
            comp.Duration = TimeSpan.FromMinutes(20);
            cycle = (map.MapUid, comp);
            entMan.System<SharedLightCycleSystem>().SetOffset(cycle, OffsetFor(day: true));

            var roof = entMan.EnsureComponent<RoofComponent>(map.Grid.Owner);
            var maps = entMan.System<SharedMapSystem>();
            tray = entMan.SpawnEntity("hydroponicsTray", map.GridCoords);
            var tile = maps.TileIndicesFor(map.Grid.Owner, map.Grid.Comp, map.GridCoords);
            entMan.System<SharedRoofSystem>().SetRoof((map.Grid.Owner, map.Grid.Comp, roof), tile, true);

            var farmer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var plant = new InteractUsingEvent(farmer, entMan.SpawnEntity("WheatSeeds", map.GridCoords), tray, map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(tray, plant);
            var holder = entMan.GetComponent<PlantHolderComponent>(tray);
            Assert.That(holder.Seed, Is.Not.Null, "The wheat didn't plant.");
            holder.WaterLevel = 100f;
            holder.NutritionLevel = 100f;
        });

        await pair.RunTicksSync(pair.SecondsToTicks(6));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<PlantHolderComponent>(tray).Dead, Is.False);
            Assert.That(entMan.GetComponent<WGFarmPlotComponent>(tray).Reason, Is.Null, "A roofed plant stalled in daylight.");
            entMan.System<SharedLightCycleSystem>().SetOffset(cycle, OffsetFor(day: false));
        });

        await pair.RunTicksSync(pair.SecondsToTicks(6));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WGFarmPlotComponent>(tray).Reason, Is.EqualTo("wg-farm-reason-night"),
                "A roofed plant with no lamp kept growing through the night.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LandedShipGrowsAtNight()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var map = await pair.CreateTestMap();
        EntityUid tray = default;

        await server.WaitAssertion(() =>
        {
            var comp = entMan.EnsureComponent<LightCycleComponent>(map.MapUid);
            comp.Enabled = true;
            comp.Duration = TimeSpan.FromMinutes(20);
            Entity<LightCycleComponent> cycle = (map.MapUid, comp);
            var seconds = (float) comp.Duration.TotalSeconds;
            var darkest = 0f;
            for (var t = 0f; t < seconds; t += seconds / 48f)
            {
                if (SharedLightCycleSystem.CalculateLightLevel(comp, t) < SharedLightCycleSystem.CalculateLightLevel(comp, darkest))
                    darkest = t;
            }
            entMan.System<SharedLightCycleSystem>().SetOffset(cycle, TimeSpan.FromSeconds(darkest) - timing.CurTime);

            entMan.EnsureComponent<ShuttleDeedComponent>(map.Grid.Owner);
            tray = entMan.SpawnEntity("hydroponicsTray", map.GridCoords);
            var farmer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var plant = new InteractUsingEvent(farmer, entMan.SpawnEntity("WheatSeeds", map.GridCoords), tray, map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(tray, plant);
            var holder = entMan.GetComponent<PlantHolderComponent>(tray);
            holder.WaterLevel = 100f;
            holder.NutritionLevel = 100f;
        });

        await pair.RunTicksSync(pair.SecondsToTicks(6));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WGFarmPlotComponent>(tray).Reason, Is.Null,
                "A plant aboard a ship landed on a world stalled for the world's night or weather.");
        });

        await pair.CleanReturnAsync();
    }
}
