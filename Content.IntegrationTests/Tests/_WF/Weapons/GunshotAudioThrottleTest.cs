#nullable enable
using System.Collections.Generic;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Weapons;

/// <summary>
/// Guns above 5 shots/s send other players at most one gunshot sound per 0.2 s; slower guns are untouched.
/// </summary>
[TestFixture]
public sealed class GunshotAudioThrottleTest
{
    private const string FastGun = "WeaponHeavyMiningLaser";
    private const string SlowGun = "WeaponLaserGun";

    /// <summary>Fires a gun for two seconds and returns (shots fired, distinct audio entities seen).</summary>
    private static async Task<(int Shots, int Sounds)> Fire(string proto)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var guns = entMan.System<SharedGunSystem>();

        EntityUid gunUid = default;
        var startShots = 0;
        await server.WaitAssertion(() =>
        {
            gunUid = entMan.SpawnEntity(proto, map.GridCoords);
            var gun = entMan.GetComponent<GunComponent>(gunUid);
            gun.SelectedMode = SelectiveFire.FullAuto;
            startShots = entMan.GetComponent<HitscanBatteryAmmoProviderComponent>(gunUid).Shots;
        });

        var seen = new HashSet<EntityUid>();
        void Sample()
        {
            var query = entMan.EntityQueryEnumerator<AudioComponent>();
            while (query.MoveNext(out var uid, out _))
                seen.Add(uid);
        }

        await server.WaitAssertion(() =>
        {
            var gun = entMan.GetComponent<GunComponent>(gunUid);
            guns.AttemptShots(gunUid, gunUid, gun, new EntityCoordinates(map.Grid.Owner, 6, 0), TimeSpan.FromSeconds(2));
            seen.Clear();
        });

        var ticks = (int) Math.Ceiling(2.5 / server.ResolveDependency<IGameTiming>().TickPeriod.TotalSeconds);
        for (var i = 0; i < ticks; i++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(Sample);
        }

        var shots = 0;
        await server.WaitPost(() =>
            shots = startShots - entMan.GetComponent<HitscanBatteryAmmoProviderComponent>(gunUid).Shots);

        await pair.CleanReturnAsync();
        return (shots, seen.Count);
    }

    [Test]
    public async Task FastGunSoundIsThrottled()
    {
        var (shots, sounds) = await Fire(FastGun);
        TestContext.Out.WriteLine($"fast gun: {shots} shots, {sounds} sounds");
        Assert.That(shots, Is.InRange(15, 17), "the gun should fire about 16 shots in 2 s");
        Assert.That(sounds, Is.InRange(1, 10), "no more than 5 sounds per second");
        Assert.That(sounds, Is.GreaterThanOrEqualTo(shots / 3), "the throttle should still let sounds through");
    }

    [Test]
    public async Task SlowGunSoundIsNotThrottled()
    {
        var (shots, sounds) = await Fire(SlowGun);
        TestContext.Out.WriteLine($"slow gun: {shots} shots, {sounds} sounds");
        Assert.That(shots, Is.InRange(3, 5));
        Assert.That(sounds, Is.EqualTo(shots), "a gun at or under 5 shots/s plays every shot");
    }
}
