#nullable enable
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Station.Systems;
using Content.Server.Temperature.Components;
using Content.Shared._WF.Biotype;
using Content.Shared.Atmos;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Synthetics;

/// <summary>A chassis on an emptied tile cools as it would in open space; one in air does not.</summary>
[TestFixture]
public sealed class WFChassisVacuumTest : WolfmedGameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestCase(true)]
    [TestCase(false)]
    public async Task VacuumTileCoolsChassisTest(bool vacuum)
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var profile = HumanoidCharacterProfile.RandomWithSpecies("Human").WithBiotype(WFBiotype.Synthetic);
            var body = SEntMan.System<StationSpawningSystem>().SpawnPlayerMob(map.GridCoords, job: null, profile, station: null);
            var temperature = SEntMan.GetComponent<TemperatureComponent>(body);
            var before = temperature.CurrentTemperature;

            // A grid tile's own mixture: mutable, and empty once the hull is breached.
            var tile = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.TCMB };
            if (!vacuum)
            {
                tile.Temperature = before;
                tile.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
                tile.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            }

            var xform = SEntMan.GetComponent<TransformComponent>(body);
            for (var i = 0; i < 5; i++)
            {
                var ev = new AtmosExposedUpdateEvent(xform.Coordinates, tile, xform);
                SEntMan.EventBus.RaiseLocalEvent(body, ref ev);
            }

            var drop = before - temperature.CurrentTemperature;
            if (vacuum)
                Assert.That(drop, Is.GreaterThan(10f), "a chassis on an empty tile did not cool.");
            else
                Assert.That(drop, Is.LessThan(2f), "a chassis in air cooled as if in vacuum.");
        });
    }
}
