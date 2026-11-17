namespace Content.Shared._WG.RMCCore.Explosion;

[ByRefEvent]
public readonly record struct CMClusterSpawnedEvent(List<EntityUid> Spawned, List<EntityUid> HitEntities, EntityUid OriginEntity);
