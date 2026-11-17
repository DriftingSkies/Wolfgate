namespace Content.Shared._WG.Remade.Signals;

/// <summary>The psychic tricks a Signal can play on the living.</summary>
public enum WGSignalTrick : byte
{
    /// <summary>The lights near the target flicker.</summary>
    Flicker,

    /// <summary>A line in one player's head.</summary>
    Whisper,

    /// <summary>A Remade's voice somewhere near one player.</summary>
    FalseSound,

    /// <summary>Words in blood on the floor near the target.</summary>
    Scrawl,

    /// <summary>A door opens or shuts by itself.</summary>
    Door,

    /// <summary>A door bolts shut for a while.</summary>
    Bolt,
}

/// <summary>What a trick needs: the lowest stage it works at, its Psi cost and its cooldown in seconds.</summary>
public readonly record struct WGSignalTrickInfo(WGMonolithStage Min, float Cost, float Cooldown);

/// <summary>The numbers behind the Signals: how many a stone holds, how fast their Psi comes back, what the tricks cost and how far they reach.</summary>
public static class WGSignalMath
{
    public const float MaxPsi = 100f;
    public const float StartPsi = 50f;

    /// <summary>Psi and cooldown for taking over a Remade, and how near it must be.</summary>
    public const float PossessCost = 20f;
    public const float PossessCooldown = 10f;
    public const float PossessRange = 8f;

    /// <summary>How far from a Signal its tricks reach.</summary>
    public const float TrickRange = 14f;

    /// <summary>Seconds a bolted door stays shut.</summary>
    public const float BoltSeconds = 30f;

    /// <summary>A Signal may drift this far past the hull of the stone's ship or any docked to it.</summary>
    public const float ShipMargin = 4f;

    /// <summary>How far a Signal may go from a stone that sits off any grid.</summary>
    public const float FreeRadius = 12f;

    /// <summary>How near a Signal must be to corruption or a rooted shard of its stone.</summary>
    public const float CorruptionReach = 4f;
    public const float ShardReach = 8f;

    /// <summary>The most Signals a stone holds at a stage.</summary>
    public static int Cap(WGMonolithStage stage)
    {
        return stage switch
        {
            WGMonolithStage.Hymn => 1,
            WGMonolithStage.Growth => 2,
            WGMonolithStage.Harvest => 3,
            WGMonolithStage.Rapture => 4,
            WGMonolithStage.Crescendo => 5,
            _ => 0,
        };
    }

    /// <summary>Psi a second.</summary>
    public static float PsiRate(WGMonolithStage stage)
    {
        return stage switch
        {
            WGMonolithStage.Hymn => 0.4f,
            WGMonolithStage.Growth => 0.7f,
            WGMonolithStage.Harvest => 1.1f,
            WGMonolithStage.Rapture => 1.6f,
            WGMonolithStage.Crescendo => 2.2f,
            _ => 0f,
        };
    }

    public static WGSignalTrickInfo Info(WGSignalTrick trick)
    {
        return trick switch
        {
            WGSignalTrick.Flicker => new(WGMonolithStage.Hymn, 2f, 5f),
            WGSignalTrick.Whisper => new(WGMonolithStage.Hymn, 8f, 25f),
            WGSignalTrick.FalseSound => new(WGMonolithStage.Hymn, 10f, 15f),
            WGSignalTrick.Scrawl => new(WGMonolithStage.Hymn, 15f, 45f),
            WGSignalTrick.Door => new(WGMonolithStage.Growth, 6f, 10f),
            _ => new(WGMonolithStage.Growth, 25f, 60f),
        };
    }
}
