using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Player;

namespace Content.Server._WG.Xenonids.Parasite.AI;

/// <summary>Adds and removes the hunting HTN of an AI facehugger: off while it sits in a container (a host's face, a xeno's grip) or a player drives it, on again when it is free.</summary>
public sealed partial class WGParasiteAISystem : EntitySystem
{
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGParasiteAIComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WGParasiteAIComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<WGParasiteAIComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<WGParasiteAIComponent, PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<WGParasiteAIComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMapInit(Entity<WGParasiteAIComponent> ent, ref MapInitEvent args)
    {
        EnsureHtn(ent);
    }

    private void OnInserted(Entity<WGParasiteAIComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        RemComp<HTNComponent>(ent);
    }

    private void OnRemoved(Entity<WGParasiteAIComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        EnsureHtn(ent);
    }

    private void OnPlayerDetached(Entity<WGParasiteAIComponent> ent, ref PlayerDetachedEvent args)
    {
        EnsureHtn(ent);
    }

    private void OnMobStateChanged(Entity<WGParasiteAIComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            RemComp<HTNComponent>(ent);
    }

    /// <summary>Gives the hugger its HTN unless something else is driving it.</summary>
    public void EnsureHtn(Entity<WGParasiteAIComponent> ent)
    {
        if (TerminatingOrDeleted(ent) || HasComp<HTNComponent>(ent) || HasComp<ActorComponent>(ent))
            return;

        if (_mobState.IsDead(ent) || _container.IsEntityInContainer(ent))
            return;

        var htn = AddComp<HTNComponent>(ent);
        htn.RootTask = new HTNCompoundTask { Task = ent.Comp.RootTask };
        htn.Blackboard.SetValue(NPCBlackboard.Owner, ent.Owner);
        htn.Blackboard.SetValue(WGParasiteAIComponent.LeapRangeKey, ent.Comp.LeapRange);
        _npc.WakeNPC(ent, htn);
    }
}
