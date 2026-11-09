using Content.Server._WF.Warheads.Components;
using Content.Shared._WF.Warheads;
using Content.Shared.DeviceLinking;
using Content.Shared.DoAfter;

namespace Content.Server._WF.Warheads;

public sealed partial class WFWarheadSystem
{
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    /// <summary>Longest timer the panel can set, in seconds.</summary>
    public const float MaxTimer = 3600f;

    private void InitializeUi()
    {
        SubscribeLocalEvent<WFWarheadComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<WFWarheadComponent, WFWarheadSetTimerMessage>(OnSetTimerMessage);
        SubscribeLocalEvent<WFWarheadComponent, WFWarheadArmMessage>(OnArmMessage);
        SubscribeLocalEvent<WFWarheadComponent, WFWarheadTurnKeyMessage>(OnTurnKeyMessage);
        SubscribeLocalEvent<WFWarheadComponent, WFWarheadSafetyMessage>(OnSafetyMessage);
    }

    private void OnUiOpened(Entity<WFWarheadComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnSetTimerMessage(Entity<WFWarheadComponent> ent, ref WFWarheadSetTimerMessage args)
    {
        if (!HasComp<WFTimerFuzeComponent>(ent) || ent.Comp.State != WFWarheadState.Safe || !float.IsFinite(args.Seconds))
            return;

        ent.Comp.Timer = Math.Clamp(args.Seconds, ent.Comp.FinalPhase, MaxTimer);
        Dirty(ent);
        UpdateUi(ent);
    }

    private void OnArmMessage(Entity<WFWarheadComponent> ent, ref WFWarheadArmMessage args)
    {
        if (!TryComp<WFTimerFuzeComponent>(ent, out var fuze) || ent.Comp.State != WFWarheadState.Safe
            || !KeyIn(ent, args.Actor))
            return;

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.Actor, fuze.ArmDelay,
            new WFWarheadArmDoAfterEvent(), ent, target: ent)
        {
            BreakOnMove = true,
            NeedHand = true,
        });
    }

    private void OnTurnKeyMessage(Entity<WFWarheadComponent> ent, ref WFWarheadTurnKeyMessage args)
    {
        if (!TryComp<WFTwoPersonArmingComponent>(ent, out var keys)
            || ent.Comp.State is not (WFWarheadState.Safe or WFWarheadState.Armed)
            || !KeyIn(ent, args.Actor))
            return;

        TurnKey((ent, keys), ent, args.Actor);
    }

    private void OnSafetyMessage(Entity<WFWarheadComponent> ent, ref WFWarheadSafetyMessage args)
    {
        if (!TryComp<WFRemoteFuzeComponent>(ent, out var remote))
            return;

        var wanted = args.Safe ? WFWarheadState.Armed : WFWarheadState.Safe;
        if (ent.Comp.State != wanted || !args.Safe && !KeyIn(ent, args.Actor))
            return;

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.Actor, remote.SafetyDelay,
            new WFWarheadSafetyDoAfterEvent(), ent, target: ent)
        {
            BreakOnMove = true,
            NeedHand = true,
        });
    }

    /// <summary>Sends the control panel its current state, if anyone has it open.</summary>
    public void UpdateUi(EntityUid uid, WFWarheadComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false) || !_ui.HasUi(uid, WFWarheadUiKey.Key))
            return;

        var payload = GetPayload(comp);
        var state = new WFWarheadBoundUserInterfaceState
        {
            State = comp.State,
            DetonateAt = comp.DetonateAt,
            Timer = comp.Timer,
            FinalPhase = comp.FinalPhase,
            YieldClass = comp.YieldClass,
            DisarmQuality = comp.DisarmQuality,
        };

        if (TryComp<WFTimerFuzeComponent>(uid, out var timer))
        {
            state.HasTimerFuze = true;
            state.TimerOptions = new List<float>(timer.Options);
        }

        if (TryComp<WFTwoPersonArmingComponent>(uid, out var keys))
        {
            state.HasTwoPerson = true;
            state.KeyWindow = keys.Window;
            if (keys.FirstKey is { } first
                && !TerminatingOrDeleted(first)
                && _timing.CurTime - keys.FirstKeyTime <= TimeSpan.FromSeconds(keys.Window))
            {
                state.FirstKeyHolder = Name(first);
                state.FirstKeyTime = keys.FirstKeyTime;
            }
        }

        if (TryComp<WFRemoteFuzeComponent>(uid, out var remote))
        {
            state.HasRemote = true;
            state.SignalTimer = remote.SignalTimer;
            state.RemoteLinks = TryComp<DeviceLinkSinkComponent>(uid, out var sink) ? sink.LinkedSources.Count : 0;
        }

        state.HasImpact = HasComp<WFImpactFuzeComponent>(uid);

        if (payload != null)
        {
            state.BlastRadius = EstimateBlastRadius(payload);
            state.FlashRange = payload.FlashRange;
            state.EmpRange = payload.EmpRange;
            state.FalloutRadius = payload.FalloutIntensity > 0f ? payload.FalloutSpread + payload.FalloutRadius : 0f;
            state.FalloutSources = payload.FalloutIntensity > 0f ? payload.FalloutCount + 1 : 0;
            state.DeletesGrid = payload.DeleteGrid;
        }

        _ui.SetUiState(uid, WFWarheadUiKey.Key, state);
    }

    /// <summary>
    /// Rough blast radius in tiles: the radius of a cone of the payload's total intensity whose height falls by the
    /// slope per tile, flattened where it would pass the per-tile cap.
    /// </summary>
    public static float EstimateBlastRadius(WFWarheadPayload payload)
    {
        if (payload.ExplosionType == null || payload.TotalIntensity <= 0f || payload.IntensitySlope <= 0f)
            return 0f;

        var cone = MathF.Cbrt(3f * payload.TotalIntensity / (MathF.PI * payload.IntensitySlope));
        if (payload.MaxIntensity <= 0f || cone * payload.IntensitySlope <= payload.MaxIntensity)
            return cone;

        // Capped: a flat disc at the cap with a cone rim of width cap / slope.
        var rim = payload.MaxIntensity / payload.IntensitySlope;
        var disc = MathF.Sqrt(Math.Max(payload.TotalIntensity / (MathF.PI * payload.MaxIntensity), 0f));
        return disc + rim / 2f;
    }
}
