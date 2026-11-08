namespace Content.Server._WF.Outposts;

/// <summary>Raised on an outpost just before a save serialises it; whatever is put on it now goes into the save.</summary>
[ByRefEvent]
public record struct WFOutpostSavingEvent;

/// <summary>Raised on an outpost once a save has serialised it, whether or not that worked.</summary>
[ByRefEvent]
public record struct WFOutpostSavedEvent;

/// <summary>Raised on a loaded save once it has settled on its ground as its owner's outpost.</summary>
[ByRefEvent]
public record struct WFOutpostSettledEvent;

/// <summary>Raised on an outpost being appraised, for what a save keeps of it other than as kept entities.</summary>
[ByRefEvent]
public record struct WFOutpostAppraiseEvent(double Value);

/// <summary>Raised on an outpost being fingerprinted, for what a save keeps of it other than as kept entities.</summary>
[ByRefEvent]
public record struct WFOutpostFingerprintEvent(int Hash);
