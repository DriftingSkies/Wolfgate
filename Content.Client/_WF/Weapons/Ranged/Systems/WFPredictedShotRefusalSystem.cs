using Content.Shared._RMC14.Weapons.Ranged.Prediction;
using Content.Shared._WF.Weapons.Ranged.Events;

namespace Content.Client._WF.Weapons.Ranged.Systems;

/// <summary>
/// Drops predicted projectiles the server says it never fired, so the shooter sees no phantom rounds.
/// </summary>
public sealed class WFPredictedShotRefusalSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFPredictedShotsRefusedEvent>(OnRefused);
    }

    private void OnRefused(WFPredictedShotsRefusedEvent ev)
    {
        foreach (var id in ev.ClientIds)
        {
            var uid = new EntityUid(id);
            if (IsClientSide(uid) && HasComp<PredictedProjectileClientComponent>(uid))
                QueueDel(uid);
        }
    }
}
