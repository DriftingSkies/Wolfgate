#nullable enable
using Content.Server._WF.OutpostRaids;
using Content.Server._WF.Outposts;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Forecast;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared._WF.Planets.Forecast;
using Content.Shared._WF.ShipPa;
using Content.Shared.Power.EntitySystems;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Outposts.OutpostFoundingTest;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.OutpostRaids;

/// <summary>The Active window outposts can be raided in, and the warning an outpost gets before a raid.</summary>
[TestFixture]
[TestOf(typeof(WFRaidWindowSystem))]
public sealed class RaidWindowTest
{
    [Test]
    public async Task TheWindowOpensAtDuskAndWhileAStormRages()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        await EnableFeature(pair);
        await server.WaitPost(() => server.CfgMan.SetCVar(PlanetCVars.PlanetWeather, false));
        var layers = await BuildStandalone(pair);
        var map = await pair.CreateTestMap();
        var weather = server.System<WFPlanetWeatherSystem>();
        var window = server.System<WFRaidWindowSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid world = default;
        double daySeconds = 0;

        await server.WaitPost(() =>
        {
            world = entMan.GetComponent<CEZMapComponent>(layers[0]).NetworkUid;
            Assert.That(weather.TryGetMinute(world, out _, out daySeconds), Is.True);
        });
        var gameMinute = TimeSpan.FromSeconds(daySeconds / 1440);

        // Clear skies: the window follows the clock alone.
        foreach (var (minute, open) in new[] { (1079, false), (1080, true), (0, true), (359, true), (360, false), (720, false) })
        {
            await server.WaitAssertion(() =>
            {
                Assert.That(weather.SetMinuteOfDay(world, minute), Is.True);
                Assert.That(window.TryGetWindow(world, out var active, out var until), Is.True);
                Assert.That(active, Is.EqualTo(open), $"The window is wrong at minute {minute}.");
                Assert.That(until, Is.GreaterThan(TimeSpan.Zero), $"The window never changes at minute {minute}.");
                if (minute is 1079 or 359)
                    Assert.That(until, Is.LessThanOrEqualTo(gameMinute + TimeSpan.FromSeconds(0.1)), $"The change at minute {minute} is too far off.");
                Assert.That(window.TryGetWindow(world, out var later, out _, timing.CurTime + until + TimeSpan.FromSeconds(0.1)), Is.True);
                Assert.That(later, Is.EqualTo(!open), $"The window didn't change when it said it would at minute {minute}.");

                var console = entMan.SpawnEntity("WFComputerWeatherForecast", map.GridCoords);
                var comp = entMan.GetComponent<WFWeatherForecastConsoleComponent>(console);
                comp.Selected = entMan.GetNetEntity(world);
                var ui = server.System<WFWeatherForecastConsoleSystem>().BuildState((console, comp));
                Assert.That(ui.HasRaidWindow, Is.True);
                Assert.That(ui.RaidActive, Is.EqualTo(open));
                Assert.That(ui.RaidChangeInSeconds, Is.EqualTo((float) until.TotalSeconds).Within(0.01f));
                entMan.DeleteEntity(console);
            });
        }

        // A raging storm at noon opens it until the storm ends; one still building doesn't.
        await server.WaitAssertion(() =>
        {
            Assert.That(weather.SetMinuteOfDay(world, 720), Is.True);
            var state = entMan.GetComponent<WFPlanetWeatherComponent>(world);
            state.AdminHeld = true;
            state.Current = "Ashfall";
            state.Phase = WFStormPhase.Main;
            state.NextChange = timing.CurTime + TimeSpan.FromSeconds(60);
            state.Upcoming.Clear();
            Assert.That(window.TryGetWindow(world, out var active, out var until), Is.True);
            Assert.That(active, Is.True, "A raging storm didn't open the window.");
            Assert.That(until.TotalSeconds, Is.EqualTo(60).Within(0.1), "The window should close when the storm ends.");

            state.Phase = WFStormPhase.Telegraph;
            Assert.That(window.TryGetWindow(world, out active, out _), Is.True);
            Assert.That(active, Is.False, "A storm still building opened the window.");
            state.AdminHeld = false;
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheMastWarnsAheadOfARaid()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        // With PVS off, wildlife's owner-only alerts log as empty states over a long run.
        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.NetPVS, true));
        var (layers, user) = await Setup(pair);
        var ground = layers[0];
        var raids = server.System<WFRaidSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid outpost = default, mast = default;

        try
        {
            // The mast sounds the alarm its lead before the raid.
            await server.WaitAssertion(() =>
            {
                var kit = SpawnKit(entMan, ground);
                Assert.That(server.System<WFOutpostSystem>().TryFound(user, kit, new EntityCoordinates(ground, Site.X, Site.Y), out var founded, out var reason),
                    Is.True, $"Founding was refused: {reason}.");
                outpost = founded!.Value;
                mast = entMan.SpawnEntity("WFOutpostWarningMast", new EntityCoordinates(outpost, 0.5f, 0.5f));
                Assert.That(entMan.GetComponent<TransformComponent>(mast).Anchored, Is.True, "Precondition: the mast stands anchored.");
                server.System<SharedPowerReceiverSystem>().SetNeedsPower(mast, false);
                raids.ScheduleRaid(outpost, timing.CurTime + TimeSpan.FromSeconds(123));
            });

            await server.WaitRunTicks(pair.SecondsToTicks(1.5f));
            await server.WaitAssertion(() =>
            {
                Assert.That(raids.WarningLead(outpost), Is.EqualTo(TimeSpan.FromSeconds(120)));
                Assert.That(Code(entMan, outpost), Is.Not.EqualTo(WFRaidSystem.AlertCode), "The mast warned too early.");
            });
            await server.WaitRunTicks(pair.SecondsToTicks(3));
            await server.WaitAssertion(() =>
            {
                Assert.That(Code(entMan, outpost), Is.EqualTo(WFRaidSystem.AlertCode), "The mast didn't warn 120 s ahead.");
                Assert.That(raids.Raids, Has.Count.EqualTo(1));
                Assert.That(raids.Raids[0].Warned, Is.True);

                // Without a mast, the outpost notices it only moments before.
                entMan.DeleteEntity(mast);
                raids.CancelRaid(raids.Raids[0].Id);
                Assert.That(raids.WarningLead(outpost), Is.Null);
                raids.ScheduleRaid(outpost, timing.CurTime + TimeSpan.FromSeconds(17));
            });

            await server.WaitRunTicks(pair.SecondsToTicks(1));
            await server.WaitAssertion(() => Assert.That(raids.Raids[0].LateWarned, Is.False, "The late warning came too early."));
            await server.WaitRunTicks(pair.SecondsToTicks(2));
            await server.WaitAssertion(() =>
            {
                Assert.That(raids.Raids[0].LateWarned, Is.True, "No late warning 15 s out.");
                raids.CancelRaid(raids.Raids[0].Id);
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    private static string? Code(IEntityManager entMan, EntityUid grid)
    {
        return entMan.TryGetComponent<ShipAlertComponent>(grid, out var alert) ? alert.Code.Id : null;
    }
}
