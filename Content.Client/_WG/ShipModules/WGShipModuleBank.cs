using Content.Client._WG.CombatConsole;
using Content.Shared._WG.ShipModules;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WG.ShipModules;

/// <summary>
/// Helm strip with one key per fitted ship module: its charges, its state and, in the tooltip, its hotkey. Pressing a key
/// fires the module, and a key lights red for a moment each time its module fires. Hidden on a ship with no modules.
/// </summary>
public sealed partial class WGShipModuleBank : GridContainer
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    /// <summary>How long a key stays lit after its module fires.</summary>
    private static readonly TimeSpan FlashTime = TimeSpan.FromSeconds(0.45);

    /// <summary>The console theme's red key face.</summary>
    private const string LitClass = "WgDispense";

    private readonly Func<EntityUid?> _ship;
    private readonly string _prefix;
    private readonly List<Button> _keys = new();
    private readonly List<TimeSpan> _litUntil = new();

    /// <summary>The ship slot each key fires, by key.</summary>
    private readonly List<int> _slotOf = new();

    /// <summary>Tracks the modules of the ship a screen is showing; control names use the fuel bank's prefix.</summary>
    public WGShipModuleBank(Func<EntityUid?> ship, string prefix = "Cockpit")
    {
        IoCManager.InjectDependencies(this);
        _ship = ship;
        _prefix = prefix;
        Name = prefix + "Modules";
        Columns = 2;
        HorizontalExpand = true;
        HSeparationOverride = 6;
        VSeparationOverride = 4;
        Refresh();
    }

    /// <summary>The ship's module strip, or null while it has none.</summary>
    public List<WGShipModuleSlot>? Slots =>
        _ship() is { } ship && _entities.TryGetComponent(ship, out WGShipModulesComponent? modules) && modules.Slots.Count > 0
            ? modules.Slots
            : null;

    protected override void EnteredTree()
    {
        base.EnteredTree();
        _entities.System<WGShipModuleHelmSystem>().Track(this);
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        _entities.System<WGShipModuleHelmSystem>().Untrack(this);
    }

    /// <summary>Shows one key per module of the ship and updates what each says.</summary>
    public void Refresh()
    {
        // Hidden rather than collapsed, so the strip's column leaves no gap for it.
        var slots = Slots;
        Visible = slots != null;
        if (slots == null)
            return;

        while (_keys.Count < slots.Count)
        {
            AddKey(_keys.Count);
        }

        var now = _timing.CurTime;
        for (var i = 0; i < _keys.Count; i++)
        {
            var key = _keys[i];
            key.Visible = i < slots.Count;
            if (!key.Visible)
                continue;

            var slot = slots[i];
            _slotOf[i] = slot.Slot;
            // Read every refresh, so a key rebound in the options shows at once.
            if (slot.Slot < WGShipModuleKeys.Slots.Length)
            {
                var tooltip = Loc.GetString("wg-ship-module-tooltip",
                    ("key", _input.GetKeyFunctionButtonString(WGShipModuleKeys.Slots[slot.Slot])));
                if (key.ToolTip != tooltip)
                    key.ToolTip = tooltip;
            }

            var label = _prototype.TryIndex(slot.Module, out var module) ? Loc.GetString(module.Label) : slot.Module.Id;
            var text = Loc.GetString("wg-ship-module-key", ("label", label),
                ("charges", slot.Charges), ("max", slot.MaxCharges)) + "\n" + Status(slot, now);
            if (key.Text != text)
                key.Text = text;

            key.Disabled = slot.Lock != WGShipModuleLock.None || slot.Charges <= 0 || now < slot.ReadyAt;
            Light(i, _timing.RealTime < _litUntil[i]);
        }
    }

    /// <summary>Lights the key of a slot that just fired, if this strip shows it.</summary>
    public bool Flash(EntityUid ship, int slot)
    {
        if (_ship() != ship || Slots is not { } slots)
            return false;

        Refresh();
        for (var i = 0; i < slots.Count && i < _keys.Count; i++)
        {
            if (slots[i].Slot != slot)
                continue;

            _litUntil[i] = _timing.RealTime + FlashTime;
            Light(i, true);
            return true;
        }

        return false;
    }

    /// <summary>The window or screen-filling view that holds this strip, which shakes as a whole.</summary>
    public Control? ShakeRoot()
    {
        for (var control = (Control?) this; control != null; control = control.Parent)
        {
            if (control is BaseWindow || control.Parent is UIScreen)
                return control;
        }

        return null;
    }

    private void Light(int slot, bool lit)
    {
        var key = _keys[slot];
        if (key.HasStyleClass(LitClass) == lit)
            return;

        if (lit)
            key.AddStyleClass(LitClass);
        else
            key.RemoveStyleClass(LitClass);

        WGInstrumentTheme.Switch(key);
    }

    private void AddKey(int index)
    {
        var key = new Button
        {
            Name = _prefix + "Module" + index,
            HorizontalExpand = true,
            MinHeight = 44,
        };
        WGInstrumentTheme.Switch(key);
        key.OnPressed += _ =>
        {
            if (_ship() is { } ship)
                _entities.System<WGShipModuleHelmSystem>().Fire(ship, _slotOf[index]);
        };
        _keys.Add(key);
        _litUntil.Add(TimeSpan.Zero);
        _slotOf.Add(-1);
        AddChild(key);
    }

    /// <summary>The second line of a module's key: what stops it, the cooldown, how long until a charge, or ready.</summary>
    public static string Status(WGShipModuleSlot slot, TimeSpan now)
    {
        if (slot.Lock != WGShipModuleLock.None)
            return Loc.GetString($"wg-ship-module-status-{slot.Lock.ToString().ToLowerInvariant()}");

        if (slot.Charges > 0)
            return Loc.GetString(now < slot.ReadyAt ? "wg-ship-module-status-cooldown" : "wg-ship-module-status-ready");

        var seconds = Math.Max(1, (int) Math.Ceiling((slot.NextCharge - now).TotalSeconds));
        return Loc.GetString("wg-ship-module-status-charging", ("seconds", seconds));
    }
}
