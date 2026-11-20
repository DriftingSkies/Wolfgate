#nullable enable
using System.Numerics;
using Content.Client._WG.AdminObserver;
using Content.Server._WG.AdminObserver;
using Content.Server.Medical.SuitSensors;
using Content.Server.Preferences.Managers;
using Content.Shared._WG.AdminObserver;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Medical.SuitSensor;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Shared;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.AdminObserver;

/// <summary>The humanoid behind a character avatar: inert, carried by its observer, and unseen while that is hidden.</summary>
[TestFixture]
[TestOf(typeof(WGAvatarBodySystem))]
public sealed class AvatarBodyTest
{
    [Test]
    public async Task BodyIsInertAndGoesWithItsObserver()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var manifest = server.System<WGManifestSystem>();
        var bodies = server.System<WGAvatarBodySystem>();
        var store = server.System<WGAdminObserverStore>();
        var containers = server.System<SharedContainerSystem>();
        var inventory = server.System<InventorySystem>();
        var mobs = server.System<MobStateSystem>();
        var map = await pair.CreateTestMap();
        var session = await AdminKitFixture.Admin(pair);
        var observer = await AdminKitFixture.Enter(pair, session, map.GridCoords);

        // A new admin starts with the plain ghost and their own character.
        EntityUid body = default;
        var character = string.Empty;
        await server.WaitAssertion(() =>
        {
            var stock = store.Get(session)!.Presets;
            Assert.That(stock, Has.Count.EqualTo(2));
            Assert.That(stock[1].Kind, Is.EqualTo(WGAvatarKind.Character));

            var profile = (HumanoidCharacterProfile) server.ResolveDependency<IServerPreferencesManager>()
                .GetPreferences(session.UserId).SelectedCharacter;
            character = profile.Name;

            Assert.That(manifest.TryManifest(session, stock[1].Name, out var message), message);
            var manifested = entMan.GetComponent<WGManifestedComponent>(observer);
            Assert.That(manifested.Body, Is.Not.Null);
            body = manifested.Body!.Value;

            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<MetaDataComponent>(observer).EntityName, Is.EqualTo(character));
                Assert.That(entMan.GetComponent<WGAvatarBodyComponent>(body).Observer, Is.EqualTo(observer));
                Assert.That(entMan.HasComponent<HumanoidAppearanceComponent>(body));
                Assert.That(entMan.HasComponent<GodmodeComponent>(body));
                Assert.That(entMan.GetComponent<MindContainerComponent>(body).HasMind, Is.False);
                Assert.That(containers.TryGetContainingContainer((body, null, null), out var held) && held.Owner == observer && held.ID == WGAvatarBodySystem.Slot,
                    "The body rides in the observer's container.");
                Assert.That(inventory.TryGetSlotEntity(body, "jumpsuit", out _), "With no outfit chosen it wears its job's loadout.");
            });

            // Nothing it wears reports it to crew monitors, now or when dressed later.
            var worn = inventory.GetSlotEnumerator(body);
            while (worn.NextItem(out var item))
            {
                if (entMan.TryGetComponent(item, out SuitSensorComponent? sensor))
                    Assert.That(sensor.Mode, Is.EqualTo(SuitSensorMode.SensorOff));
            }

            // Manifesting again as the same avatar keeps the body; a change to the preset replaces it.
            Assert.That(manifest.TryManifest(session, stock[1].Name, out _));
            Assert.That(entMan.GetComponent<WGManifestedComponent>(observer).Body, Is.EqualTo(body));
        });

        // Five minutes drifting in space leave it as it was.
        var transform = server.System<SharedTransformSystem>();
        await server.WaitPost(() => transform.SetCoordinates(observer, new EntityCoordinates(map.MapUid, new Vector2(200, 200))));
        await pair.RunSeconds(300);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.EntityExists(body));
                Assert.That(mobs.IsAlive(body), "Vacuum does nothing to a body that is not simulated.");
                Assert.That(entMan.GetComponent<MetaDataComponent>(body).EntityPaused);
                Assert.That(entMan.GetComponent<DamageableComponent>(body).TotalDamage.Float(), Is.Zero);
                Assert.That(entMan.GetComponent<TransformComponent>(body).ParentUid, Is.EqualTo(observer));
            });
        });

        // Another map wakes what is carried onto it; the body is put back to sleep at once.
        var elsewhere = await pair.CreateTestMap();
        await server.WaitPost(() => transform.SetCoordinates(observer, elsewhere.GridCoords));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<MetaDataComponent>(body).EntityPaused);

            // Round end takes the body, and with it the avatar.
            bodies.RemoveBodies();
            Assert.Multiple(() =>
            {
                Assert.That(entMan.Deleted(body));
                Assert.That(entMan.HasComponent<WGManifestedComponent>(observer), Is.False);
            });

            // A new one goes when its observer does.
            Assert.That(manifest.TryManifest(session, store.Get(session)!.Presets[1].Name, out var message), message);
            body = entMan.GetComponent<WGManifestedComponent>(observer).Body!.Value;
            entMan.DeleteEntity(observer);
            Assert.That(entMan.Deleted(body));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LivingPlayerReceivesTheBodyOnlyWhileManifested()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var manifest = server.System<WGManifestSystem>();
        var bodies = server.System<WGAvatarBodySystem>();
        var map = await pair.CreateTestMap();
        var session = await AdminKitFixture.Admin(pair);

        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.NetPVS, true));
        await AdminKitFixture.Living(pair, map.GridCoords);
        var observer = await AdminKitFixture.Unmanned(pair, map.GridCoords);
        var preset = new WGAvatarPreset { Name = "me", Kind = WGAvatarKind.Character, EntryStyle = WGEntryStyle.Silent };

        EntityUid body = default;
        await server.WaitAssertion(() =>
        {
            var built = bodies.Ensure((observer, entMan.GetComponent<WGAdminObserverComponent>(observer)), session, preset);
            Assert.That(built, Is.Not.Null);
            body = built!.Value;
        });
        await pair.RunTicksSync(10);

        var netObserver = entMan.GetNetEntity(observer);
        var netBody = entMan.GetNetEntity(body);

        bool ClientHolds(NetEntity net)
        {
            return client.EntMan.TryGetEntity(net, out var uid)
                   && (client.EntMan.GetComponent<MetaDataComponent>(uid.Value).Flags & MetaDataFlags.Detached) == 0;
        }

        Assert.That(ClientHolds(netBody), Is.False, "A hidden observer's body stays off a living player's client.");

        await server.WaitPost(() => manifest.Manifest(observer, preset, "Somebody", body));
        await pair.RunTicksSync(15);

        await client.WaitAssertion(() =>
        {
            Assert.That(ClientHolds(netBody), "Manifested, the body is sent so it can be copied.");
            var uid = client.EntMan.GetEntity(netObserver);
            var sprite = client.EntMan.GetComponent<SpriteComponent>(uid);
            Assert.Multiple(() =>
            {
                Assert.That(client.EntMan.GetComponent<WGAvatarVisualsComponent>(uid).Replaced, "The observer is drawn as its body.");
                Assert.That(sprite.LayerMapTryGet(HumanoidVisualLayers.Chest, out _), "It has a humanoid's layers.");
                Assert.That(sprite.LayerMapTryGet("ghostVariant", out _), Is.False);
            });
        });

        await server.WaitPost(() => manifest.Unmanifest(observer));
        await pair.RunTicksSync(10);
        Assert.That(ClientHolds(netBody), Is.False, "Hidden again, the body leaves with the observer.");

        await pair.CleanReturnAsync();
    }
}
