using Content.Shared.Emoting;
using Content.Shared.Hands;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Pointing;
using Content.Shared.Speech;
using Content.Shared.Throwing;

namespace Content.Shared._WG.MappingMode;

/// <summary>
/// Keeps mapping observers out of the world: no interaction, pickup, speech, emotes, throwing, attacks or pointing.
/// </summary>
public sealed class WGMappingObserverSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WGMappingObserverComponent, InteractionAttemptEvent>(OnInteractionAttempt);
        SubscribeLocalEvent<WGMappingObserverComponent, UseAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WGMappingObserverComponent, PickupAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WGMappingObserverComponent, DropAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WGMappingObserverComponent, EmoteAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WGMappingObserverComponent, SpeakAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WGMappingObserverComponent, ThrowAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WGMappingObserverComponent, AttackAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WGMappingObserverComponent, PointAttemptEvent>(OnAttempt);
    }

    private void OnInteractionAttempt(Entity<WGMappingObserverComponent> ent, ref InteractionAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnAttempt(EntityUid uid, WGMappingObserverComponent component, CancellableEntityEventArgs args)
    {
        args.Cancel();
    }
}
