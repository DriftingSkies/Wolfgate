using Content.Server._WF.Planets;

namespace Content.Server.Parallax;

public sealed partial class BiomeSystem
{
    /// <summary>Adds the chunks round every <see cref="WFTerrainLoaderComponent"/> to this pass's load.</summary>
    private void WfProcessLoaders()
    {
        var loaders = EntityQueryEnumerator<WFTerrainLoaderComponent, TransformComponent>();
        while (loaders.MoveNext(out var uid, out _, out var xform))
        {
            if (!_handledEntities.Add(uid) || !_biomeQuery.TryGetComponent(xform.MapUid, out var biome) || !biome.Enabled)
                continue;

            var worldPos = _transform.GetWorldPosition(xform);
            WfNoteBodyLoader(biome, worldPos);
            AddChunksInRange(biome, worldPos);
        }
    }
}
