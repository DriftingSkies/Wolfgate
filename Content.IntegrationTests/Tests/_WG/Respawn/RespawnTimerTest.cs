using Content.Server._Corvax.Respawn;
using Content.Server.GameTicking;
using Content.Shared._NF.CCVar;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.Respawn;

/// <summary>A death in a started round sets the configured respawn time, scaled by the preset's multiplier.</summary>
[TestFixture]
[TestOf(typeof(RespawnSystem))]
public sealed class RespawnTimerTest
{
    [Test]
    public async Task DeathSetsConfiguredRespawnTime()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            Dirty = true,
            DummyTicker = false,
        });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var respawn = server.System<RespawnSystem>();
        var mobState = server.System<MobStateSystem>();
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var timing = server.ResolveDependency<IGameTiming>();

        var expected = 0f;
        TimeSpan? delay = null;
        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
            expected = cfg.GetCVar(NFCCVars.RespawnTime) * (ticker.CurrentPreset?.RespawnMultiplier ?? 1f);

            mobState.ChangeMobState(pair.Player!.AttachedEntity!.Value, MobState.Dead);
            delay = respawn.GetRespawnTime(pair.Player.UserId) - timing.CurTime;
        });

        Assert.That(expected, Is.GreaterThan(0f));
        Assert.That(delay?.TotalSeconds, Is.EqualTo(expected).Within(1));

        await pair.CleanReturnAsync();
    }
}
