using Robust.Shared.Serialization;

namespace Content.Shared._WF.Hideouts;

[Serializable, NetSerializable]
public enum WFHideoutRegistryUiKey : byte
{
    Key,
}

/// <summary>
/// What the outpost registry at Central shows the player at it: their outpost standing this round or their latest save,
/// the worlds with free plots, and the prices.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFHideoutRegistryState : BoundUserInterfaceState
{
    /// <summary>The player's outpost standing this round, by name, and the world it stands on.</summary>
    public string? Outpost;
    public string? OutpostWorld;

    /// <summary>The player's latest save that could land this round, by name, with its home world and landing price.</summary>
    public string? Save;
    public string? SaveWorld;
    public int LandPrice;

    /// <summary>The player's outpost save slots are still being read.</summary>
    public bool Reading;

    /// <summary>A landing is on its way down for the player.</summary>
    public bool Landing;

    public List<WFHideoutRegistryWorld> Worlds = new();
    public int FoundPrice;
    public int RideFee;
    public int Balance;
}

/// <summary>A world outposts may stand on, with its free plots.</summary>
[Serializable, NetSerializable]
public sealed record WFHideoutRegistryWorld(string Id, string Name, int FreePlots, int Plots);

/// <summary>The player founds an outpost on a free plot of a world.</summary>
[Serializable, NetSerializable]
public sealed class WFHideoutRegistryFoundMessage(string world) : BoundUserInterfaceMessage
{
    public readonly string World = world;
}

/// <summary>The player lands their latest save on its home world.</summary>
[Serializable, NetSerializable]
public sealed class WFHideoutRegistryLandMessage : BoundUserInterfaceMessage;

/// <summary>The player rides the registry's drop down to their standing outpost.</summary>
[Serializable, NetSerializable]
public sealed class WFHideoutRegistryRideMessage : BoundUserInterfaceMessage;
