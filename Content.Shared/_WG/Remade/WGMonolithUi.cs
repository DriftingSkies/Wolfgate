using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Remade;

[Serializable, NetSerializable]
public enum WGMonolithVisuals : byte
{
    Stage,
}

[Serializable, NetSerializable]
public enum WGMonolithTapUiKey : byte
{
    Key,
}

/// <summary>What the Tap's window shows. Null resonance means no Scion is in reach.</summary>
[Serializable, NetSerializable]
public sealed class WGMonolithTapState(
    float? resonance,
    WGMonolithStage stage,
    float[] dials,
    float output,
    float climb,
    bool barred,
    bool hasSilo,
    int shards)
    : BoundUserInterfaceState
{
    public readonly float? Resonance = resonance;
    public readonly WGMonolithStage Stage = stage;

    /// <summary>The dial of each Gift, indexed by <see cref="WGGift"/>, as the stone runs it: locked at full once it has rooted.</summary>
    public readonly float[] Dials = dials;

    public readonly float Output = output;

    /// <summary>Resonance gained a minute at the current dials.</summary>
    public readonly float Climb = climb;

    public readonly bool Barred = barred;

    /// <summary>Whether a material silo on the Scion's ship can take what Matter makes.</summary>
    public readonly bool HasSilo = hasSilo;

    public readonly int Shards = shards;

    public float Dial(WGGift gift) => (int) gift < Dials.Length ? Dials[(int) gift] : 0f;
}

/// <summary>Sets one Gift's dial on the Tap, from 0 to 1.</summary>
[Serializable, NetSerializable]
public sealed class WGMonolithTapSetDrawMessage(WGGift gift, float draw) : BoundUserInterfaceMessage
{
    public readonly WGGift Gift = gift;
    public readonly float Draw = draw;
}

/// <summary>Finishes feeding a corpse to the Tap.</summary>
[Serializable, NetSerializable]
public sealed partial class WGMonolithFeedDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Finishes chipping a shard off a Scion.</summary>
[Serializable, NetSerializable]
public sealed partial class WGMonolithChipDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Finishes cutting a shard free of the growth that holds it.</summary>
[Serializable, NetSerializable]
public sealed partial class WGMonolithShardCutDoAfterEvent : SimpleDoAfterEvent;
