using System.Numerics;
using Content.Shared._WG.SectorControl;

namespace Content.Server._WG.AdsIncursion.Components;

/// <summary>A node under way to found itself, on its encounter entity. Its lead down stops it; arrived, it becomes a node.</summary>
[RegisterComponent]
public sealed partial class WGAdsDeploymentComponent : Component
{
    /// <summary>The cell it goes to found a node in.</summary>
    [ViewVariables]
    public WGSectorCell Target;

    /// <summary>The point it flies to: the target cell's centre, or a clear point near it.</summary>
    [ViewVariables]
    public Vector2 TargetPosition;

    /// <summary>The node it set out from.</summary>
    [ViewVariables]
    public EntityUid Source;

    /// <summary>The key of its lead ship.</summary>
    [ViewVariables]
    public string Lead = string.Empty;

    [ViewVariables]
    public TimeSpan Launched;

    /// <summary>When it has taken too long: it founds where it stands if that is in its cell, and is ended otherwise.</summary>
    [ViewVariables]
    public TimeSpan Overdue;
}
