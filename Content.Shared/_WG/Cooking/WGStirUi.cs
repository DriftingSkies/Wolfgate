using Robust.Shared.Serialization;

namespace Content.Shared._WG.Cooking;

[Serializable, NetSerializable]
public enum WGStirUiKey : byte
{
    Key,
}

/// <summary>The stirring minigame is done: <see cref="Score"/> from 0 (sloppy) to 1 (three smooth turns in the ring).</summary>
[Serializable, NetSerializable]
public sealed class WGStirMessage(float score) : BoundUserInterfaceMessage
{
    public readonly float Score = score;
}
