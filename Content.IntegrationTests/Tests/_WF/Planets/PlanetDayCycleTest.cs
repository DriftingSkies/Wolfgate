#nullable enable
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Forecast;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets;
using Content.Shared._WF.Planets.Forecast;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>The day cycle the forecast console counts down: time to dusk or dawn matches when night actually flips.</summary>
[TestFixture]
public sealed class PlanetDayCycleTest
{
    [Test]
    public async Task ForecastCountsDownToTheNextDuskOrDawn()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var em = server.EntMan;
        var weather = server.System<WFPlanetWeatherSystem>();
        EntityUid network = default;
        await server.WaitPost(() => network = em.GetComponent<CEZMapComponent>(layers[0]).NetworkUid);

        foreach (var minute in new[] { 0, 300, 350, 370, 720, 1070, 1090, 1430 })
        {
            await server.WaitPost(() => Assert.That(weather.SetMinuteOfDay(network, minute), Is.True));
            await server.WaitRunTicks(pair.SecondsToTicks(1.5f));
            await server.WaitAssertion(() =>
            {
                var state = em.GetComponent<WFPlanetWeatherComponent>(network);
                var profile = server.ResolveDependency<IPrototypeManager>().Index(state.Profile);
                var now = server.ResolveDependency<IGameTiming>().CurTime;
                var second = TimeSpan.FromSeconds(1);
                bool NightAt(TimeSpan at)
                {
                    var m = weather.GetMinuteOfDay(state, profile, at);
                    return m < WFPlanetWeatherSystem.DawnMinute || m >= WFPlanetWeatherSystem.DuskMinute;
                }

                Assert.That(weather.TryGetDayCycle(network, out var night, out var until), Is.True);
                Assert.That(night, Is.EqualTo(NightAt(now)), $"Night is wrong at minute {minute}.");
                Assert.That(em.GetComponent<WFPlanetEnvironmentComponent>(layers[0]).IsNight, Is.EqualTo(night),
                    $"The ground's IsNight disagrees at minute {minute}.");
                Assert.That(until, Is.GreaterThan(TimeSpan.Zero));
                Assert.That(until.TotalSeconds, Is.LessThanOrEqualTo(profile.DaySeconds / 2.0 + 1));
                Assert.That(NightAt(now + until - second), Is.EqualTo(night), $"Night flipped early at minute {minute}.");
                Assert.That(NightAt(now + until + second), Is.EqualTo(!night), $"Night did not flip at minute {minute}.");

                var console = em.SpawnEntity("WFComputerWeatherForecast", map.GridCoords);
                var comp = em.GetComponent<WFWeatherForecastConsoleComponent>(console);
                comp.Selected = em.GetNetEntity(network);
                var ui = server.System<WFWeatherForecastConsoleSystem>().BuildState((console, comp));
                Assert.That(ui.HasClock, Is.True);
                Assert.That(ui.Night, Is.EqualTo(night));
                Assert.That(ui.DayChangeInSeconds, Is.EqualTo((float) until.TotalSeconds).Within(0.01f));
                em.DeleteEntity(console);
            });
        }

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
