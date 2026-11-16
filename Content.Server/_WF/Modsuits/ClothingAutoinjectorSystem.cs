using Content.Shared._WF.Modsuits.Support;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Modsuits;

/// <summary>Injects the wearer with an emergency reagent mix when they go critical.</summary>
public sealed partial class ClothingAutoinjectorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ClothingAutoInjectComponent, InventoryRelayedEvent<ClothingAutoInjectRelayedEvent>>(OnInject);
        SubscribeLocalEvent<ClothingAutoInjectComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChange);
    }

    private void OnMobStateChange(MobStateChangedEvent args)
    {
        if (!TryComp<InventoryComponent>(args.Target, out var inventory))
            return;

        var ev = new ClothingAutoInjectRelayedEvent(args.Target, args.NewMobState);
        _inventory.RelayEvent((args.Target, inventory), ref ev);
    }

    private void OnInject(Entity<ClothingAutoInjectComponent> ent, ref InventoryRelayedEvent<ClothingAutoInjectRelayedEvent> args)
    {
        if (!ent.Comp.AutoInjectOnCrit
            || args.Args.NewState != MobState.Critical
            || ent.Comp.NextAutoInjectTime > _timing.CurTime)
            return;

        if (!TryInjectReagents(args.Args.Target, ent.Comp.Reagents))
            return;

        _audio.PlayPvs(ent.Comp.InjectSound, args.Args.Target);
        _popup.PopupEntity(Loc.GetString(ent.Comp.Popup), args.Args.Target, args.Args.Target);
        ent.Comp.NextAutoInjectTime = _timing.CurTime + ent.Comp.AutoInjectInterval;
    }

    private bool TryInjectReagents(EntityUid uid, Dictionary<string, FixedPoint2> reagents)
    {
        var solution = new Solution();
        foreach (var (reagent, quantity) in reagents)
            solution.AddReagent(reagent, quantity);

        if (!_solution.TryGetInjectableSolution(uid, out var targetSolution, out _))
            return false;

        return _solution.TryAddSolution(targetSolution.Value, solution);
    }

    private void OnExamined(Entity<ClothingAutoInjectComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var onMsg = ent.Comp.NextAutoInjectTime < _timing.CurTime
            ? Loc.GetString("comp-autoinjector-examined-on")
            : Loc.GetString("comp-autoinjector-examined-off",
                ("time", Math.Floor(ent.Comp.NextAutoInjectTime.TotalSeconds - _timing.CurTime.TotalSeconds)));
        args.PushMarkup(onMsg);
    }
}
