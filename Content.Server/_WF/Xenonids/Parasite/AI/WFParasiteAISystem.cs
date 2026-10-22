using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Player;

namespace Content.Server._WF.Xenonids.Parasite.AI;

/// <summary>Adds and removes the hunting HTN of an AI facehugger: off while it sits in a container (a host's face, a xeno's grip) or a player drives it, on again when it is free.</summary>
public sealed class WFParasiteAISystem : EntitySystem
{
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFParasiteAIComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFParasiteAIComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<WFParasiteAIComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<WFParasiteAIComponent, PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<WFParasiteAIComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMapInit(Entity<WFParasiteAIComponent> ent, ref MapInitEvent args)
    {
        EnsureHtn(ent);
    }

    private void OnInserted(Entity<WFParasiteAIComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        RemComp<HTNComponent>(ent);
    }

    private void OnRemoved(Entity<WFParasiteAIComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        EnsureHtn(ent);
    }

    private void OnPlayerDetached(Entity<WFParasiteAIComponent> ent, ref PlayerDetachedEvent args)
    {
        EnsureHtn(ent);
    }

    private void OnMobStateChanged(Entity<WFParasiteAIComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            RemComp<HTNComponent>(ent);
    }

    /// <summary>Gives the hugger its HTN unless something else is driving it.</summary>
    public void EnsureHtn(Entity<WFParasiteAIComponent> ent)
    {
        if (TerminatingOrDeleted(ent) || HasComp<HTNComponent>(ent) || HasComp<ActorComponent>(ent))
            return;

        if (_mobState.IsDead(ent) || _container.IsEntityInContainer(ent))
            return;

        var htn = AddComp<HTNComponent>(ent);
        htn.RootTask = new HTNCompoundTask { Task = ent.Comp.RootTask };
        htn.Blackboard.SetValue(NPCBlackboard.Owner, ent.Owner);
        htn.Blackboard.SetValue(WFParasiteAIComponent.LeapRangeKey, ent.Comp.LeapRange);
        _npc.WakeNPC(ent, htn);
    }
}
