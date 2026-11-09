using Robust.Shared.Serialization;

namespace Content.Shared._WF.Colonies;

/// <summary>How far a world is developed, from the sum of every holder's score there.</summary>
[Serializable, NetSerializable]
public enum WFWorldTier : byte
{
    None,
    Settled,
    Colony,
    Charter,
    Dominion,
}

/// <summary>A world's development as the outpost console's Development tab shows it.</summary>
[Serializable, NetSerializable]
public sealed record WFWorldDevelopmentView(
    string World,
    WFWorldTier Tier,
    double Total,
    double? Next,
    List<WFWorldHolderView> Holders,
    string? Governor,
    int Population,
    int Housing,
    bool Blockaded,
    string You,
    List<WFColonyOutputView> Output,
    WFColonyCharterView? Charter);

/// <summary>One holder's standing on a world.</summary>
[Serializable, NetSerializable]
public sealed record WFWorldHolderView(string Key, string Name, double Points, float Share, bool Company, bool Yours, bool Governs);

/// <summary>A good the viewer's buildings make on the colony, and whether it is kept off the shelf.</summary>
[Serializable, NetSerializable]
public sealed record WFColonyOutputView(string Good, string Name, float Rate, bool Kept);

/// <summary>The charter as its governor sees it on the Development tab.</summary>
[Serializable, NetSerializable]
public sealed record WFColonyCharterView(
    bool Defence,
    bool Open,
    List<WFWorldHolderView> Permits,
    List<WFWorldHolderView> Applicants,
    List<WFWorldHolderView> Berths,
    bool YardKit);

/// <summary>The viewer's holder keeps a good it makes off the shelf, dropped to its outpost as crates, or sells it again.</summary>
[Serializable, NetSerializable]
public sealed class WFColonyKeepMessage(string good, bool keep) : BoundUserInterfaceMessage
{
    public readonly string Good = good;
    public readonly bool Keep = keep;
}

/// <summary>The governor sets whether the outposts' batteries engage campaign ships in orbit.</summary>
[Serializable, NetSerializable]
public sealed class WFColonyDefenceMessage(bool engage) : BoundUserInterfaceMessage
{
    public readonly bool Engage = engage;
}

/// <summary>The governor lets anyone found on the world, or only holders and permit holders.</summary>
[Serializable, NetSerializable]
public sealed class WFColonyOpenMessage(bool open) : BoundUserInterfaceMessage
{
    public readonly bool Open = open;
}

/// <summary>The governor grants or takes back a holder's permit to found on the world.</summary>
[Serializable, NetSerializable]
public sealed class WFColonyPermitMessage(string holder, bool granted) : BoundUserInterfaceMessage
{
    public readonly string Holder = holder;
    public readonly bool Granted = granted;
}

/// <summary>The governor opens or closes the yard's berths to a company.</summary>
[Serializable, NetSerializable]
public sealed class WFColonyBerthMessage(string holder, bool open) : BoundUserInterfaceMessage
{
    public readonly string Holder = holder;
    public readonly bool Open = open;
}

/// <summary>The governor asks for the yard platform kit, dropped at the outpost's hub, to deploy in the world's orbit.</summary>
[Serializable, NetSerializable]
public sealed class WFColonyYardKitMessage : BoundUserInterfaceMessage;
