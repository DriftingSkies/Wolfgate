using Content.Client.Administration.Managers;
using Content.Shared._WG.AdminObserver;
using Content.Shared.Ghost;
using Content.Shared.Popups;
using Robust.Client.Console;
using Robust.Client.Player;
using Robust.Shared.Input.Binding;

namespace Content.Client._WG.AdminObserver;

/// <summary>Runs <c>aghost</c> from a key: into the admin observer, and back to the body it left.</summary>
public sealed partial class WGAdminObserverHotkeySystem : EntitySystem
{
    [Dependency] private IClientAdminManager _admin = default!;
    [Dependency] private IClientConsoleHost _console = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    [Dependency] private WGManifestClientSystem _manifest = default!;
    [Dependency] private WGObserverPanelClientSystem _panel = default!;

    private const string AghostCommand = "aghost";
    private const string ManifestCommand = "manifest";
    private const string UnmanifestCommand = "unmanifest";

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .Bind(WGAdminObserverKeys.WGToggleAghost, InputCmdHandler.FromDelegate(_ => ToggleAghost(), handle: true,
                outsidePrediction: true))
            .Bind(WGAdminObserverKeys.WGToggleManifest, InputCmdHandler.FromDelegate(_ => ToggleManifest(), handle: true,
                outsidePrediction: true))
            .Bind(WGAdminObserverKeys.WGOpenObserverPanel, InputCmdHandler.FromDelegate(_ => _panel.Toggle(), handle: true,
                outsidePrediction: true))
            .Register<WGAdminObserverHotkeySystem>();
    }

    /// <summary>Shows the admin observer to players, or hides it again.</summary>
    public void ToggleManifest()
    {
        var command = _manifest.LocalManifested() ? UnmanifestCommand : ManifestCommand;
        if (_admin.CanCommand(command))
            _console.ExecuteCommand(command);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<WGAdminObserverHotkeySystem>();
    }

    /// <summary>Runs <c>aghost</c> for an admin, unless it would only swap one bodiless observer for another.</summary>
    public void ToggleAghost()
    {
        if (!_admin.CanCommand(AghostCommand))
            return;

        if (HasNoBodyToReturnTo())
        {
            _popup.PopupCursor(Loc.GetString("wg-observer-hotkey-no-body"));
            return;
        }

        _console.ExecuteCommand(AghostCommand);
    }

    /// <summary>Whether the local player is an admin observer that is not visiting from a body.</summary>
    public bool HasNoBodyToReturnTo()
    {
        return _player.LocalEntity is { } entity
               && TryComp<GhostComponent>(entity, out var ghost)
               && ghost.CanGhostInteract
               && !ghost.CanReturnToBody;
    }
}
