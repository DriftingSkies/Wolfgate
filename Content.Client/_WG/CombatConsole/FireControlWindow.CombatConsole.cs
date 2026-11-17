using System.Linq;
using System.Numerics;
using Content.Client._WG.CombatConsole;
using Content.Shared._Mono.FireControl;
using Content.Shared._Mono.ShipGuns;
using Content.Shared._WG.CombatConsole;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client._WG.CombatConsole.WGInstrumentTheme;

namespace Content.Client._Mono.FireControl.UI;

public sealed partial class FireControlWindow
{
    private readonly Button[] _wgRecall = new Button[WGWeaponGroups.Count];
    private readonly Button[] _wgSave = new Button[WGWeaponGroups.Count];
    private WGCombatConsoleState? _wgCombat;
    private Control _wgBatteryControls = default!;
    private Control _wgCountermeasures = default!;
    private Button _wgStore = default!;
    private Button _wgTypeSelect = default!;
    private Control _wgTypeChoices = default!;
    private Button _wgAutomatic = default!;
    private Button _wgDispense = default!;
    private Label _wgFlareStatus = default!;
    private Label _wgSelection = default!;
    private Label _wgThreats = default!;
    private readonly WGThreatAnnunciator _wgAnnunciator = new();

    /// <summary>Forwards validated console settings through the existing bound interface.</summary>
    public event Action<BoundUserInterfaceMessage>? CombatMessage;

