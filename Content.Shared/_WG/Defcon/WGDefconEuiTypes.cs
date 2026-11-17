using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Defcon;

/// <summary>Console command names for the DEFCON tools.</summary>
public static class WGDefconCommands
{
    public const string Panel = "wg_defconpanel";
}

/// <summary>One level as the panel shows it.</summary>
[Serializable, NetSerializable]
public sealed class WGDefconLevelEntry(int level, string name, Color color)
{
    public readonly int Level = level;
    public readonly string Name = name;
    public readonly Color Color = color;
}

/// <summary>One source's share of the score as the panel shows it.</summary>
[Serializable, NetSerializable]
public sealed class WGDefconReasonEntry(float score, string reason)
{
    public readonly float Score = score;
    public readonly string Reason = reason;
}

/// <summary>Everything the DEFCON panel shows.</summary>
[Serializable, NetSerializable]
public sealed class WGDefconEuiState : EuiStateBase
{
    public bool Enabled;
    public int Level;
    public int? Locked;
    public float Score;
    public float Bias;
    public int Floor;
    public List<WGDefconLevelEntry> Levels = new();
    public List<WGDefconReasonEntry> Reasons = new();
}

/// <summary>Sets the level, optionally locking it there and announcing it.</summary>
[Serializable, NetSerializable]
public sealed class WGDefconSetLevelMessage(int level, bool locked, bool announce) : EuiMessageBase
{
    public readonly int Level = level;
    public readonly bool Locked = locked;
    public readonly bool Announce = announce;
}

/// <summary>Lets the level follow the threat again.</summary>
[Serializable, NetSerializable]
public sealed class WGDefconUnlockMessage : EuiMessageBase;

/// <summary>Sets the score added on top of the sources.</summary>
[Serializable, NetSerializable]
public sealed class WGDefconBiasMessage(float bias) : EuiMessageBase
{
    public readonly float Bias = bias;
}

/// <summary>Scores the threat now.</summary>
[Serializable, NetSerializable]
public sealed class WGDefconEvaluateMessage : EuiMessageBase;
