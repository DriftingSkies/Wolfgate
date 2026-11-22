using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WG.Performance;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.ContentPack;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Utility;

namespace Content.Server._WG.RoundSetup;

/// <summary>
/// A crew's hull file is read and parsed off the main thread as soon as its leader picks the ship, so the launch only
/// pays for the entities. A launch before the parse is done loads the file as it always did.
/// </summary>
public sealed partial class WGShipScenarioSystem
{
    [Dependency] private IResourceManager _resources = default!;

    /// <summary>Most hull files kept parsed ahead of a launch; picking more starts over.</summary>
    private const int MaxPrepared = 8;

    /// <summary>Hull files being parsed, or parsed, ahead of a launch, by path. Each is used once.</summary>
    private readonly Dictionary<ResPath, Task<MappingDataNode?>> _prepared = new();

    /// <summary>Starts parsing a vessel's hull file for the launch to come.</summary>
    public void PrepareHull(string? vesselId)
    {
        if (vesselId == null || !_prototypes.TryIndex<VesselPrototype>(vesselId, out var vessel)
            || _prepared.ContainsKey(vessel.ShuttlePath))
            return;

        if (_prepared.Count >= MaxPrepared)
            _prepared.Clear();

        _prepared[vessel.ShuttlePath] = WGGridFileReader.ReadAsync(_resources, vessel.ShuttlePath);
    }

    /// <summary>Loads a hull from its parse if that is done, otherwise from its file as before.</summary>
    private bool LoadHull(MapId map, ResPath path, Vector2 offset, [NotNullWhen(true)] out Entity<MapGridComponent>? grid)
    {
        // The loader may change the data it is given, so a parse is used for one launch only.
        if (_prepared.Remove(path, out var parsing) && parsing.IsCompletedSuccessfully
            && parsing.GetAwaiter().GetResult() is { } data)
            return WGGridFileReader.TryLoad(_loader, data, path, map, out grid, offset);

        return _loader.TryLoadGrid(map, path, out grid, offset: offset);
    }
}
