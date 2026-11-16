using Content.Shared._WF.Modsuits;
using Content.Shared._WF.Modsuits.Components;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;

namespace Content.Shared._WF.Asf;

/// <summary>
/// Ties a modsuit's personal shield to its seal: no shield on an unsealed suit, and unsealing drops it.
/// </summary>
public sealed partial class ShieldRequiresSealSystem : EntitySystem
{
    [Dependency] private ItemToggleSystem _toggle = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShieldRequiresSealComponent, ItemToggleActivateAttemptEvent>(OnActivateAttempt);
        SubscribeLocalEvent<ShieldRequiresSealComponent, ModSuitUpdatedEvent>(OnSuitUpdated);
    }

    private void OnActivateAttempt(Entity<ShieldRequiresSealComponent> ent, ref ItemToggleActivateAttemptEvent args)
    {
        if (args.Cancelled || TryComp<ModSuitComponent>(ent, out var suit) && suit.State == ModSuitState.Active)
            return;

        args.Cancelled = true;
        args.Popup = Loc.GetString(ent.Comp.UnsealedPopup);
    }

    private void OnSuitUpdated(Entity<ShieldRequiresSealComponent> ent, ref ModSuitUpdatedEvent args)
    {
        if (!TryComp<ModSuitComponent>(ent, out var suit) || suit.State != ModSuitState.Active)
            _toggle.TryDeactivate(ent.Owner);
    }
}
