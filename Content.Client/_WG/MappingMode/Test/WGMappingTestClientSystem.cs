using Content.Client.Gameplay;
using Content.Shared._WG.MappingMode;
using Content.Shared._WG.MappingMode.Test;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input.Binding;
using Robust.Shared.Timing;

namespace Content.Client._WG.MappingMode.Test;

/// <summary>
/// Live testing on the client: knows when the local player walks a test (their entity carries
/// <see cref="WGMappingTesterComponent"/>), keeps the test banner over whatever gameplay screen is up, binds the
/// return key while testing, and sends the start and stop requests.
/// </summary>
public sealed partial class WGMappingTestClientSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IStateManager _state = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private WGMappingClientSystem _mapping = default!;

    private WGMappingTestBanner? _banner;
    private bool _keyBound;

    /// <summary>Whether the local player is in a live test.</summary>
    public bool Testing => _player.LocalEntity is { } local && TryComp<WGMappingTesterComponent>(local, out _);

    /// <summary>The test running on the player's session, if any.</summary>
    public WGMappingTestInfo? Running => _mapping.Session?.Test;

    public override void Shutdown()
    {
        base.Shutdown();
        Unmount();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_player.LocalEntity is not { } local ||
            !TryComp<WGMappingTesterComponent>(local, out var tester) ||
            _state.CurrentState is not GameplayState ||
            _ui.ActiveScreen is not { } screen)
        {
            Unmount();
            return;
        }

        if (_banner == null)
            Mount();

        if (_banner!.Parent != screen)
        {
            _banner.Orphan();
            screen.AddChild(_banner);
            LayoutContainer.SetAnchorPreset(_banner, LayoutContainer.LayoutPreset.CenterTop);
        }

        var key = _input.GetKeyFunctionButtonString(WGMappingKeyFunctions.WGMappingTestReturn);
        _banner.Update(tester.MapName, tester.Ends - _timing.CurTime, string.IsNullOrEmpty(key) ? null : key);
    }

    /// <summary>Asks the server to start the session's test, or to join the one running.</summary>
    public void Start()
    {
        if (_mapping.Session != null)
            RaiseNetworkEvent(new WGMappingTestStartRequest());
    }

    /// <summary>Goes back to the mapping observer; with <paramref name="all"/> the owner ends the test for everyone.</summary>
    public void Stop(bool all = false)
    {
        RaiseNetworkEvent(new WGMappingTestStopRequest(all));
    }

    private void Mount()
    {
        _banner = new WGMappingTestBanner();
        _banner.BackButton.OnPressed += _ => Stop();

        var common = _input.Contexts.GetContext("common");
        if (!common.FunctionExists(WGMappingKeyFunctions.WGMappingTestReturn))
        {
            common.AddFunction(WGMappingKeyFunctions.WGMappingTestReturn);
            _keyBound = true;
        }

        CommandBinds.Builder
            .Bind(WGMappingKeyFunctions.WGMappingTestReturn, InputCmdHandler.FromDelegate(_ => Stop()))
            .Register<WGMappingTestClientSystem>();
    }

    private void Unmount()
    {
        if (_banner == null)
            return;

        CommandBinds.Unregister<WGMappingTestClientSystem>();
        if (_keyBound)
        {
            var common = _input.Contexts.GetContext("common");
            if (common.FunctionExists(WGMappingKeyFunctions.WGMappingTestReturn))
                common.RemoveFunction(WGMappingKeyFunctions.WGMappingTestReturn);

            _keyBound = false;
        }

        _banner.Orphan();
        _banner = null;
    }
}
