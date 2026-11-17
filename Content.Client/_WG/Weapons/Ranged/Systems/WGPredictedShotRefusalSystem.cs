using Content.Shared._RMC14.Weapons.Ranged.Prediction;
using Content.Shared._WG.Weapons.Ranged.Events;

namespace Content.Client._WG.Weapons.Ranged.Systems;

/// <summary>
/// Drops predicted projectiles the server says it never fired, so the shooter sees no phantom rounds.
/// </summary>
public sealed class WGPredictedShotRefusalSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGPredictedShotsRefusedEvent>(OnRefused);
    }

    private void OnRefused(WGPredictedShotsRefusedEvent ev)
    {
        foreach (var id in ev.ClientIds)
        {
            var uid = new EntityUid(id);
            if (IsClientSide(uid) && HasComp<PredictedProjectileClientComponent>(uid))
                QueueDel(uid);
        }
    }
}
