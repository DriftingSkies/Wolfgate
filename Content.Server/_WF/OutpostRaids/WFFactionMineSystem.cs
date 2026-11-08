using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.StepTrigger.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.OutpostRaids;

/// <summary>A mine that only goes off under something its faction is hostile to, such as raiders and hostile wildlife.</summary>
[RegisterComponent, Access(typeof(WFFactionMineSystem))]
public sealed partial class WFFactionMineComponent : Component
{
    [DataField(required: true)]
    public ProtoId<NpcFactionPrototype> Faction;
}

/// <summary>Keeps faction mines quiet under anyone their faction isn't hostile to.</summary>
public sealed class WFFactionMineSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private NpcFactionSystem _faction = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFFactionMineComponent, StepTriggerAttemptEvent>(OnStepAttempt);
    }

    private void OnStepAttempt(Entity<WFFactionMineComponent> ent, ref StepTriggerAttemptEvent args)
    {
        if (!_proto.TryIndex(ent.Comp.Faction, out var faction) || !_faction.IsMemberOfAny(args.Tripper, faction.Hostile))
            args.Cancelled = true;
    }
}
