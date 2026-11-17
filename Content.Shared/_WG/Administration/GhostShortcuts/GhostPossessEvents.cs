using Robust.Shared.Serialization;

namespace Content.Shared._WG.Administration.GhostShortcuts;

/// <summary>
/// Admin asks the server to put a ghost's player into a body. Sent by the occupied-body prompt; the first attempt
/// arrives as a normal drag-drop. Admin-only; ignored otherwise.
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostPossessRequestEvent : EntityEventArgs
{
    public NetEntity Ghost;
    public NetEntity Body;

    /// <summary>
    /// Ghost whoever is in the body first instead of aborting.
    /// </summary>
    public bool Replace;

    /// <summary>
    /// The occupant mind the admin was shown; a replace only goes ahead while that is still who is in the body.
    /// </summary>
    public NetEntity Occupant;

    public GhostPossessRequestEvent(NetEntity ghost, NetEntity body, bool replace, NetEntity occupant)
    {
        Ghost = ghost;
        Body = body;
        Replace = replace;
        Occupant = occupant;
    }
}

/// <summary>
/// Server tells the admin the body already has a player so the client can ask whether to replace them.
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostPossessOccupiedEvent : EntityEventArgs
{
    public NetEntity Ghost;
    public NetEntity Body;

    /// <summary>
    /// Mind of the occupant described below, echoed back by the Replace button.
    /// </summary>
    public NetEntity Occupant;
    public string BodyName = string.Empty;
    public string GhostPlayer = string.Empty;
    public string OccupantCharacter = string.Empty;
    public string OccupantPlayer = string.Empty;

    public GhostPossessOccupiedEvent(NetEntity ghost, NetEntity body, NetEntity occupant)
    {
        Ghost = ghost;
        Body = body;
        Occupant = occupant;
    }
}
