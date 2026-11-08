using Robust.Shared.Serialization;

namespace Content.Shared._WF.Outposts;

/// <summary>A player in the lobby asks whether they can join the round at their outpost.</summary>
[Serializable, NetSerializable]
public sealed class WFOutpostJoinQueryEvent : EntityEventArgs;

/// <summary>The outpost a player in the lobby can wake up at, in a free cryopod, or null.</summary>
[Serializable, NetSerializable]
public sealed class WFOutpostJoinOfferEvent(string? outpost) : EntityEventArgs
{
    public readonly string? Outpost = outpost;
}

/// <summary>A player in the lobby joins the round in a free cryopod on their outpost, as its settler.</summary>
[Serializable, NetSerializable]
public sealed class WFOutpostJoinRequestEvent : EntityEventArgs;
