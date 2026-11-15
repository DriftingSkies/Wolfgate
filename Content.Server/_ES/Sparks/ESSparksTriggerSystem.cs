using Content.Server.Explosion.EntitySystems;
using Content.Shared._ES.Sparks;
using Content.Shared._ES.Sparks.Components;

// WOLFGATE(InteractionParticles): sparks when triggered, ported from Starlight's Visual Overdose
namespace Content.Server._ES.Sparks;

/// <summary>
/// Sparks hooked to <see cref="TriggerEvent"/>, which only exists on the server.
/// </summary>
/// <remarks>
/// Starlight subscribes this in the shared sparks system; this content's trigger system lives in Content.Server, so the
/// subscription lives here instead.
/// </remarks>
public sealed class ESSparksTriggerSystem : EntitySystem
{
    [Dependency] private ESSparksSystem _sparks = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ESSparkOnTriggerComponent, TriggerEvent>(OnTrigger);
    }

    private void OnTrigger(Entity<ESSparkOnTriggerComponent> ent, ref TriggerEvent args)
    {
        _sparks.DoSparks(ent);
    }
}
