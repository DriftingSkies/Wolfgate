using Content.Shared._Rat.Overwatch;
using Content.Shared.Movement.Events;
using Content.Shared.Silicons.StationAi;

namespace Content.Server._WG.Cyborgs;

/// <summary>
/// Lets an AI view camera feeds through an overwatch console. An AI moves its remote eye, not its brain, so this
/// stops the feed when the eye moves and hands the view back to the eye afterwards.
/// </summary>
public sealed class WGAiOverwatchSystem : EntitySystem
{
    [Dependency] private SharedStationAiSystem _stationAi = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OverwatchConsoleComponent, BoundUserInterfaceMessageAttempt>(OnMessageAttempt);
        SubscribeLocalEvent<WGAiOverwatchEyeComponent, MoveInputEvent>(OnEyeMoveInput);
    }

    /// <summary>
    /// The entity a watcher's eye returns to when it stops watching: the AI's remote eye, or null for anyone else.
    /// </summary>
    public EntityUid? ReleaseTarget(EntityUid watcher)
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
        if (args.Message is not OverwatchViewCameraMessage || ReleaseTarget(args.Actor) is not { } eye)
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
