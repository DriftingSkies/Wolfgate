using Content.Shared._WG.AdminVessels;
using Robust.Shared.Random;

namespace Content.Server.Anomaly;

public sealed partial class AnomalySystem
{
    /// <summary>Spawns a generated anomaly on a test chamber marker of the grid, if it has one. False when none.</summary>
    public bool TrySpawnInChamber(EntityUid grid, string toSpawn)
    {
        var chambers = new List<EntityUid>();
        var query = EntityQueryEnumerator<WGAnomalyChamberComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid)
                chambers.Add(uid);
        }

        if (chambers.Count == 0)
            return false;

        Spawn(toSpawn, Transform(Random.Pick(chambers)).Coordinates);
        return true;
    }
}
