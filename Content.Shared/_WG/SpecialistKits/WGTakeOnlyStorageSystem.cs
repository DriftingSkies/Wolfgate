using Robust.Shared.Containers;

namespace Content.Shared._WG.SpecialistKits;

/// <summary>Refuses every insert into a sealed kit case.</summary>
public sealed class WGTakeOnlyStorageSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGTakeOnlyStorageComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
    }

    private void OnInsertAttempt(EntityUid uid, WGTakeOnlyStorageComponent comp, ContainerIsInsertingAttemptEvent args)
    {
        if (comp.Sealed)
            args.Cancel();
    }
}
