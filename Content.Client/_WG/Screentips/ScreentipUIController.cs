using System.Numerics;
using Content.Client.Gameplay;
using Content.Client.Viewport;
using Content.Shared._WG.Screentips;
using Content.Shared.IdentityManagement;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._WG.Screentips;

/// <summary>
/// Shows the name of the entity under the cursor, at the top of the game view or beside the cursor.
/// </summary>
public sealed class ScreentipUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IStateManager _state = default!;

    /// <summary>How far the tip sits from the cursor or from the top of the view, in UI units.</summary>
    private static readonly Vector2 Offset = new(16, 16);

    private PanelContainer? _panel;
    private Label? _label;
    private string? _colorHex;

    /// <summary>The entity the tip names, or null while it is hidden.</summary>
    public EntityUid? Shown { get; private set; }

    /// <summary>The tip's text, for tests.</summary>
    public string? Text => _label?.Text;

    public void OnStateEntered(GameplayState state)
    {
        _label = new Label { StyleClasses = { "LabelKeyText" } };
        // A new label has no colour yet, so the next Show applies the configured one.
        _colorHex = null;
        _panel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.Black.WithAlpha(0.6f),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 2,
                ContentMarginBottomOverride = 2,
            },
            MouseFilter = Control.MouseFilterMode.Ignore,
            Visible = false,
        };
        _panel.AddChild(_label);
        UIManager.PopupRoot.AddChild(_panel);
    }

    public void OnStateExited(GameplayState state)
    {
        _panel?.Orphan();
        _panel = null;
        _label = null;
        Shown = null;
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_panel == null || _label == null)
            return;

        var mode = (ScreentipMode) _cfg.GetCVar(ScreentipCVars.Mode);
        if (mode == ScreentipMode.Off
            || UIManager.CurrentlyHovered is not IViewportControl viewport
            || GetHovered(viewport) is not { } entity)
        {
            Hide();
            return;
        }

        Show(entity, Identity.Name(entity, EntityManager, _player.LocalEntity), mode, (Control) viewport);
    }

    /// <summary>Shows the tip for <paramref name="entity"/>. Split from the frame update so tests can drive it.</summary>
    public void Show(EntityUid entity, string name, ScreentipMode mode, Control viewport)
    {
        if (_panel == null || _label == null)
            return;

        var hex = _cfg.GetCVar(ScreentipCVars.Color);
        if (hex != _colorHex)
        {
            _colorHex = hex;
            _label.FontColorOverride = Color.TryFromHex(hex, out var color) ? color : Color.White;
        }

        Shown = entity;
        _label.Text = name;
        _panel.Visible = true;
        _panel.Measure(Vector2Helpers.Infinity);
        var size = _panel.DesiredSize;

        Vector2 position;
        if (mode == ScreentipMode.NextToCursor)
        {
            position = UIManager.MousePositionScaled.Position + Offset;
            var root = UIManager.PopupRoot.Size;
            position = Vector2.Clamp(position, Vector2.Zero, Vector2.Max(Vector2.Zero, root - size));
        }
        else
        {
            position = viewport.GlobalPosition + new Vector2((viewport.Size.X - size.X) / 2, Offset.Y);
        }

        LayoutContainer.SetPosition(_panel, position);
    }

    private void Hide()
    {
        Shown = null;
        if (_panel != null)
            _panel.Visible = false;
    }

    private EntityUid? GetHovered(IViewportControl viewport)
    {
        if (_state.CurrentState is not GameplayStateBase screen || !_input.MouseScreenPosition.IsValid)
            return null;

        var world = viewport.PixelToMap(_input.MouseScreenPosition.Position);
        return viewport is ScalingViewport scaling
            ? screen.GetClickedEntity(world, scaling.Eye)
            : screen.GetClickedEntity(world);
    }
}
