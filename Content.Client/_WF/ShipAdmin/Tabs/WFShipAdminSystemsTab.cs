using System.Globalization;
using Content.Client.UserInterface.Controls;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.ShipAdmin;
using Content.Shared.Administration;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.ShipAdmin.Tabs;

/// <summary>A ship's name, radar signature, shields, power and the protections staff can switch on it.</summary>
public sealed class WFShipAdminSystemsTab : WFShipAdminTab
{
    private readonly LineEdit _name;
    private readonly Button _rename;
    private readonly LineEdit _colour;
    private readonly Button _setColour;
    private readonly CheckBox _hidden;
    private readonly CheckBox _labelHidden;
    private readonly WFShipAdminText _shieldStatus;
    private readonly Button _shieldsOn;
    private readonly Button _shieldsOff;
    private readonly Button _shieldsRestore;
    private readonly LineEdit _strength;
    private readonly Button _setStrength;
    private readonly Button _powerOn;
    private readonly ConfirmButton _powerOff;
    private readonly Dictionary<WFShipAdminProtect, CheckBox> _protect = new();

    private NetEntity _shown;
    private AdminFlags _flags;
    private bool _hasShields;

    /// <summary>The shield status line, for tests.</summary>
    public string ShieldStatus => _shieldStatus.Text;

    public override string Title => Loc.GetString("wf-ship-admin-tab-systems");

    public WFShipAdminSystemsTab()
    {
        Body.AddChild(Heading(Loc.GetString("wf-ship-admin-systems-name")));
        _name = new LineEdit { MinWidth = 260 };
        _name.IsValid = text => text.Trim().Length > 0 && text.Trim().Length <= ShuttleDeedComponent.MaxNameLength;
        _rename = new Button { Text = Loc.GetString("wf-ship-admin-systems-rename"), StyleClasses = { "ButtonSquare" } };
        _rename.OnPressed += _ => Send(new WFShipAdminRenameMessage(Grid, _name.Text));
        Body.AddChild(Row(_name, _rename));

        Body.AddChild(Heading(Loc.GetString("wf-ship-admin-systems-radar")));
        _colour = new LineEdit { MinWidth = 110, PlaceHolder = Loc.GetString("wf-ship-admin-systems-colour-hint") };
        _setColour = new Button { Text = Loc.GetString("wf-ship-admin-systems-set-colour"), StyleClasses = { "ButtonSquare" } };
        _setColour.OnPressed += _ => Send(new WFShipAdminIffMessage(Grid, _colour.Text.Trim(), null, null));
        Body.AddChild(Row(_colour, _setColour));

        _hidden = new CheckBox { Text = Loc.GetString("wf-ship-admin-systems-hidden"), ToolTip = Loc.GetString("wf-ship-admin-systems-hidden-tooltip") };
        _hidden.OnToggled += args => Send(new WFShipAdminIffMessage(Grid, null, args.Pressed, null));
        _labelHidden = new CheckBox { Text = Loc.GetString("wf-ship-admin-systems-label-hidden"), ToolTip = Loc.GetString("wf-ship-admin-systems-label-hidden-tooltip") };
        _labelHidden.OnToggled += args => Send(new WFShipAdminIffMessage(Grid, null, null, args.Pressed));
        Body.AddChild(Row(_hidden, _labelHidden));

        Body.AddChild(Heading(Loc.GetString("wf-ship-admin-systems-shields")));
        _shieldStatus = Status();
        Body.AddChild(_shieldStatus);
        _shieldsOn = Switch("wf-ship-admin-systems-shields-on", WFShipAdminAction.Shields, true);
        _shieldsOff = Switch("wf-ship-admin-systems-shields-off", WFShipAdminAction.Shields, false);
        _shieldsRestore = new Button
        {
            Text = Loc.GetString("wf-ship-admin-systems-shields-restore"),
            ToolTip = Loc.GetString("wf-ship-admin-systems-shields-restore-tooltip"),
            StyleClasses = { "ButtonSquare" },
        };
        _shieldsRestore.OnPressed += _ => Send(new WFShipAdminShieldMessage(Grid, 100f));
        Body.AddChild(Row(_shieldsOn, _shieldsOff, _shieldsRestore));

        _strength = new LineEdit { MinWidth = 90, PlaceHolder = Loc.GetString("wf-ship-admin-systems-shields-strength-hint") };
        _strength.IsValid = text => TryStrength(text, out _);
        _setStrength = new Button
        {
            Text = Loc.GetString("wf-ship-admin-systems-shields-set"),
            ToolTip = Loc.GetString("wf-ship-admin-systems-shields-set-tooltip"),
            StyleClasses = { "ButtonSquare" },
        };
        _setStrength.OnPressed += _ =>
        {
            if (TryStrength(_strength.Text, out var percent))
                Send(new WFShipAdminShieldMessage(Grid, percent));
        };
        _strength.OnTextChanged += _ => GateStrength();
        Body.AddChild(Row(_strength, _setStrength));

        Body.AddChild(Heading(Loc.GetString("wf-ship-admin-systems-power")));
        _powerOn = Switch("wf-ship-admin-systems-power-on", WFShipAdminAction.Power, true);
        _powerOff = new ConfirmButton
        {
            Text = Loc.GetString("wf-ship-admin-systems-power-off"),
            ToolTip = Loc.GetString("wf-ship-admin-systems-power-off-tooltip"),
            ConfirmationText = Loc.GetString("wf-ship-admin-confirm"),
            StyleClasses = { "ButtonSquare" },
        };
        _powerOff.OnPressed += _ => Send(new WFShipAdminActionMessage(Grid, WFShipAdminAction.Power, false));
        Body.AddChild(Row(_powerOn, _powerOff));

        Body.AddChild(Heading(Loc.GetString("wf-ship-admin-systems-protect")));
        foreach (var protect in Enum.GetValues<WFShipAdminProtect>())
        {
            if (protect == WFShipAdminProtect.None)
                continue;

            var key = $"wf-ship-admin-systems-protect-{protect.ToString().ToLowerInvariant()}";
            var box = new CheckBox { Text = Loc.GetString(key), ToolTip = Loc.GetString($"{key}-tooltip") };
            var flag = protect;
            box.OnToggled += args => Send(new WFShipAdminProtectMessage(Grid, flag, args.Pressed));
            _protect[protect] = box;
            Body.AddChild(box);
        }
    }

