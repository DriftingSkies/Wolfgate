using Content.Shared.Alert;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Waddle;

/// <summary>
/// Shows the waddling alert on the wearer while <see cref="WaddleWhenWornComponent"/> shoes are worn and toggled on.
/// </summary>
public sealed partial class WaddleAlertSystem : EntitySystem
{
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private ItemToggleSystem _toggle = default!;

    private static readonly ProtoId<AlertPrototype> WaddlingAlert = "Waddling";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WaddleWhenWornComponent, ClothingGotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<WaddleWhenWornComponent, ClothingGotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<WaddleWhenWornComponent, ItemToggledEvent>(OnToggled);
    }

    private void OnEquipped(Entity<WaddleWhenWornComponent> ent, ref ClothingGotEquippedEvent args)
    {
        SetAlert(args.Wearer, _toggle.IsActivated(ent.Owner));
    }

    private void OnUnequipped(Entity<WaddleWhenWornComponent> ent, ref ClothingGotUnequippedEvent args)
    {
        SetAlert(args.Wearer, false);
    }

    private void OnToggled(Entity<WaddleWhenWornComponent> ent, ref ItemToggledEvent args)
    {
        if (TryComp<ClothingComponent>(ent, out var clothing) && clothing.InSlotFlag == SlotFlags.FEET)
            SetAlert(Transform(ent).ParentUid, args.Activated);
    }

    private void SetAlert(EntityUid wearer, bool shown)
    {
        if (shown)
            _alerts.ShowAlert(wearer, WaddlingAlert);
        else
            _alerts.ClearAlert(wearer, WaddlingAlert);
    }
}