    private void WgInitializeCombatConsole()
    {
        MinSize = new Vector2(960, 600);
        FitWindow(this, new Vector2(1180, 780));
        SelectAllButton.Text = Loc.GetString("wg-gunnery-all");
        UnselectAllButton.Text = Loc.GetString("wg-gunnery-clear");
        SelectBallisticButton.Text = Loc.GetString("wg-console-ballistic");
        SelectEnergyButton.Text = Loc.GetString("wg-console-energy");
        SelectMissileButton.Text = Loc.GetString("wg-console-missile");
        foreach (var button in new[] { SelectAllButton, UnselectAllButton, SelectBallisticButton, SelectEnergyButton, SelectMissileButton })
        {
            button.Margin = new Thickness(0);
            button.AddStyleClass("WgCompact");
        }

        _wgStore = Button("wg-gunnery-store", true);
        _wgStore.Name = "CockpitGroupStore";
        _wgStore.AddStyleClass("WgCompact");
        _wgStore.HorizontalExpand = false;
        _wgStore.SetWidth = 58;
        _wgStore.Disabled = true;
        _wgStore.ToolTip = Loc.GetString("wg-gunnery-store-help");
        _wgStore.OnToggled += _ => WgShowGroupMode();
        var groups = Row();
        groups.SeparationOverride = 3;
        for (var i = 0; i < WGWeaponGroups.Count; i++)
        {
            var slot = i;
            var recall = _wgRecall[i] = new Button { HorizontalExpand = true, Name = $"CockpitGroupRecall{i}" };
            recall.AddStyleClass("WgCompact");
            recall.Text = Loc.GetString("wg-gunnery-group", ("slot", i + 1), ("count", 0));
            var save = _wgSave[i] = new Button
            {
                HorizontalExpand = true,
                Name = $"CockpitGroupSave{i}",
                Text = Loc.GetString("wg-gunnery-save-slot", ("slot", i + 1)),
                Visible = false,
                ToolTip = Loc.GetString("wg-console-group-save-help"),
            };
            save.AddStyleClass("WgCompact");
            save.AddStyleClass("WgGroup");
            recall.Disabled = save.Disabled = true;
            recall.OnPressed += _ => WgRecallGroup(slot);
            save.OnPressed += _ =>
            {
                CombatMessage?.Invoke(new WGSaveWeaponGroupMessage(slot,
                    WeaponsList.Where(p => p.Value.Pressed && !(_wgCombat?.FlareLaunchers.Contains(p.Key) ?? false))
                        .Select(p => p.Key).ToList()));
                _wgStore.Pressed = false;
                WgShowGroupMode();
            };
            var holder = new Control { HorizontalExpand = true, SetHeight = 32 };
            holder.AddChild(recall);
            holder.AddChild(save);
            groups.AddChild(holder);
        }
        groups.AddChild(_wgStore);

        _wgTypeSelect = Button("wg-gunnery-type", true);
        _wgTypeSelect.Name = "WgWeaponTypeSelect";
        _wgTypeSelect.AddStyleClass("WgCompact");
        _wgTypeChoices = Row(SelectBallisticButton, SelectEnergyButton, SelectMissileButton);
        _wgTypeChoices.Name = "WgWeaponTypes";
        _wgTypeChoices.Visible = false;
        _wgTypeSelect.OnToggled += args => _wgTypeChoices.Visible = args.Pressed;
        foreach (var button in new[] { SelectBallisticButton, SelectEnergyButton, SelectMissileButton })
        {
            button.OnPressed += _ =>
            {
                _wgTypeSelect.Pressed = false;
                _wgTypeChoices.Visible = false;
            };
        }
        var selection = Row(SelectAllButton, UnselectAllButton, _wgTypeSelect);
        selection.SeparationOverride = 3;
        _wgSelection = Label("wg-console-selection", Muted);
        _wgSelection.ClipText = true;
        _wgSelection.HorizontalExpand = true;
        _wgThreats = Label("wg-gunnery-threat-offline", Muted);
        _wgThreats.Name = "WgMissileAlert";
        _wgThreats.ClipText = true;
        _wgThreats.HorizontalExpand = true;

        _wgAutomatic = Button("wg-console-auto-safe", true);
        _wgAutomatic.Name = "CockpitAutomaticFlares";
        _wgAutomatic.AddStyleClass("WgCompact");
        _wgAutomatic.Visible = false;
        _wgAutomatic.Disabled = true;
        _wgAutomatic.ToolTip = Loc.GetString("wg-console-auto-help");
        _wgAutomatic.OnToggled += args => CombatMessage?.Invoke(new WGAutomaticFlaresMessage(args.Pressed));
        _wgDispense = Button("wg-console-dispense");
        _wgDispense.Name = "CockpitDispenseFlares";
        _wgDispense.AddStyleClass("WgDispense");
        _wgDispense.AddStyleClass("WgCompact");
        _wgDispense.Visible = false;
        _wgDispense.Disabled = true;
        _wgDispense.OnPressed += _ => CombatMessage?.Invoke(new WGDispenseFlaresMessage());
        _wgFlareStatus = Label("wg-gunnery-flare-offline", Muted);
        _wgFlareStatus.Name = "CockpitFlareSupply";
        _wgFlareStatus.ClipText = true;
        _wgFlareStatus.HorizontalExpand = true;
        _wgFlareStatus.MouseFilter = MouseFilterMode.Pass;
        var countermeasures = Column(_wgFlareStatus, Row(_wgAutomatic, _wgDispense));
        countermeasures.SeparationOverride = 2;
        countermeasures.HorizontalExpand = true;
        countermeasures.Name = "WgCountermeasurePanel";
        countermeasures.Visible = false;
        _wgCountermeasures = countermeasures;

        var head = Column(groups, selection, _wgTypeChoices, _wgSelection);
        head.SeparationOverride = 5;
        var tail = Column(_wgThreats, countermeasures);
        tail.SeparationOverride = 5;
        var controls = new WGWeaponBank(head, ControllablesBox, tail);
        _wgBatteryControls = controls;
        var battery = Panel("wg-gunnery-battery", controls, true);
        battery.Name = "WgWeaponBattery";
        battery.SetWidth = 324;
        battery.HorizontalExpand = false;
        var scope = Scope("wg-console-fire-scope", NavRadar, new Vector2(240, 120));
        // WOLFGATE(ShipShields): the gunner's shield control, on the right.
        // WOLFGATE(AsteroidMining): the mining laser panel sits under the shield control.
        var right = Column(WgCreateShieldPanel(), WgCreateMiningPanel());
        right.VerticalExpand = true;
        var instruments = Row(battery, scope, right);
        instruments.VerticalExpand = true;
        RefreshButton.Name = "CockpitGunneryRefresh";
        RefreshButton.Text = Loc.GetString("wg-gunnery-link");
        RefreshButton.AddStyleClass("WgCompact");
        RefreshButton.Margin = new Thickness(0);
        RefreshButton.HorizontalExpand = false;
        RefreshButton.SetWidth = 72;
        ServerStatus.HorizontalExpand = true;
        ServerStatus.HorizontalAlignment = HAlignment.Stretch;
        ServerStatus.ClipText = true;
        var header = Row(Label("wg-console-fire-title", Accent), ServerStatus, RefreshButton);
        foreach (var button in new[] { IFFToggle, IFFDetailedToggle })
        {
            button.SetWidth = 72;
            button.AddStyleClass("WgCompact");
            button.Margin = new Thickness(0);
        }
        var footer = Row(IFFToggle, IFFDetailedToggle);
        footer.HorizontalExpand = false;
        footer.HorizontalAlignment = HAlignment.Right;
        var body = Column(header, instruments, footer);
        body.Margin = new Thickness(10);
        body.HorizontalExpand = body.VerticalExpand = true;
        ContentsContainer.RemoveAllChildren();
        ContentsContainer.AddChild(body);
        Install(this);
        OnWeaponSelectionChanged += WgUpdateSelection;
        WgUpdateSelection();
    }

    private void WgRecallGroup(int slot)
    {
        if (_wgCombat == null)
            return;
        var selected = _wgCombat.Groups[slot].ToHashSet();
        foreach (var (uid, button) in WeaponsList)
            button.Pressed = selected.Contains(uid);
        OnWeaponSelectionChanged?.Invoke();
        UpdateAllWeaponButtonTexts();
    }

    private void WgShowGroupMode()
    {
        for (var i = 0; i < WGWeaponGroups.Count; i++)
        {
            _wgRecall[i].Visible = !_wgStore.Pressed;
            _wgSave[i].Visible = _wgStore.Pressed;
        }
    }

