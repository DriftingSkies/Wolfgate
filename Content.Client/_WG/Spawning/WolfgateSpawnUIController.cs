using JetBrains.Annotations;
using Robust.Client.Placement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.Spawning;

/// <summary>
/// Drives <see cref="WolfgateSpawnWindow"/>, which replaces the engine's flat entity spawn panel. Placement
/// behaviour is kept identical to the engine controller so admin muscle memory still works.
/// </summary>
[UsedImplicitly]
public sealed partial class WolfgateSpawnUIController : UIController
{
    [Dependency] private IPlacementManager _placement = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private WolfgateSpawnWindow? _window;
    private EntityPrototype? _selected;

    /// <summary>Set while we drive the placement manager ourselves, since starting placement clears the old one.</summary>
    private bool _placing;

    public override void Initialize()
    {
        base.Initialize();

        _placement.DirectionChanged += OnDirectionChanged;
        _placement.PlacementChanged += OnPlacementChanged;
        _proto.PrototypesReloaded += _ => _window?.Reload();
    }

    /// <summary>Opens the spawn window with the search focused, or closes it if open.</summary>
    public void ToggleWindow()
    {
        EnsureWindow();

        if (_window!.IsOpen)
        {
            _window.Close();
            return;
        }

        _window.Open();
        _window.SetRotation(_placement.Direction.ToString());
        _window.FocusSearch();
    }

    /// <summary>Closes the spawn window if it exists.</summary>
    public void CloseWindow()
    {
        if (_window is { Disposed: false })
            _window.Close();
    }

    private void EnsureWindow()
    {
        if (_window is { Disposed: false })
            return;

        _window = UIManager.CreateWindow<WolfgateSpawnWindow>();
        LayoutContainer.SetAnchorPreset(_window, LayoutContainer.LayoutPreset.CenterLeft);

        _window.SetPlacementModes(_placement.AllModeNames);
        _window.SetReplace(_placement.Replacement);
        _window.SetErase(_placement.Eraser);

        _window.OnEntrySelected += OnEntrySelected;
        _window.OnReplaceToggled += pressed => _placement.Replacement = pressed;
        _window.OnEraseToggled += OnEraseToggled;
        _window.OnOverrideSelected += OnOverrideSelected;
        _window.OnClose += OnWindowClosed;
    }

    private void OnEntrySelected(WolfgateSpawnEntry? entry)
    {
        if (entry == null)
        {
            _selected = null;
            _placement.Clear();
            return;
        }

        _selected = entry.Prototype;
        BeginPlacing(entry.Prototype);
    }

    private void BeginPlacing(EntityPrototype prototype)
    {
        if (_window == null)
            return;

        var mode = _placement.AllModeNames[Math.Clamp(_window.OverrideId, 0, _placement.AllModeNames.Length - 1)];

        _placing = true;
        try
        {
            _placement.BeginPlacing(new PlacementInformation
            {
                PlacementOption = mode != IPlacementManager.DefaultModeName ? mode : prototype.PlacementMode,
                EntityType = prototype.ID,
                Range = 2,
                IsTile = false,
            });
        }
        finally
        {
            _placing = false;
        }
    }

    private void OnEraseToggled(bool pressed)
    {
        _placement.Clear();

        // Clearing already toggles the eraser off, so only turn it back on when the button went down.
        if (pressed)
            _placement.ToggleEraser();

        _window?.SetErase(pressed);
    }

    private void OnOverrideSelected(int id)
    {
        if (_selected != null && _placement.CurrentMode != null)
            BeginPlacing(_selected);
    }

    private void OnWindowClosed()
    {
        _selected = null;
        _window?.ClearSelection();
        _placement.Clear();
    }

    private void OnPlacementChanged(object? sender, EventArgs e)
    {
        if (_placing)
            return;

        _selected = null;
        _window?.ClearSelection();
        _window?.SetErase(false);
    }

    private void OnDirectionChanged(object? sender, EventArgs e)
    {
        _window?.SetRotation(_placement.Direction.ToString());
    }
}
