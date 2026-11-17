using Content.Shared._WG.Defcon;

namespace Content.Server._WG.Defcon;

/// <summary>One source's share of the sector threat, with the reason shown in banners.</summary>
public readonly record struct WGDefconContribution(float Score, string Reason);

/// <summary>Raised (broadcast) each time the DEFCON is scored, for threat sources to add to.</summary>
public sealed class WGDefconGatherEvent(WGDefconProfilePrototype profile, TimeSpan now) : EntityEventArgs
{
    public readonly WGDefconProfilePrototype Profile = profile;
    public readonly TimeSpan Now = now;
    public readonly List<WGDefconContribution> Contributions = new();

    /// <summary>The calmest level any source allows.</summary>
    public int Floor = WGDefconTracker.Calmest;

    public void Add(float score, string reason)
    {
        if (score > 0f)
            Contributions.Add(new WGDefconContribution(score, reason));
    }
}

/// <summary>Raised (broadcast) after the DEFCON level changes, or DEFCON is turned on or off.</summary>
public sealed class WGDefconChangedEvent(int old, int level) : EntityEventArgs
{
    public readonly int Old = old;
    public readonly int Level = level;
}
