using Content.Client.Gameplay;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingMode.Test;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input.Binding;
using Robust.Shared.Timing;

namespace Content.Client._WF.MappingMode.Test;

/// <summary>
/// Live testing on the client: knows when the local player walks a test (their entity carries
/// <see cref="WFMappingTesterComponent"/>), keeps the test banner over whatever gameplay screen is up, binds the
/// return key while testing, and sends the start and stop requests.
/// </summary>
public sealed class WFMappingTestClientSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IStateManager _state = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private WFMappingClientSystem _mapping = default!;

    private WFMappingTestBanner? _banner;
    private bool _keyBound;

    /// <summary>Whether the local player is in a live test.</summary>
    public bool Testing => _player.LocalEntity is { } local && TryComp<WFMappingTesterComponent>(local, out _);

    /// <summary>The test running on the player's session, if any.</summary>
    public WFMappingTestInfo? Running => _mapping.Session?.Test;

    public override void Shutdown()
    {
        base.Shutdown();
        Unmount();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_player.LocalEntity is not { } local ||
            !TryComp<WFMappingTesterComponent>(local, out var tester) ||
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

        var key = _input.GetKeyFunctionButtonString(WFMappingKeyFunctions.WFMappingTestReturn);
        _banner.Update(tester.MapName, tester.Ends - _timing.CurTime, string.IsNullOrEmpty(key) ? null : key);
    }

    /// <summary>Asks the server to start the session's test, or to join the one running.</summary>
    public void Start()
    {
        if (_mapping.Session != null)
            RaiseNetworkEvent(new WFMappingTestStartRequest());
    }

    /// <summary>Goes back to the mapping observer; with <paramref name="all"/> the owner ends the test for everyone.</summary>
    public void Stop(bool all = false)
    {
        RaiseNetworkEvent(new WFMappingTestStopRequest(all));
    }

    private void Mount()
    {
        _banner = new WFMappingTestBanner();
        _banner.BackButton.OnPressed += _ => Stop();

        var common = _input.Contexts.GetContext("common");
        if (!common.FunctionExists(WFMappingKeyFunctions.WFMappingTestReturn))
        {
            common.AddFunction(WFMappingKeyFunctions.WFMappingTestReturn);
            _keyBound = true;
        }

        CommandBinds.Builder
            .Bind(WFMappingKeyFunctions.WFMappingTestReturn, InputCmdHandler.FromDelegate(_ => Stop()))
            .Register<WFMappingTestClientSystem>();
    }

    private void Unmount()
    {
        if (_banner == null)
            return;

        CommandBinds.Unregister<WFMappingTestClientSystem>();
        if (_keyBound)
        {
            var common = _input.Contexts.GetContext("common");
            if (common.FunctionExists(WFMappingKeyFunctions.WFMappingTestReturn))
                common.RemoveFunction(WFMappingKeyFunctions.WFMappingTestReturn);

            _keyBound = false;
        }

        _banner.Orphan();
        _banner = null;
    }
}
