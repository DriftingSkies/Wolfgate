using Content.Shared._WF.CustomMarkings;
using Content.Shared.Humanoid.Markings;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.MarkingToggles;

/// <summary>Client to server: show or hide one of my body's markings.</summary>
[Serializable, NetSerializable]
public sealed class MarkingToggleRequestEvent(MarkingCategories category, string markingId, bool visible) : EntityEventArgs
{
    public readonly MarkingCategories Category = category;
    public readonly string MarkingId = markingId;
    public readonly bool Visible = visible;
}

/// <summary>Client to server: show or hide one of the custom markings my body wears.</summary>
[Serializable, NetSerializable]
public sealed class CustomMarkingToggleRequestEvent(CustomMarking marking, bool visible) : EntityEventArgs
{
    public readonly CustomMarking Marking = marking;
    public readonly bool Visible = visible;
}
