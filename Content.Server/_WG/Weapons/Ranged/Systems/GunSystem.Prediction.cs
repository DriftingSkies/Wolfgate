using Content.Shared._RMC14.Weapons.Ranged.Prediction;

namespace Content.Server.Weapons.Ranged.Systems;

/// <summary>
/// Marks projectiles the shooter's client predicted.
/// </summary>
public sealed partial class GunSystem
{
    /// <summary>
    /// Links a fired projectile to the copy the shooter's client predicted for the same slot, so the client hides
    /// the server's copy and its hit reports resolve.
    /// </summary>
    private void MarkPredicted(EntityUid projectile, EntityUid gunUid)
    {
        if (PredictedShotContext is not { } shot || shot.Gun != gunUid)
            return;

        var slot = shot.Next++;
        if (!GunPrediction || shot.ClientIds is not { } ids || slot >= ids.Count || ids[slot] == 0)
            return;

        // Built with its fields set: adding it raises MapInit, which registers it by shooter and client id.
        var comp = new PredictedProjectileServerComponent
        {
            Shooter = shot.Shooter,
            ClientId = ids[slot],
            ClientEnt = shot.Shooter.AttachedEntity,
        };
        AddComp(projectile, comp, true);
        Dirty(projectile, comp);
    }
}
