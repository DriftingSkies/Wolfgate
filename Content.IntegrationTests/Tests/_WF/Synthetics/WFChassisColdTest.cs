#nullable enable
using Content.Server._WF.Synthetics;
using Content.Server.Station.Systems;
using Content.Server.Temperature.Components;
using Content.Shared._WF.Biotype;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Synthetics;

/// <summary>An unsuited synthetic in vacuum freezes onto the floor, never out cold, and stands once it warms up.</summary>
[TestFixture]
public sealed class WFChassisColdTest
{
    [Test]
    public async Task FrozenChassisIsDownedNotOut()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid body = default;

        await server.WaitPost(() =>
        {
            var profile = HumanoidCharacterProfile.RandomWithSpecies("Human").WithBiotype(WFBiotype.Synthetic);
            body = entMan.System<StationSpawningSystem>().SpawnPlayerMob(map.GridCoords, job: null, profile, station: null);
        });

        // The test map is open space: a chassis cools to about 50 K in a minute.
        var downedAt = -1;
        var wentOut = false;
        for (var t = 0; t < 180; t += 5)
        {
            await pair.RunSeconds(5f);
            WolfmedConsciousness state = default;
            await server.WaitPost(() => state = entMan.GetComponent<WolfmedConsciousnessComponent>(body).State);
            if (state == WolfmedConsciousness.Downed && downedAt < 0)
                downedAt = t + 5;
            wentOut |= state == WolfmedConsciousness.Unconscious;
        }

        Assert.Multiple(() =>
        {
            Assert.That(downedAt, Is.InRange(1, 120), "a frozen chassis stayed on its feet.");
            Assert.That(wentOut, Is.False, "the cold knocked a chassis out.");
        });

        // Held warm against the vacuum, it stands.
        var up = false;
        for (var i = 0; i < 10 && !up; i++)
        {
            await server.WaitPost(() =>
            {
                entMan.GetComponent<TemperatureComponent>(body).CurrentTemperature = 310f;
                up = entMan.GetComponent<WolfmedConsciousnessComponent>(body).State == WolfmedConsciousness.Up;
            });
            await pair.RunSeconds(1f);
        }

        Assert.That(up, Is.True, "a warmed chassis didn't stand back up.");
        await pair.CleanReturnAsync();
    }

    [TestCase(300f, 0f)]
    [TestCase(180f, 0f)]
    [TestCase(60f, WFChassisColdSystem.MaxPressure)]
    [TestCase(20f, WFChassisColdSystem.MaxPressure)]
    public void PressureCurve(float temperature, float expected)
    {
        Assert.That(WFChassisColdSystem.Pressure(temperature), Is.EqualTo(expected).Within(0.001f));
    }
}
