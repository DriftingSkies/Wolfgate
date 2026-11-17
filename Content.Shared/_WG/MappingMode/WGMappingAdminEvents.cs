using Robust.Shared.Serialization;

namespace Content.Shared._WG.MappingMode;

/// <summary>An admin leaves the mapping lobby as an admin observer on the private admin map.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingAdminObserveRequest : EntityEventArgs;

/// <summary>An admin observer goes back to the mapping lobby.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingAdminLeaveRequest : EntityEventArgs;
