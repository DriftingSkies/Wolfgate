using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Lock;
using Content.Shared.Radio.Components;
using Content.Shared.Radio.EntitySystems;

namespace Content.Shared._WG.Cyborgs;

/// <summary>
/// Opens a cyborg's encryption key slots while its chassis lock is open.
/// </summary>
public sealed class WGBorgRadioKeysSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGBorgRadioKeysComponent, InteractUsingEvent>(OnInteractUsing, before: new[] { typeof(EncryptionKeySystem) });
        SubscribeLocalEvent<WGBorgRadioKeysComponent, ExaminedEvent>(OnExamined, before: new[] { typeof(EncryptionKeySystem) });
    }

    private void OnInteractUsing(Entity<WGBorgRadioKeysComponent> ent, ref InteractUsingEvent args)
    {
        SyncLock(ent);
    }

    private void OnExamined(Entity<WGBorgRadioKeysComponent> ent, ref ExaminedEvent args)
    {
        SyncLock(ent);
    }

    // The lock is networked and the key holder isn't, so both sides read the lock right before the key system does.
    private void SyncLock(EntityUid uid)
    {
        if (TryComp<EncryptionKeyHolderComponent>(uid, out var keys))
            keys.KeysUnlocked = !TryComp<LockComponent>(uid, out var lockComp) || !lockComp.Locked;
    }
}
