#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Server._NF.Shipyard.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.IntegrationTests.Tests._WG.Traders;

/// <summary>Stands in for any component that fails to save: its data field collides with the component's own type key.</summary>
[RegisterComponent]
public sealed partial class WGUnsavableTestComponent : Component
{
    [DataField("type")]
    public int Broken = 1;
}

/// <summary>One component that won't save must not lose a sold ship, and no game component should be built to fail.</summary>
[TestFixture]
public sealed class UsedShipUnsavableTest
{
    [Test]
    public async Task BadComponentDoesNotLoseTheShip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var maps = entMan.System<SharedMapSystem>();
        var shipyard = entMan.System<ShipyardSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = maps.CreateGridEntity(map.MapId);
            maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, map.Tile.Tile);
            var item = entMan.SpawnEntity("Paper", new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));
            entMan.AddComponent<WGUnsavableTestComponent>(item).Broken = 2;

            // The engine logs the component that failed as an error, which is right live but fails a test.
            var failureLevel = pair.ServerLogHandler.FailureLevel;
            pair.ServerLogHandler.FailureLevel = null;
            var saved = shipyard.TrySaveShip(grid.Owner, out var data);
            pair.ServerLogHandler.FailureLevel = failureLevel;

            Assert.That(saved, Is.True, "The ship should still copy.");
            Assert.That(entMan.HasComponent<WGUnsavableTestComponent>(item), Is.False, "The bad component should have come off.");
            Assert.That(shipyard.TryAddSavedShip(data!, out var loaded), Is.True, "The copy should load.");
            var paperAboard = false;
            var aboard = entMan.GetComponent<TransformComponent>(loaded!.Value).ChildEnumerator;
            while (aboard.MoveNext(out var child))
            {
                paperAboard |= entMan.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID == "Paper";
            }

            Assert.That(paperAboard, Is.True, "The entity should come along without the bad component.");
            entMan.DeleteEntity(loaded.Value);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Finds saved fields that fail every save: one named "type", or a dictionary keyed on class objects.</summary>
    [Test]
    public async Task NoComponentIsBuiltToFailASave()
    {
        await using var pair = await PoolManager.GetServerClient();
        var factory = pair.Server.ResolveDependency<IComponentFactory>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var bad = new List<string>();

        foreach (var name in factory.AllRegisteredTypes.Select(t => factory.GetRegistration(t)).Where(r => !r.Unsaved).Select(r => r.Type))
        {
            if (name.Assembly.GetName().Name!.Contains("IntegrationTests"))
                continue;

            var members = name.GetFields(flags).Cast<MemberInfo>().Concat(name.GetProperties(flags));
            foreach (var member in members)
            {
                if (member.GetCustomAttribute<DataFieldAttribute>() is not { } data)
                    continue;

                var tag = data.Tag ?? char.ToLowerInvariant(member.Name[0]) + member.Name[1..];
                if (tag == "type")
                    bad.Add($"{name.Name}.{member.Name}: saved as \"type\", the key the save uses for the component itself");

                var type = member is FieldInfo field ? field.FieldType : ((PropertyInfo) member).PropertyType;
                var dictionary = type.GetInterfaces().Append(type).FirstOrDefault(i => i.IsGenericType
                    && (i.GetGenericTypeDefinition() == typeof(IDictionary<,>) || i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
                if (dictionary?.GetGenericArguments()[0] is { IsClass: true } key && key != typeof(string) && key != typeof(Enum))
                {
                    bad.Add($"{name.Name}.{member.Name}: keyed on {key.Name}, which can't be a YAML key");
                }
            }
        }

        Assert.That(bad, Is.Empty, string.Join("\n", bad));
        await pair.CleanReturnAsync();
    }
}
