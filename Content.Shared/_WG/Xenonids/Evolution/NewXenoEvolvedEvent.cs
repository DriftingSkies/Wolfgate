namespace Content.Shared._WG.Xenonids.Evolution;

[ByRefEvent]
public readonly record struct NewXenoEvolvedEvent(Entity<XenoEvolutionComponent> OldXeno, EntityUid NewXeno, bool SubtractPoints);
