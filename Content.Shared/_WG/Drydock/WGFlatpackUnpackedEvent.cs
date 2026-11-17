namespace Content.Shared._WG.Drydock;

/// <summary>Raised on a flatpack, on the server, once it has unpacked into <see cref="Spawned"/>.</summary>
[ByRefEvent]
public readonly record struct WGFlatpackUnpackedEvent(EntityUid Spawned);
