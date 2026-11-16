using Content.Client.UserInterface.Controls;
using Content.Shared._Mono.Grid;
using Content.Shared._WF.ShipAdmin;
using Content.Shared.Administration;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.ShipAdmin.Tabs;

/// <summary>Free upgrades: refit tiers, the best machine parts and hull modifiers.</summary>
public sealed class WFShipAdminUpgradesTab : WFShipAdminTab
{
    private readonly BoxContainer _refits;
    private readonly ConfirmButton _fit;
    private readonly WFShipAdminDropdown _modifiers;
    private readonly ConfirmButton _modify;
    private readonly List<string> _modifierIds = new();

    public override string Title => Loc.GetString("wf-ship-admin-tab-upgrades");

    public WFShipAdminUpgradesTab()
    {
        Body.AddChild(Heading(Loc.GetString("wf-ship-admin-upgrades-refits")));
        Body.AddChild(Note(Loc.GetString("wf-ship-admin-upgrades-refits-note")));
        _refits = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        Body.AddChild(_refits);

        Body.AddChild(Heading(Loc.GetString("wf-ship-admin-upgrades-parts")));
        _fit = new ConfirmButton
        {
            Text = Loc.GetString("wf-ship-admin-upgrades-fit"),
            ToolTip = Loc.GetString("wf-ship-admin-upgrades-fit-tooltip"),
            ConfirmationText = Loc.GetString("wf-ship-admin-confirm"),
            StyleClasses = { "ButtonSquare" },
        };
        _fit.OnPressed += _ => Send(new WFShipAdminActionMessage(Grid, WFShipAdminAction.Fitting));
        Body.AddChild(Left(_fit));

        Body.AddChild(Heading(Loc.GetString("wf-ship-admin-upgrades-modifiers")));
        Body.AddChild(Note(Loc.GetString("wf-ship-admin-upgrades-modifiers-note")));
        _modifiers = Dropdown();
        foreach (var modifier in IoCManager.Resolve<IPrototypeManager>().EnumeratePrototypes<GridModificationPrototype>())
            _modifierIds.Add(modifier.ID);

        _modifierIds.Sort(string.CompareOrdinal);
        for (var i = 0; i < _modifierIds.Count; i++)
            _modifiers.AddItem(_modifierIds[i], i);

        _modify = new ConfirmButton
        {
            Text = Loc.GetString("wf-ship-admin-upgrades-modify"),
            ConfirmationText = Loc.GetString("wf-ship-admin-confirm"),
            StyleClasses = { "ButtonSquare" },
        };
        _modify.OnPressed += _ =>
        {
            var index = _modifiers.SelectedId;
            if (index >= 0 && index < _modifierIds.Count)
                Send(new WFShipAdminModifierMessage(Grid, _modifierIds[index]));
        };
        Body.AddChild(Row(_modifiers, _modify));
    }

    /// <summary>How many refit lines the tab lists, for tests.</summary>
    public int RefitCount => _refits.ChildCount;

    protected override void Show(WFShipAdminDetail detail, AdminFlags flags)
    {
        var busy = detail.FixTotal > 0 || (detail.Flags & WFShipAdminRowFlags.Fixing) != 0;
        _refits.RemoveAllChildren();
        foreach (var refit in detail.Refits)
            _refits.AddChild(RefitRow(refit, flags, busy));

        Gate(_fit, WFShipAdminAction.Fitting, flags);
        Gate(_modify, WFShipAdminAction.Modifier, flags, _modifierIds.Count > 0);
    }

    private Control RefitRow(WFShipAdminRefit refit, AdminFlags flags, bool busy)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, SeparationOverride = 4 };
        row.AddChild(Line(refit.Name, Loc.GetString("wf-ship-admin-upgrades-tier", ("tier", refit.Tier), ("max", refit.MaxTier)), false));
        for (var tier = refit.Tier + 1; tier <= refit.MaxTier; tier++)
        {
            var button = new ConfirmButton
            {
                Text = Loc.GetString("wf-ship-admin-upgrades-to-tier", ("tier", tier)),
                ConfirmationText = Loc.GetString("wf-ship-admin-confirm"),
                StyleClasses = { "ButtonSquare" },
            };
            Gate(button, WFShipAdminAction.Refit, flags, !busy);
            var id = refit.Id;
            var target = tier;
            button.OnPressed += _ => Send(new WFShipAdminRefitMessage(Grid, id, target));
            row.AddChild(button);
        }

        return row;
    }
}
