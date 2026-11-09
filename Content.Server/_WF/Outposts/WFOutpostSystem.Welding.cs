using Content.Server._WF.Outposts.Components;
using Content.Shared._WF.Outposts;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Content.Shared.Tools.Systems;

namespace Content.Server._WF.Outposts;

/// <summary>Welding outpost structures into another in place: a prefab wall welded fast, or cut loose to pack up again.</summary>
public sealed partial class WFOutpostSystem
{
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private DamageableSystem _damageable = default!;

    private const string WeldingQuality = "Welding";

    private void InitializeWelding()
    {
        SubscribeLocalEvent<WFWeldSwapComponent, InteractUsingEvent>(OnWeldSwapInteractUsing);
        SubscribeLocalEvent<WFWeldSwapComponent, WFWeldSwapDoAfterEvent>(OnWeldSwapDoAfter);
    }

    private void OnWeldSwapInteractUsing(Entity<WFWeldSwapComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !_tool.HasQuality(args.Used, WeldingQuality))
            return;

        args.Handled = true;
        if (Transform(ent).GridUid is { } grid && TryComp<WFOutpostComponent>(grid, out var outpost) && !CanBuild(args.User, (grid, outpost)))
        {
            _popup.PopupEntity(Loc.GetString("wf-outpost-weld-not-yours"), ent, args.User);
            return;
        }

        _tool.UseTool(args.Used, args.User, ent, ent.Comp.Delay, WeldingQuality, new WFWeldSwapDoAfterEvent(), ent.Comp.Fuel);
    }

    private void OnWeldSwapDoAfter(Entity<WFWeldSwapComponent> ent, ref WFWeldSwapDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        var swapped = WeldSwap(ent);
        if (ent.Comp.Done is { } done)
            _popup.PopupEntity(Loc.GetString(done), swapped, args.User);
        _adminLog.Add(LogType.WFOutpost, LogImpact.Low,
            $"{ToPrettyString(args.User):user} welded {ToPrettyString(ent.Owner):entity} into {ToPrettyString(swapped):entity}");
    }

    /// <summary>Replaces the structure with what it welds into, on the same spot and facing, keeping its damage.</summary>
    public EntityUid WeldSwap(Entity<WFWeldSwapComponent> ent)
    {
        var xform = Transform(ent);
        var swapped = Spawn(ent.Comp.Into, xform.Coordinates);
        _transform.SetLocalRotation(swapped, xform.LocalRotation);
        if (TryComp<DamageableComponent>(ent, out var old) && TryComp<DamageableComponent>(swapped, out var fresh))
            _damageable.SetDamage(swapped, fresh, old.Damage);
        QueueDel(ent);
        return swapped;
    }
}
