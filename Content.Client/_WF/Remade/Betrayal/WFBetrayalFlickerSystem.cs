using System.Numerics;
using Content.Shared._WF.Remade.Dread;
using Content.Shared._WF.Remade.Betrayal;
using Content.Shared.CCVar;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.Remade.Betrayal;

/// <summary>
/// For a split second another player looks like a Remade, to this player alone. The player's own sprite is hidden and a Remade
/// sprite stands in its place, following it, for 0.2 to 0.4 s. It exists on this client only. Reduced motion and
/// <c>wf.remade.dread.visuals</c> at 0 turn it off.
/// </summary>
public sealed partial class WFBetrayalFlickerSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;

    private const string Prototype = "WFRemadeGlimpse";

    private readonly record struct Live(EntityUid Target, EntityUid Stand, bool WasVisible, TimeSpan End);

    private readonly List<Live> _live = new();

    /// <summary>How many flickers this client has shown, and how many are showing. Tests read them.</summary>
    public int Shown { get; private set; }

    public int Showing => _live.Count;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFBetrayalFlickerEvent>(OnFlicker);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(_ => Clear());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Clear();
    }

    private bool Allowed()
    {
        return _config.GetCVar(WFDreadCVars.Visuals) > 0f && !_config.GetCVar(CCVars.ReducedMotion);
    }

    private void Clear()
    {
        foreach (var live in _live)
        {
            End(live);
        }

        _live.Clear();
    }

    private void End(Live live)
    {
        if (!TerminatingOrDeleted(live.Stand))
            Del(live.Stand);

        if (!TerminatingOrDeleted(live.Target) && TryComp<SpriteComponent>(live.Target, out var sprite))
            sprite.Visible = live.WasVisible;
    }

    private void OnFlicker(WFBetrayalFlickerEvent ev)
    {
        if (!Allowed() || ev.Sprite < 0 || ev.Sprite >= WFDreadGlimpses.All.Length || !TryGetEntity(ev.Target, out var target)
            || TerminatingOrDeleted(target) || target == _player.LocalEntity || !TryComp<SpriteComponent>(target, out var real)
            || _live.Exists(live => live.Target == target))
        {
            return;
        }

        var glimpse = WFDreadGlimpses.All[ev.Sprite];
        var stand = Spawn(Prototype, new EntityCoordinates(target.Value, Vector2.Zero));
        var sprite = Comp<SpriteComponent>(stand);
        sprite.LayerSetRSI(0, new ResPath(glimpse.Rsi));
        sprite.LayerSetState(0, WFDreadGlimpses.State);
        sprite.Offset = new Vector2(0f, WFDreadGlimpses.Offset(glimpse.Height));

        var wasVisible = real.Visible;
        real.Visible = false;
        _live.Add(new Live(target.Value, stand, wasVisible, _timing.CurTime + TimeSpan.FromSeconds(Math.Clamp(ev.Seconds, 0.1f, 0.6f))));
        Shown++;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_live.Count == 0)
            return;

        var now = _timing.CurTime;
        for (var i = _live.Count - 1; i >= 0; i--)
        {
            var live = _live[i];
            if (now < live.End && !TerminatingOrDeleted(live.Target) && !TerminatingOrDeleted(live.Stand) && Allowed())
                continue;

            End(live);
            _live.RemoveAt(i);
        }
    }
}