    private void WgUpdateSelection()
    {
        foreach (var (uid, button) in WeaponsList)
        {
            if (_wgCombat?.FlareLaunchers.Contains(uid) == true)
                button.Pressed = false;
        }
        var selected = WeaponsList.Where(p => p.Value.Pressed).Select(p => p.Key).ToHashSet();
        _wgSelection.Text = Loc.GetString("wg-gunnery-selection", ("count", selected.Count));
        for (var i = 0; i < WGWeaponGroups.Count; i++)
            _wgRecall[i].Pressed = _wgCombat != null && _wgCombat.Groups[i].Count > 0 &&
                                   selected.SetEquals(_wgCombat.Groups[i]);
    }

    private void WgUpdateCombatConsole(FireControlConsoleBoundInterfaceState state)
    {
        if (_wgAnnunciator.Update(state.Combat.Threats, IsOpen && state.Connected))
            IoCManager.Resolve<IEntityManager>().System<WGConsoleAudio>().Warn();
        _wgCombat = state.Combat;
        var combat = state.Combat;
        _weaponTypes.Clear();
        foreach (var (uid, type) in combat.WeaponTypes)
            _weaponTypes[uid] = type;
        SelectBallisticButton.Disabled = !_weaponTypes.Values.Contains(ShipGunType.Ballistic);
        SelectEnergyButton.Disabled = !_weaponTypes.Values.Contains(ShipGunType.Energy);
        SelectMissileButton.Disabled = !_weaponTypes.Values.Contains(ShipGunType.Missile);
        foreach (var (uid, button) in WeaponsList)
        {
            WgStyleWeapon(button, uid);
            var available = !combat.FlareLaunchers.Contains(uid);
            ControllablesBox.SetWeapon(button, uid, available);
            if (!available)
                button.Pressed = false;
        }
        for (var i = 0; i < WGWeaponGroups.Count; i++)
        {
            _wgRecall[i].Text = Loc.GetString("wg-gunnery-group", ("slot", i + 1), ("count", combat.Groups[i].Count));
            _wgRecall[i].ToolTip = Loc.GetString("wg-gunnery-group-help", ("slot", i + 1), ("count", combat.Groups[i].Count));
            _wgRecall[i].Disabled = !state.Connected || combat.Groups[i].Count == 0;
            _wgSave[i].Disabled = !state.Connected;
        }
        _wgStore.Disabled = !state.Connected;
        if (!state.Connected)
        {
            _wgStore.Pressed = false;
            WgShowGroupMode();
        }
        _wgCountermeasures.Visible = _wgAutomatic.Visible = _wgDispense.Visible = combat.FlareLaunchers.Count > 0;
        _wgAutomatic.Pressed = combat.Automatic;
        _wgAutomatic.Disabled = !state.Connected || combat.FlareLaunchers.Count == 0;
        _wgAutomatic.Text = Loc.GetString(combat.Automatic ? "wg-console-auto-armed" : "wg-console-auto-safe");
        _wgDispense.Disabled = !state.Connected || combat.FlareLaunchers.Count == 0 ||
                              combat.Ammunition <= 0 && !combat.UnlimitedSupply || combat.Cooldown > 0;
        var supplied = combat.Ammunition > 0 || combat.UnlimitedSupply;
        var flareState = !state.Connected ? "wg-gunnery-flare-offline" :
            !supplied ? "wg-gunnery-flare-empty" : combat.Cooldown > 0 ? "wg-gunnery-flare-rearming" : "wg-gunnery-flare-ready";
        _wgFlareStatus.Text = Loc.GetString(flareState,
            ("ammo", combat.UnlimitedSupply ? Loc.GetString("wg-gunnery-flare-unlimited") : combat.Ammunition.ToString()),
            ("seconds", (int) Math.Ceiling(combat.Cooldown)));
        _wgFlareStatus.FontColorOverride = !state.Connected ? Muted : !supplied ? Red : combat.Cooldown > 0 ? Accent : Cream;
        _wgFlareStatus.ToolTip = combat.FlareLaunchers.Count == 0 ? Loc.GetString("wg-console-no-launchers") :
            Loc.GetString(combat.UnlimitedSupply ? "wg-console-flare-autoloader-status" : "wg-console-flare-status",
                ("count", combat.FlareLaunchers.Count), ("ammo", combat.Ammunition), ("seconds", (int) Math.Ceiling(combat.Cooldown)));
        _wgThreats.Text = Loc.GetString(!state.Connected ? "wg-gunnery-threat-offline" :
            combat.Threats == 0 ? "wg-gunnery-threat-clear" : "wg-gunnery-threat-incoming", ("count", combat.Threats));
        _wgThreats.FontColorOverride = !state.Connected ? Muted : combat.Threats == 0 ? Muted : Red;
        ServerStatus.FontColorOverride = state.Connected ? Green : Red;
        WgUpdateSelection();
    }
}
