using Content.Shared._Rat.Overwatch;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Silicons.StationAi;

namespace Content.Server._WG.Cyborgs;

/// <summary>
/// Lets an AI view camera feeds through an overwatch console. An AI moves its remote eye, not its brain, so this
/// stops the feed when the eye moves and hands the view back to the eye afterwards. Any watcher sees a feed at the
/// default zoom limit, so an AI's wider view doesn't carry over to it.
/// </summary>
public sealed class WGAiOverwatchSystem : EntitySystem
{
    [Dependency] private SharedContentEyeSystem _contentEye = default!;
    [Dependency] private SharedStationAiSystem _stationAi = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OverwatchConsoleComponent, BoundUserInterfaceMessageAttempt>(OnMessageAttempt);
        SubscribeLocalEvent<WGAiOverwatchEyeComponent, MoveInputEvent>(OnEyeMoveInput);
    }

    /// <summary>
    /// Lowers a watcher's zoom limit to the default while it views a feed, keeping its own limit to restore.
    /// </summary>
    public void LimitZoom(EntityUid watcher)
    {
        if (HasComp<WGOverwatchZoomLimitComponent>(watcher)
            || !TryComp<ContentEyeComponent>(watcher, out var eye)
            || eye.MaxZoom is { X: <= 1f, Y: <= 1f })
            return;

        EnsureComp<WGOverwatchZoomLimitComponent>(watcher).MaxZoom = eye.MaxZoom;
        _contentEye.SetMaxZoom(watcher, SharedContentEyeSystem.DefaultZoom, eye);
    }

    /// <summary>
    /// Ends a watcher's feed: restores its zoom limit and returns the entity its eye goes back to, the AI's remote eye
    /// or null for anyone else.
    /// </summary>
    public EntityUid? ReleaseTarget(EntityUid watcher)
    {
        if (!TerminatingOrDeleted(watcher) && TryComp<WGOverwatchZoomLimitComponent>(watcher, out var limit))
        {
            _contentEye.SetMaxZoom(watcher, limit.MaxZoom);
            RemComp(watcher, limit);
        }

        return RemoteEye(watcher);
    }

    /// <summary>
    /// The AI's remote eye, or null if the watcher isn't an AI with one.
    /// </summary>
    private EntityUid? RemoteEye(EntityUid watcher)
    {
        if (TerminatingOrDeleted(watcher)
            || !HasComp<StationAiHeldComponent>(watcher)
            || !_stationAi.TryGetCore(watcher, out var core)
            || core.Comp?.RemoteEntity is not { } eye
            || TerminatingOrDeleted(eye))
            return null;

        return eye;
    }

    private void OnMessageAttempt(EntityUid uid, OverwatchConsoleComponent component, BoundUserInterfaceMessageAttempt args)
    {
        if (args.Message is not OverwatchViewCameraMessage || RemoteEye(args.Actor) is not { } eye)
            return;

        EnsureComp<WGAiOverwatchEyeComponent>(eye).Ai = args.Actor;
    }

    private void OnEyeMoveInput(Entity<WGAiOverwatchEyeComponent> ent, ref MoveInputEvent args)
    {
        if (!args.HasDirectionalMovement)
            return;

        RemCompDeferred<WGAiOverwatchEyeComponent>(ent);

        if (TryComp<RatOverwatchWatchingComponent>(ent.Comp.Ai, out var watching) && watching.Watching != null)
            RemComp<RatOverwatchWatchingComponent>(ent.Comp.Ai);
    }
}
