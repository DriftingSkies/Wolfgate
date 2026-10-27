using Content.Shared._WF.ShipAccess;
using Content.Shared.Silicons.StationAi;
using Content.Shared.StationRecords;

namespace Content.Server._WF.ShipAccess;

public sealed partial class WFShipAccessServerSystem
{
    /// <summary>Keeps each AI core's ship key on the grid it is anchored to.</summary>
    private void InitializeAi()
    {
        SubscribeLocalEvent<StationAiCoreComponent, ComponentStartup>(OnAiCoreStartup);
        SubscribeLocalEvent<StationAiCoreComponent, AnchorStateChangedEvent>(OnAiCoreAnchorChanged);
    }

    private void OnAiCoreStartup(Entity<StationAiCoreComponent> ent, ref ComponentStartup args)
    {
        UpdateAiKey(ent);
    }

    private void OnAiCoreAnchorChanged(Entity<StationAiCoreComponent> ent, ref AnchorStateChangedEvent args)
    {
        UpdateAiKey(ent);
    }

    /// <summary>An anchored core carries its grid's AI key in the key storage its prototype gives it; a loose one carries none.</summary>
    private void UpdateAiKey(Entity<StationAiCoreComponent> ent)
    {
        if (!TryComp<StationRecordKeyStorageComponent>(ent, out var storage))
            return;

        var xform = Transform(ent);
        StationRecordKey? key = xform.Anchored && xform.GridUid is { } grid ? WFShipAiAccessSystem.AiKey(grid) : null;
        if (storage.Key.Equals(key))
            return;

        storage.Key = key;
        Dirty(ent, storage);
    }
}
