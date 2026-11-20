#nullable enable
using Content.Server._WG.AdminObserver;
using Content.Server._WG.Subtle;
using Content.Shared._WG.AdminObserver;
using Content.Shared.Eye;
using Robust.Shared;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.AdminObserver;

/// <summary>A living player sees an admin observer only while it is manifested, and always with its marker.</summary>
[TestFixture]
[TestOf(typeof(WGManifestSystem))]
public sealed class ManifestVisibilityTest
{
    [Test]
    public async Task LivingPlayerSeesOnlyAManifestedObserver()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var manifest = server.System<WGManifestSystem>();
        var subtle = server.System<SubtleSystem>();
        var map = await pair.CreateTestMap();

        // The pool runs without PVS, which sends every entity to every client whatever its layer.
        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.NetPVS, true));

        var human = await AdminKitFixture.Living(pair, map.GridCoords);
        var observer = await AdminKitFixture.Unmanned(pair, map.GridCoords);
        var netObserver = entMan.GetNetEntity(observer);

        bool EyeSees() => (entMan.GetComponent<EyeComponent>(human).VisibilityMask
                           & entMan.GetComponent<VisibilityComponent>(observer).Layer) != 0;

        bool ClientHas<T>() where T : IComponent
        {
            return client.EntMan.TryGetEntity(netObserver, out var uid)
                   && (client.EntMan.GetComponent<MetaDataComponent>(uid.Value).Flags & MetaDataFlags.Detached) == 0
                   && client.EntMan.HasComponent<T>(uid);
        }

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(EyeSees(), Is.False, "A hidden observer is on the ghost layer.");
                Assert.That(subtle.InSubtleRange(human, observer), Is.False, "A hidden observer is no subtle listener.");
                Assert.That(entMan.GetComponent<VisibilityComponent>(observer).Layer & (ushort) VisibilityFlags.Ghost, Is.Not.Zero);
            });
        });
        Assert.That(ClientHas<TransformComponent>(), Is.False, "A living player's client should not hold a hidden observer.");

        await server.WaitPost(() => manifest.Manifest(observer, new WGAvatarPreset { EntryStyle = WGEntryStyle.Silent }, null));
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(EyeSees(), "A manifested observer is on the layer everyone sees.");
                Assert.That(subtle.InSubtleRange(human, observer), "A manifested observer hears a subtle emote beside it.");
                Assert.That(entMan.GetComponent<VisibilityComponent>(observer).Layer & (ushort) VisibilityFlags.Ghost, Is.Zero);
                Assert.That(entMan.HasComponent<WGStaffMarkerComponent>(observer), "Visible means marked.");
            });
        });
        Assert.Multiple(() =>
        {
            Assert.That(ClientHas<WGManifestedComponent>(), "The living player's client should now hold the observer.");
            Assert.That(ClientHas<WGStaffMarkerComponent>(), "And its staff marker.");
        });

        await server.WaitPost(() => manifest.Unmanifest(observer));
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(EyeSees(), Is.False);
                Assert.That(entMan.HasComponent<WGManifestedComponent>(observer), Is.False);
                Assert.That(entMan.HasComponent<WGStaffMarkerComponent>(observer), Is.False);
            });
        });
        Assert.That(ClientHas<TransformComponent>(), Is.False, "Hiding should take the observer out of the player's view at once.");

        await pair.CleanReturnAsync();
    }
}
