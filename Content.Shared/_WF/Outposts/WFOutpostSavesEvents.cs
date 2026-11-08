using Robust.Shared.Serialization;

namespace Content.Shared._WF.Outposts;

/// <summary>The character editor asks for the player's outpost saves.</summary>
[Serializable, NetSerializable]
public sealed class WFOutpostSavesRequestEvent : EntityEventArgs;

/// <summary>A player's outpost save slots, empty ones as null, sent to the character editor.</summary>
[Serializable, NetSerializable]
public sealed class WFOutpostSavesEvent : EntityEventArgs
{
    public readonly List<WFOutpostSaveSlot?> Slots;

    public WFOutpostSavesEvent(List<WFOutpostSaveSlot?> slots)
    {
        Slots = slots;
    }
}

/// <summary>The character editor empties one of the player's save slots.</summary>
[Serializable, NetSerializable]
public sealed class WFOutpostDeleteSaveRequestEvent : EntityEventArgs
{
    public readonly int Slot;

    public WFOutpostDeleteSaveRequestEvent(int slot)
    {
        Slot = slot;
    }
}

/// <summary>Asks for one of the player's outpost saves, to preview it.</summary>
[Serializable, NetSerializable]
public sealed class WFOutpostPreviewRequestEvent : EntityEventArgs
{
    public readonly int Slot;

    public WFOutpostPreviewRequestEvent(int slot)
    {
        Slot = slot;
    }
}

/// <summary>Server to client: a save's grid YAML, or null with a Fluent id saying why there is none.</summary>
[Serializable, NetSerializable]
public sealed class WFOutpostPreviewEvent : EntityEventArgs
{
    public readonly int Slot;

    /// <summary>When the save was written; with the slot, it tells one version of a save from another.</summary>
    public readonly DateTime Saved;

    public readonly string? Yaml;
    public readonly string? Error;

    public WFOutpostPreviewEvent(int slot, DateTime saved, string? yaml, string? error)
    {
        Slot = slot;
        Saved = saved;
        Yaml = yaml;
        Error = error;
    }
}
