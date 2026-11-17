using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Warheads;

/// <summary>How a designed warhead can be set off. Any mix of flags.</summary>
[Flags]
public enum WGWarheadFuzes : byte
{
    None = 0,
    Timer = 1 << 0,
    TwoPerson = 1 << 1,
    Remote = 1 << 2,
    Impact = 1 << 3,
}

[Serializable, NetSerializable]
public sealed class WGNukeDesignerEuiState : EuiStateBase
{
    /// <summary>Payload presets by prototype ID, to start a design from.</summary>
    public Dictionary<string, WGWarheadPayload> Presets = new();

    /// <summary>Warhead entity prototypes a design can be built in, with their display names.</summary>
    public Dictionary<string, string> Chassis = new();

    /// <summary>Explosion prototype IDs.</summary>
    public List<string> ExplosionTypes = new();

    public string? Status;
}

/// <summary>A design: a chassis, its payload, how it arms, and what to call it.</summary>
[Serializable, NetSerializable]
public sealed class WGNukeDesign
{
    public string Chassis = string.Empty;
    public WGWarheadPayload Payload = new();
    public WGWarheadFuzes Fuzes = WGWarheadFuzes.Timer;
    public float Timer = 120f;
    public string? Name;

    /// <summary>Starts the countdown as soon as it spawns.</summary>
    public bool StartCountdown;
}

/// <summary>Spawns the design at the admin's position.</summary>
[Serializable, NetSerializable]
public sealed class WGNukeDesignerSpawnMessage(WGNukeDesign design) : EuiMessageBase
{
    public readonly WGNukeDesign Design = design;
}

/// <summary>Sets off the design's payload at the admin's position straight away, with no warhead.</summary>
[Serializable, NetSerializable]
public sealed class WGNukeDesignerDetonateMessage(WGWarheadPayload payload) : EuiMessageBase
{
    public readonly WGWarheadPayload Payload = payload;
}

public static class WGNukeDesignerCommands
{
    public const string Open = "nukedesigner";
}
