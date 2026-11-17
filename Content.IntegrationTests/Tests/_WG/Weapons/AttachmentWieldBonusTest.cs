using System.IO;
using Content.Shared._ES.Weapons.Ranged.Attachments;
using Content.Shared._ES.Weapons.Ranged.Attachments.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Weapons;

/// <summary>
/// Gun attachments and the wield bonus: a wielded gun takes an attachment's wield modifiers at once and loses them
/// when it comes off, a saved gun still has its bonus when loaded, and a gun with no wield bonus takes attachments
/// without complaint.
/// </summary>
[TestFixture]
[TestOf(typeof(ESSharedGunAttachmentsSystem))]
public sealed class AttachmentWieldBonusTest
{
    private const string Wielder = "MobHuman";
    private const string WieldedGun = "WeaponLMGMR8B";
    private const string Grip = "GunAttachmentGripErebusVFG";
    private const string Pistol = "WeaponPistolViper";
    private const string Suppressor = "GunAttachmentSuppressorMulticalPistol";

    /// <summary>
    /// The spread of a wielded gun right after an attachment goes on or comes off is already what a refresh gives,
    /// and taking the attachment off again restores what the gun had before.
    /// </summary>
    [Test]
    public async Task WieldedGunFollowsAttachment()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var attachments = entMan.System<ESSharedGunAttachmentsSystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var guns = entMan.System<SharedGunSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var wield = entMan.System<SharedWieldableSystem>();

        await server.WaitAssertion(() =>
        {
            var user = entMan.SpawnEntity(Wielder, map.GridCoords);
            var gun = entMan.SpawnEntity(WieldedGun, map.GridCoords);
            var grip = entMan.SpawnEntity(Grip, map.GridCoords);
            var gunComp = entMan.GetComponent<GunComponent>(gun);

            (double Min, double Max, double Decay, double Increase) Spread()
            {
                var min = gunComp.MinAngleModified;
                var max = gunComp.MaxAngleModified;
                var decay = gunComp.AngleDecayModified;
                var increase = gunComp.AngleIncreaseModified;
                return (min.Degrees, max.Degrees, decay.Degrees, increase.Degrees);
            }

            Assert.That(hands.TryPickupAnyHand(user, gun), Is.True);
            Assert.That(wield.TryWield(gun, entMan.GetComponent<WieldableComponent>(gun), user), Is.True);
            var bare = Spread();

            Assert.That(attachments.TryFindEmptyValidSlot(gun, grip, out var slot), Is.True);
            Assert.That(attachments.TryInsertAttachment(gun, grip, slot!.Value), Is.True);
            var attached = Spread();
            guns.RefreshModifiers(gun);
            Assert.Multiple(() =>
            {
                Assert.That(attached, Is.Not.EqualTo(bare), "the grip changes a wielded gun's spread");
                Assert.That(attached, Is.EqualTo(Spread()), "attached: stale until the next refresh");
            });

            Assert.That(containers.Remove(grip, containers.GetContainer(gun, slot.Value.ContainerId)), Is.True);
            var removed = Spread();
            guns.RefreshModifiers(gun);
            Assert.Multiple(() =>
            {
                Assert.That(removed, Is.EqualTo(Spread()), "removed: stale until the next refresh");
                Assert.That(removed, Is.EqualTo(bare), "removed: the grip's wield bonus stayed on the gun");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A gun saved with a grip on it, as a safety deposit box or a used ship does, wields like a fresh one when
    /// loaded. Loaded entities skip map init, which is the only place the wield bonus was worked out.
    /// </summary>
    [Test]
    public async Task LoadedGunKeepsWieldBonus()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var attachments = entMan.System<ESSharedGunAttachmentsSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var loader = entMan.System<MapLoaderSystem>();
        var transforms = entMan.System<SharedTransformSystem>();
        var wield = entMan.System<SharedWieldableSystem>();

        await server.WaitAssertion(() =>
        {
            (double Min, double Max, double Decay, double Increase) WieldedSpread(EntityUid gun)
            {
                var user = entMan.SpawnEntity(Wielder, map.GridCoords);
                Assert.That(hands.TryPickupAnyHand(user, gun), Is.True);
                Assert.That(wield.TryWield(gun, entMan.GetComponent<WieldableComponent>(gun), user), Is.True);

                var gunComp = entMan.GetComponent<GunComponent>(gun);
                var min = gunComp.MinAngleModified;
                var max = gunComp.MaxAngleModified;
                var decay = gunComp.AngleDecayModified;
                var increase = gunComp.AngleIncreaseModified;
                return (min.Degrees, max.Degrees, decay.Degrees, increase.Degrees);
            }

            var fresh = entMan.SpawnEntity(WieldedGun, map.GridCoords);
            var grip = entMan.SpawnEntity(Grip, map.GridCoords);
            Assert.That(attachments.TryFindEmptyValidSlot(fresh, grip, out var slot), Is.True);
            Assert.That(attachments.TryInsertAttachment(fresh, grip, slot!.Value), Is.True);

            using var writer = new StringWriter();
            Assert.That(loader.TrySaveEntity(fresh, writer), Is.True);
            using var reader = new StringReader(writer.ToString());
            Assert.That(loader.TryLoadEntity(reader, nameof(LoadedGunKeepsWieldBonus), out var loaded), Is.True);
            transforms.SetCoordinates(loaded!.Value.Owner, map.GridCoords);

            Assert.That(WieldedSpread(loaded.Value.Owner), Is.EqualTo(WieldedSpread(fresh)));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A pistol has attachment slots but no wield bonus; putting a suppressor on and taking it off logs no error.
    /// </summary>
    [Test]
    public async Task GunWithoutWieldBonusTakesAttachments()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var attachments = entMan.System<ESSharedGunAttachmentsSystem>();
        var containers = entMan.System<SharedContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var gun = entMan.SpawnEntity(Pistol, map.GridCoords);
            var suppressor = entMan.SpawnEntity(Suppressor, map.GridCoords);
            Assert.That(entMan.HasComponent<GunWieldBonusComponent>(gun), Is.False, "the pistol gained a wield bonus");

            Assert.That(attachments.TryFindEmptyValidSlot(gun, suppressor, out var slot), Is.True);
            Assert.That(attachments.TryInsertAttachment(gun, suppressor, slot!.Value), Is.True);
            Assert.That(containers.Remove(suppressor, containers.GetContainer(gun, slot.Value.ContainerId)), Is.True);
        });

        await pair.CleanReturnAsync();
    }
}