    protected override void Show(WFShipAdminDetail detail, AdminFlags flags)
    {
        // The name and colour boxes are only refilled for another ship, so typing isn't wiped by a refresh.
        if (_shown != detail.Grid)
        {
            _shown = detail.Grid;
            _name.Text = detail.Name;
            _colour.Text = detail.IffColor;
        }

        _hidden.Pressed = detail.IffHidden;
        _labelHidden.Pressed = detail.IffLabelHidden;
        foreach (var (protect, box) in _protect)
        {
            box.Pressed = (detail.Protect & protect) != 0;
            Gate(box, WFShipAdminAction.Protect, flags);
        }

        Gate(_rename, WFShipAdminAction.Rename, flags);
        Gate(_setColour, WFShipAdminAction.Iff, flags);
        Gate(_hidden, WFShipAdminAction.Iff, flags);
        Gate(_labelHidden, WFShipAdminAction.Iff, flags);
        _flags = flags;
        _hasShields = detail.ShieldPercent != null;
        _shieldStatus.Text = detail.ShieldPercent is { } percent
            ? Loc.GetString("wf-ship-admin-systems-shields-status", ("percent", percent))
            : Loc.GetString("wf-ship-admin-systems-shields-none");
        Gate(_shieldsOn, WFShipAdminAction.Shields, flags, _hasShields);
        Gate(_shieldsOff, WFShipAdminAction.Shields, flags, _hasShields);
        Gate(_shieldsRestore, WFShipAdminAction.Shields, flags, _hasShields);
        GateStrength();
        Gate(_powerOn, WFShipAdminAction.Power, flags);
        Gate(_powerOff, WFShipAdminAction.Power, flags);
    }

    private void GateStrength()
    {
        Gate(_setStrength, WFShipAdminAction.Shields, _flags, _hasShields && TryStrength(_strength.Text, out _));
    }

    private static bool TryStrength(string text, out float percent)
    {
        return float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out percent)
               && percent >= 0f && percent <= 100f;
    }

    private Button Switch(string key, WFShipAdminAction action, bool value)
    {
        var button = new Button { Text = Loc.GetString(key), StyleClasses = { "ButtonSquare" } };
        button.OnPressed += _ => Send(new WFShipAdminActionMessage(Grid, action, value));
        return button;
    }
}
