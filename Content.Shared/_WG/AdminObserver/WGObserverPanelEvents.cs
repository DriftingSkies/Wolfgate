using Content.Shared.Actions;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.AdminObserver;

/// <summary>Server to client: open the observer panel.</summary>
[Serializable, NetSerializable]
public sealed class WGObserverPanelOpenEvent : EntityEventArgs;

/// <summary>Client to server: these are my staff marker choices now. Saved at once; applied at once if I am marked.</summary>
[Serializable, NetSerializable]
public sealed class WGObserverSetMarkerEvent : EntityEventArgs
{
    public WGStaffMarkerSettings Marker = new();
}

/// <summary>Client to server: add this avatar preset, or replace the one called <see cref="Original"/> with it.</summary>
[Serializable, NetSerializable]
public sealed class WGObserverSavePresetEvent : EntityEventArgs
{
    /// <summary>The preset being edited, by the name it has now; null for a new one.</summary>
    public string? Original;

    public WGAvatarPreset Preset = new();
}

/// <summary>Client to server: delete my avatar preset of this name.</summary>
[Serializable, NetSerializable]
public sealed class WGObserverDeletePresetEvent : EntityEventArgs
{
    public string Name = string.Empty;
}

/// <summary>Client to server: a plain manifest uses this preset from now on; null for the ghost.</summary>
[Serializable, NetSerializable]
public sealed class WGObserverSetDefaultPresetEvent : EntityEventArgs
{
    public string? Name;
}

/// <summary>Client to server: open one of my observer's built-in consoles.</summary>
[Serializable, NetSerializable]
public sealed class WGObserverToolRequestEvent : EntityEventArgs
{
    public WGObserverTool Tool;
}

/// <summary>The consoles an admin observer carries in itself.</summary>
[Serializable, NetSerializable]
public enum WGObserverTool : byte
{
    Solar,
    Communications,
    Radar,
    Cargo,
    CrewMonitor,
    Records,
}

/// <summary>Raised on an admin observer by its Observer panel action.</summary>
public sealed partial class WGOpenObserverPanelEvent : InstantActionEvent;
