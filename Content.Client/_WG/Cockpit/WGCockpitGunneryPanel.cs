using System.Linq;
using Content.Client._Mono.FireControl.UI;
using Content.Client._WG.CombatConsole;
using Content.Shared._Mono.FireControl;
using Content.Shared.Shuttles.BUIStates;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Cockpit;

/// <summary>Hosts the existing weapon controls without opening a separate gunnery window.</summary>
public sealed class WGCockpitGunneryPanel : Control
{
    private readonly FireControlWindow _window = new();
    private readonly WGCockpitLease _lease = new();
    private readonly WGThreatAnnunciator _annunciator = new();
    private bool _released;

    /// <summary>Set while the panel changes the selection itself, which is not the pilot choosing.</summary>
    private bool _quiet;

    /// <summary>Forwards the existing fire-control commands through the active helm session.</summary>
    public event Action<BoundUserInterfaceMessage>? Command;

    /// <summary>Raised when the pilot changes which weapons are selected.</summary>
    public event Action? SelectionChanged;

    /// <summary>Every weapon whose key is pressed, available or not: what the pilot chose.</summary>
    public List<NetEntity> PressedWeapons => _window.WeaponsList
        .Where(pair => pair.Value.Pressed).Select(pair => pair.Key).ToList();

    /// <summary>Returns the offensive selection across the whole list, excluding countermeasure launchers.</summary>
    public List<NetEntity> SelectedWeapons => _window.WeaponsList
        .Where(pair => pair.Value.Pressed && _window.WgAvailableWeapon(pair.Key)).Select(pair => pair.Key).ToList();

    /// <summary>Whether any offensive weapon is selected, without building the list.</summary>
    public bool HasSelectedWeapons
    {
        get
        {
            foreach (var (uid, button) in _window.WeaponsList)
            {
                if (button.Pressed && _window.WgAvailableWeapon(uid))
                    return true;
            }
            return false;
        }
    }

    /// <summary>Whether this offensive weapon is selected, wherever it sits in the list.</summary>
    public bool IsSelected(NetEntity weapon) =>
        _window.WeaponsList.TryGetValue(weapon, out var button) && button.Pressed && _window.WgAvailableWeapon(weapon);

    public WGCockpitGunneryPanel()
    {
        HorizontalExpand = VerticalExpand = true;
        _window.CombatMessage += Forward;
        _window.OnServerRefresh += Refresh;
        _window.OnWeaponSelectionChanged += OnSelectionChanged;
        AddChild(_window.WgBuildCockpitGunnery(_lease));
        WGInstrumentTheme.Install(this);
    }

    /// <summary>Applies a linked console snapshot, or clears stale controls when the link is lost.</summary>
    public void UpdateState(FireControlConsoleBoundInterfaceState? state)
    {
        if (state == null)
        {
            ClearSelection();
            state = new FireControlConsoleBoundInterfaceState(false, Array.Empty<FireControllableEntry>(),
                new NavInterfaceState(250, null, null, new(), default));
        }
        UpdateStatus(state);
    }

    /// <summary>Updates the same supply, grouping and countermeasure logic as the full console.</summary>
    public void UpdateStatus(FireControlConsoleBoundInterfaceState state)
    {
        _window.UpdateStatus(state);
        _window.WgUpdateCockpitGunnery();
        if (_annunciator.Update(state.Combat.Threats, state.Connected))
            IoCManager.Resolve<IEntityManager>().System<WGConsoleAudio>().Warn();
    }

    /// <summary>Drops the previous console's targeting selection when the accessible console changes.</summary>
    public void ClearSelection()
    {
        _quiet = true;
        foreach (var button in _window.WeaponsList.Values)
            button.Pressed = false;
        _window.OnWeaponSelectionChanged?.Invoke();
        _quiet = false;
    }

    /// <summary>Selects the listed weapons that this console has, as the pilot had them before.</summary>
    public void Select(IReadOnlyCollection<NetEntity> weapons)
    {
        if (weapons.Count == 0)
            return;

        _quiet = true;
        var any = false;
        foreach (var weapon in weapons)
        {
            if (!_window.WeaponsList.TryGetValue(weapon, out var button) || button.Pressed)
                continue;

            button.Pressed = true;
            any = true;
        }

        if (any)
            _window.OnWeaponSelectionChanged?.Invoke();
        _quiet = false;
    }

    private void OnSelectionChanged()
    {
        if (!_quiet)
            SelectionChanged?.Invoke();
    }

    private void Forward(BoundUserInterfaceMessage message) => Command?.Invoke(message);
    private void Refresh() => Forward(new FireControlConsoleRefreshServerMessage());

#pragma warning disable CS0672, CS0618 // Dispose is still how the engine tears a control down, and this cleanup depends on it.
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_released)
        {
            _released = true;
            _window.CombatMessage -= Forward;
            _window.OnServerRefresh -= Refresh;
            _window.OnWeaponSelectionChanged -= OnSelectionChanged;
            _lease.Restore();
            _window.Dispose();
        }
        base.Dispose(disposing);
    }
#pragma warning restore CS0672, CS0618
}
