using System.Numerics;
using Content.Client.Gameplay;
using Content.Shared._WF.RoundSetup;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._WF.RoundSetup;

/// <summary>Shows the server's crash deadline only to the player currently aboard that ship.</summary>
public sealed partial class WFCrashCountdownHudSystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IStateManager _states = default!;
    [Dependency] private IClientNetManager _net = default!;

    private WFCrashCountdownHud _hud = default!;
    private float _untilRefresh;

    /// <summary>The synchronized game-time deadline supplied by the server.</summary>
    public TimeSpan? DescendAt { get; private set; }

    /// <summary>The body that this personal warning belongs to.</summary>
    public NetEntity? Body { get; private set; }

    /// <summary>Whether the current player can see the warning banner.</summary>
    public bool HudVisible => _hud.Visible;

    /// <summary>The whole seconds visible on the player's countdown.</summary>
    public int RemainingSeconds => _hud.Visible ? _hud.RemainingSeconds : 0;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        UpdatesOutsidePrediction = true;
        _hud = new WFCrashCountdownHud(_resources) { Visible = false };
        _ui.PopupRoot.AddChild(_hud);
        LayoutContainer.SetAnchorPreset(_hud, LayoutContainer.LayoutPreset.TopLeft);
        SubscribeNetworkEvent<WFCrashCountdownEvent>(OnCountdown);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRestart);
        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnDetached);
        _net.Disconnect += OnDisconnect;
    }

    /// <inheritdoc/>
    public override void Shutdown()
    {
        _net.Disconnect -= OnDisconnect;
        _hud.Orphan();
        base.Shutdown();
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _untilRefresh -= frameTime;
        if (_untilRefresh > 0)
            return;
        _untilRefresh = 0.1f;
        RefreshHud();
    }

    private void OnCountdown(WFCrashCountdownEvent args)
    {
        DescendAt = args.DescendAt;
        Body = args.Body;
        RefreshHud();
    }

    private void OnAttached(LocalPlayerAttachedEvent args) => RefreshHud();

    private void OnDetached(LocalPlayerDetachedEvent args) => _hud.Visible = false;

    private void OnRestart(RoundRestartCleanupEvent args) => Clear();

    private void OnDisconnect(object? sender, NetDisconnectedArgs args) => Clear();

    private void Clear()
    {
        DescendAt = null;
        Body = null;
        _hud.Visible = false;
    }

    /// <summary>Uses synchronized time, and never shows a warning intended for a previous body.</summary>
    private void RefreshHud()
    {
        if (DescendAt is not { } deadline || deadline <= _timing.CurTime ||
            _states.CurrentState is not GameplayState || _players.LocalEntity is not { } body ||
            TerminatingOrDeleted(body) || GetNetEntity(body) != Body || HasComp<GhostComponent>(body) ||
            TryComp<MobStateComponent>(body, out var state) && state.CurrentState == MobState.Dead)
        {
            _hud.Visible = false;
            return;
        }
        var seconds = (int) Math.Ceiling((deadline - _timing.CurTime).TotalSeconds);
        _hud.SetRemaining(seconds);
        var width = MathF.Min(WFCrashCountdownHud.PanelWidth, MathF.Max(0, _ui.PopupRoot.Width - 24));
        _hud.SetWidth = width;
        LayoutContainer.SetPosition(_hud, new Vector2(MathF.Max(0, (_ui.PopupRoot.Width - width) / 2), 120));
        _hud.Visible = true;
    }
}
