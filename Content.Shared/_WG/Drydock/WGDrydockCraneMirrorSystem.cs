using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools;
using Content.Shared.Tools.Systems;
using Content.Shared.Wires;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Drydock;

/// <summary>
/// Mirrors a crane in place: with its panel open and its bolts off, a Cutting tool moves its arm to the body's other side.
/// </summary>
public sealed partial class WGDrydockCraneMirrorSystem : EntitySystem
{
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private SharedWiresSystem _wires = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private static readonly ProtoId<ToolQualityPrototype> CuttingQuality = "Cutting";

    /// <summary>Seconds the cut takes.</summary>
    private const float MirrorDelay = 2f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WGDrydockCraneComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WGDrydockCraneComponent, WGDrydockCraneMirrorDoAfterEvent>(OnMirrorDoAfter);
    }

    private void OnInteractUsing(Entity<WGDrydockCraneComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !ent.Comp.MirrorInPlace || !_tool.HasQuality(args.Used, CuttingQuality))
            return;

        args.Handled = true;
        if (CanMirror(ent, args.User, popup: true))
            _tool.UseTool(args.Used, args.User, ent, MirrorDelay, CuttingQuality, new WGDrydockCraneMirrorDoAfterEvent());
    }

    private void OnMirrorDoAfter(Entity<WGDrydockCraneComponent> ent, ref WGDrydockCraneMirrorDoAfterEvent args)
    {
        // The panel or bolts may have changed during the cut.
        if (args.Cancelled || args.Handled || !CanMirror(ent, args.User, popup: false))
            return;

        args.Handled = true;
        ent.Comp.Mirrored = !ent.Comp.Mirrored;
        Dirty(ent);
        _popup.PopupClient(Loc.GetString("drydock-crane-mirror-done"), ent, args.User);
    }

    /// <summary>Whether the crane's panel is open and it is unanchored, telling the user what is missing if not.</summary>
    private bool CanMirror(EntityUid crane, EntityUid user, bool popup)
    {
        string? refusal = null;
        if (!_wires.IsPanelOpen(crane))
            refusal = "drydock-crane-mirror-panel-closed";
        else if (Transform(crane).Anchored)
            refusal = "drydock-crane-mirror-anchored";

        if (refusal != null && popup)
            _popup.PopupClient(Loc.GetString(refusal), crane, user);

        return refusal == null;
    }
}
