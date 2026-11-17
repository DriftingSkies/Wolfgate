namespace Content.Shared._WG.Roadmap;

/// <summary>
/// How far along a roadmap item is; sets its colour bar and status text.
/// </summary>
public enum RoadmapItemState : byte
{
    Planned,
    InProgress,
    Partial,
    Complete,
}
