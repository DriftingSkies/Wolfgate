using System.Linq;
using Content.Server.Worldgen.Components;
using Content.Server.Worldgen.Components.Debris;
using Content.Server.Worldgen.Prototypes;
using Content.Shared._WF.DroneRings;
using Content.Shared.Storage;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.DroneRings;

/// <summary>Works out where a map's world generation starts to spawn drones and tells clients, for the radar rings.</summary>
public sealed partial class WFDroneRingSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BiomeSelectionComponent, ComponentInit>(OnBiomesInit);
    }

    private void OnBiomesInit(EntityUid uid, BiomeSelectionComponent component, ComponentInit args)
    {
        var radii = Rings(component.Biomes);
        if (radii.Count == 0)
            return;

        var rings = EnsureComp<WFDroneRingsComponent>(uid);
        rings.Radii = radii;
        Dirty(uid, rings);
    }

    /// <summary>
    /// The distances from the centre at which the drones on offer change, going outward. Follows biome selection: at
    /// each distance the highest-priority biome in range wins, and one limited by noise shares the band with the next
    /// below it. A biome pinned to a block of chunks is a local patch and draws no ring.
    /// </summary>
    public List<float> Rings(IEnumerable<string> biomeIds)
    {
        var sources = new HashSet<string>();
        foreach (var list in _proto.EnumeratePrototypes<WFDroneSourcesPrototype>())
        {
            foreach (var spawner in list.Spawners)
            {
                sources.Add(spawner);
            }
        }

        var biomes = new List<(BiomePrototype Biome, HashSet<string> Drones)>();
        var edges = new SortedSet<float> { 0f };
        foreach (var id in biomeIds)
        {
            if (!_proto.TryIndex<BiomePrototype>(id, out var biome)
                || biome.MinX != null && biome.MaxX != null && biome.MinY != null && biome.MaxY != null)
                continue;

            biomes.Add((biome, Drones(biome, sources)));
            if (biome.DistanceRange is not { } range)
                continue;

            edges.Add(range.X);
            edges.Add(range.Y);
        }

        // A stable sort, so equal priorities keep their listed order as biome selection does.
        var ordered = biomes.OrderByDescending(entry => entry.Biome.Priority).ToList();
        var bounds = edges.ToList();
        var radii = new List<float>();
        var previous = new HashSet<string>();
        for (var i = 0; i < bounds.Count; i++)
        {
            var sample = i + 1 < bounds.Count ? (bounds[i] + bounds[i + 1]) / 2f : bounds[i] + 1f;
            var drones = DronesAt(ordered, sample);
            if (drones.Count > 0 && bounds[i] > 0f && !drones.SetEquals(previous))
                radii.Add(bounds[i]);

            previous = drones;
        }

        return radii;
    }

    /// <summary>The drone spawners a chunk at this distance can roll.</summary>
    private static HashSet<string> DronesAt(List<(BiomePrototype Biome, HashSet<string> Drones)> ordered, float distance)
    {
        var drones = new HashSet<string>();
        foreach (var (biome, own) in ordered)
        {
            if (biome.DistanceRange is { } range && (distance < range.X || distance > range.Y))
                continue;

            drones.UnionWith(own);
            if (biome.NoiseRanges.Count == 0)
                break;
        }

        return drones;
    }

    /// <summary>The drone spawners in a biome's debris tables.</summary>
    private static HashSet<string> Drones(BiomePrototype biome, HashSet<string> sources)
    {
        var drones = new HashSet<string>();
        foreach (var entry in biome.ChunkComponents.Values)
        {
            IReadOnlyList<EntitySpawnEntry>? table = entry.Component switch
            {
                NoiseDrivenDebrisSelectorComponent noise => noise.WfEntries,
                SimpleDebrisSelectorComponent simple => simple.WfEntries,
                _ => null,
            };
            if (table == null)
                continue;

            foreach (var spawn in table)
            {
                if (spawn.PrototypeId is { } id && sources.Contains(id))
                    drones.Add(id);
            }
        }

        return drones;
    }
}
