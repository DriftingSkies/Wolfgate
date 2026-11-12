namespace Content.Server._WF.Outposts;

/// <summary>Raised on an outpost just before a save serialises it; whatever is put on it now goes into the save.</summary>
[ByRefEvent]
public record struct WFOutpostSavingEvent;

/// <summary>Raised on an outpost once a save has serialised it, whether or not that worked.</summary>
[ByRefEvent]
public record struct WFOutpostSavedEvent;

/// <summary>
/// Raised on an outpost that has never been saved: a system that sets <see cref="Wanted"/> has it given a free save
/// slot of its owner's and autosaved from then on, without the owner filing it by hand.
/// </summary>
[ByRefEvent]
public record struct WFOutpostWantsSlotEvent(bool Wanted);

/// <summary>Raised on a loaded save once it has settled on its ground as its owner's outpost.</summary>
[ByRefEvent]
public record struct WFOutpostSettledEvent;

/// <summary>Raised on an outpost being appraised, for what a save keeps of it other than as kept entities.</summary>
[ByRefEvent]
public record struct WFOutpostAppraiseEvent(double Value);

/// <summary>Raised on an outpost being fingerprinted, for what a save keeps of it other than as kept entities.</summary>
[ByRefEvent]
public record struct WFOutpostFingerprintEvent(int Hash);

/// <summary>
/// Raised before a player founds, claims or drops an outpost on a ground map; a module that sets
/// <see cref="Refusal"/> to a loc id stops it.
/// </summary>
[ByRefEvent]
public record struct WFOutpostFoundAttemptEvent(Robust.Shared.Network.NetUserId Owner, EntityUid? User, EntityUid Ground)
{
    public string? Refusal;
}

/// <summary>
/// Raised as a drop looks for a site, with the staged outpost coming down. A module that sets <see cref="SkipLots"/>
/// keeps it off the world's outpost lots; one that sets <see cref="Preferred"/> has that lot of the world's tried first.
/// </summary>
[ByRefEvent]
public record struct WFOutpostLotsEvent(Robust.Shared.Network.NetUserId Owner, EntityUid Ground, EntityUid? Outpost = null)
{
    public bool SkipLots;

    /// <summary>The index of the lot to try first, or null.</summary>
    public int? Preferred;
}

/// <summary>
/// Raised as a drop is about to land, with the save's YAML; a module that sets <see cref="Ground"/> to another world's
/// ground layer has it land there instead.
/// </summary>
[ByRefEvent]
public record struct WFOutpostDropWorldEvent(Robust.Shared.Network.NetUserId Owner, string Yaml, EntityUid Ground);

/// <summary>Raised on an outpost handed to a new owner, which makes it a new outpost.</summary>
[ByRefEvent]
public record struct WFOutpostTransferredEvent;

/// <summary>Raised on an outpost grid for the markup its save loads at.</summary>
[ByRefEvent]
public record struct WFOutpostMarkupEvent(float Markup);

/// <summary>Raised on an outpost for the cryopods it may hold.</summary>
[ByRefEvent]
public record struct WFOutpostPodLimitEvent(int Limit);

/// <summary>Raised on an outpost as its console's state is built, for other modules' parts of it.</summary>
[ByRefEvent]
public record struct WFOutpostConsoleStateEvent(Content.Shared._WF.Outposts.WFOutpostConsoleState State);

/// <summary>Raised on an outpost for the most floor it may have, the most a save of it may hold, and how far across it may spread.</summary>
[ByRefEvent]
public record struct WFOutpostCapsEvent(int Tiles, int Entities, int Span);

/// <summary>Raised on a landing pad once a hull has come to rest on it.</summary>
[ByRefEvent]
public record struct WFPadDockedEvent(EntityUid Hull);
