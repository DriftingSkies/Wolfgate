using System.Collections.Generic;
using System.Linq;
using Content.Server._NF.StationEvents.Components;
using Content.Shared._Crescent.Hardpoints;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Weapons;

/// <summary>Checks the grids bluespace events spawn, loaded into an initialised map.</summary>
[TestFixture]
public sealed class EventGridHardpointTest
{
    /// <summary>Every anchored hardpoint-only gun on a bluespace event grid mounts on a hardpoint, so gunnery can link it.</summary>
    [Test]
    public async Task BluespaceGridGunsMount()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var loader = entities.System<MapLoaderSystem>();
        var maps = entities.System<SharedMapSystem>();
        var failures = new List<string>();
        var guns = 0;

        var paths = new HashSet<ResPath>();
        foreach (var proto in prototypes.EnumeratePrototypes<EntityPrototype>())
        {
            if (proto.Abstract || !proto.TryGetComponent<BluespaceErrorRuleComponent>(out var rule, factory))
                continue;

            foreach (var group in rule.Groups.Values.OfType<BluespaceGridSpawnGroup>())
                paths.UnionWith(group.Paths);
        }

        await server.WaitPost(() =>
        {
            foreach (var path in paths)
            {
                maps.CreateMap(out var mapId);
                if (!loader.TryLoadGrid(mapId, path, out var grid))
                {
                    failures.Add($"{path} did not load.");
                    maps.DeleteMap(mapId);
                    continue;
                }

                var query = entities.EntityQueryEnumerator<HardpointAnchorableOnlyComponent, TransformComponent>();
                while (query.MoveNext(out var gun, out var mount, out var xform))
                {
                    // Loose spares in a hold are meant to be mounted by the crew.
                    if (xform.GridUid != grid.Value.Owner || !xform.Anchored)
                        continue;
                    guns++;
                    if (mount.anchoredTo == null)
                        failures.Add($"{path}: {entities.ToPrettyString(gun)} at {xform.LocalPosition} has no compatible hardpoint on its tile.");
                }

                maps.DeleteMap(mapId);
            }
        });

        Assert.That(paths, Is.Not.Empty, "No bluespace event grids were found.");
        Assert.That(guns, Is.Positive, "No anchored hardpoint-only guns were found on any bluespace event grid.");
        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        await pair.CleanReturnAsync();
    }
}
