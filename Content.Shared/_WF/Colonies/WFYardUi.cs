using Robust.Shared.Serialization;

namespace Content.Shared._WF.Colonies;

[Serializable, NetSerializable]
public enum WFYardConsoleUiKey : byte
{
    Key,
}

/// <summary>The yard console as one viewer sees it: the designs their company may lay down here and the builds on the berths.</summary>
[Serializable, NetSerializable]
public sealed class WFYardConsoleState(
    string world,
    string? governor,
    string? reason,
    List<WFYardDesignView> designs,
    List<WFYardJobView> jobs) : BoundUserInterfaceMessage
{
    public readonly string World = world;
    public readonly string? Governor = governor;

    /// <summary>Why the viewer can't lay down a build here, localised, or null when they can.</summary>
    public readonly string? Reason = reason;

    public readonly List<WFYardDesignView> Designs = designs;
    public readonly List<WFYardJobView> Jobs = jobs;
}

[Serializable, NetSerializable]
public sealed record WFYardDesignView(string Id, string Name, string Description, int Stages, int Components, int Metals, bool Licensed);

[Serializable, NetSerializable]
public sealed record WFYardJobView(
    int Id,
    string Company,
    string Design,
    int Stage,
    int Stages,
    string StageName,
    int Components,
    int ComponentsNeeded,
    int Metals,
    int MetalsNeeded,
    float Progress,
    bool Yours);

/// <summary>Lays down a design on the yard for the viewer's company.</summary>
[Serializable, NetSerializable]
public sealed class WFYardOrderMessage(string design) : BoundUserInterfaceMessage
{
    public readonly string Design = design;
}

/// <summary>Scraps one of the viewer's company's builds; what went in is lost.</summary>
[Serializable, NetSerializable]
public sealed class WFYardScrapMessage(int job) : BoundUserInterfaceMessage
{
    public readonly int Job = job;
}
