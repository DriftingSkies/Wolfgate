#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Crushing;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared._WF.Vehicles.Turret;
using Content.Shared.Whitelist;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>
/// The sprite states and layers the vehicle code asks the client for exist in the RSI of the vehicle they are asked on. The
/// server does not load the Sprite component, so these read the YAML of the prototype and the meta.json of its RSI.
/// </summary>
[TestFixture]
public sealed class VehicleSpriteTest
{
    private static readonly ResPath TextureRoot = new("/Textures");

    /// <summary>The layer the frame damage visualizer fades in (VehicleFrameDamageLayers.DamagedFrame).</summary>
    private const string DamagedFrameLayer = "damaged_frame";

    /// <summary>
    /// Every part that can be fitted to a vehicle slot, and shows on the vehicle, has the state it asks for in the RSI of every
    /// vehicle it can be fitted to, and the slot's layer is one the vehicle's sprite has. A part that asks for a state the
    /// vehicle's RSI lacks makes the client log an error and draw the placeholder.
    /// </summary>
    [Test]
    public async Task EveryPartThatCanBeFittedHasItsStateInTheVehiclesRsi()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var factory = server.EntMan.ComponentFactory;
        var resources = server.ResolveDependency<IResourceManager>();
        var failures = new List<string>();
        var checkedPairs = 0;

        await server.WaitAssertion(() =>
        {
            var ids = ModulePrototypeIds(resources);
            var parts = new List<(EntityPrototype Proto, HardpointItemComponent Item, HardpointVisualComponent Visual)>();

            foreach (var proto in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || !ids.Contains(proto.ID) ||
                    !proto.TryGetComponent<HardpointItemComponent>(out var item, factory) ||
                    !proto.TryGetComponent<HardpointVisualComponent>(out var visual, factory) ||
                    string.IsNullOrWhiteSpace(visual.VehicleState))
                {
                    continue;
                }

                // A turret that draws itself as an overlay shows no state on the vehicle (VehicleHardpointVisualsSystem).
                if (proto.TryGetComponent<VehicleTurretComponent>(out var turret, factory) && turret.ShowOverlay)
                    continue;

                parts.Add((proto, item, visual));
            }

            foreach (var vehicle in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (vehicle.Abstract || !ids.Contains(vehicle.ID) ||
                    !vehicle.TryGetComponent<RMCVehicleComponent>(out _, factory) ||
                    !vehicle.TryGetComponent<HardpointSlotsComponent>(out var slots, factory))
                {
                    continue;
                }

                if (!TryGetRsi(protos, vehicle.ID, out var rsi))
                {
                    failures.Add($"{vehicle.ID}: has no sprite RSI.");
                    continue;
                }

                var states = RsiStates(resources, rsi);
                var layers = LayerKeys(protos, vehicle.ID);

                foreach (var slot in slots.Slots)
                {
                    if (string.IsNullOrWhiteSpace(slot.VisualLayer))
                        continue;

                    if (!layers.Contains(slot.VisualLayer))
                        failures.Add($"{vehicle.ID}: slot {slot.Id} draws on the layer {slot.VisualLayer}, which the sprite does not map.");

                    foreach (var (part, item, visual) in parts)
                    {
                        if (!CanBeFitted(slots, slot, part, item))
                            continue;

                        checkedPairs++;

                        foreach (var state in new[] { visual.VehicleState, visual.DamagedVehicleState })
                        {
                            if (!string.IsNullOrWhiteSpace(state) && !states.Contains(state))
                                failures.Add($"{vehicle.ID}: {part.ID} can be fitted to slot {slot.Id} but asks for the state {state}, which {rsi} does not have.");
                        }
                    }
                }
            }
        });

        Assert.That(checkedPairs, Is.GreaterThan(0), "No part and slot were paired at all.");
        Assert.That(failures, Is.Empty, string.Join("\n", failures));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A vehicle whose RSI has a damaged frame state maps the layer for it, or the frame damage visualizer finds nothing to
    /// fade in and the vehicle never looks damaged.
    /// </summary>
    [Test]
    public async Task EveryVehicleWithADamagedFrameStateMapsItsLayer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var factory = server.EntMan.ComponentFactory;
        var resources = server.ResolveDependency<IResourceManager>();
        var failures = new List<string>();
        var vehicles = 0;

        await server.WaitAssertion(() =>
        {
            var ids = ModulePrototypeIds(resources);

            foreach (var vehicle in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (vehicle.Abstract || !ids.Contains(vehicle.ID) || !vehicle.TryGetComponent<RMCVehicleComponent>(out _, factory))
                    continue;

                // The test chassis and the exit marker areas are vehicles in name only and have no sprite.
                if (!TryGetRsi(protos, vehicle.ID, out var rsi))
                    continue;

                vehicles++;

                if (RsiStates(resources, rsi).Contains(DamagedFrameLayer) && !LayerKeys(protos, vehicle.ID).Contains(DamagedFrameLayer))
                    failures.Add($"{vehicle.ID}: {rsi} has a {DamagedFrameLayer} state but the sprite maps no {DamagedFrameLayer} layer, so frame damage is never shown.");
            }
        });

        Assert.That(vehicles, Is.GreaterThanOrEqualTo(38), "The roster is missing vehicles.");
        Assert.That(failures, Is.Empty, string.Join("\n", failures));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A locker the module defines lists the weld and paper label layers its parent <c>ClosetBase</c> has, because a child's
    /// sprite layers replace its parent's and ClosetBase's visualizers still map both: a label would reserve a blank layer
    /// and ask for a state the interior RSI does not have, and a welded locker would show no seam.
    /// </summary>
    [Test]
    public async Task EveryLockerOfTheModuleKeepsTheWeldAndLabelLayers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var resources = server.ResolveDependency<IResourceManager>();
        var failures = new List<string>();
        var lockers = 0;

        await server.WaitAssertion(() =>
        {
            var ids = ModulePrototypeIds(resources);

            foreach (var proto in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || !ids.Contains(proto.ID) || !protos.EnumerateAllParents<EntityPrototype>(proto.ID).Any(p => p.id == "ClosetBase"))
                    continue;

                lockers++;
                var layers = LayerKeys(protos, proto.ID);

                foreach (var key in new[] { "enum.WeldableLayers.BaseWelded", "enum.PaperLabelVisuals.Layer" })
                {
                    if (!layers.Contains(key))
                        failures.Add($"{proto.ID}: its sprite layers do not map {key}, which ClosetBase's visualizers do.");
                }
            }
        });

        Assert.That(lockers, Is.GreaterThanOrEqualTo(10), "The module's lockers were not found.");
        Assert.That(failures, Is.Empty, string.Join("\n", failures));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The ids of the entity prototypes the module defines in Resources/Prototypes/_WF/Vehicles. The prototypes the tests make
    /// up in their own files, which have no sprite, are not among them.
    /// </summary>
    private static HashSet<string> ModulePrototypeIds(IResourceManager resources)
    {
        var ids = new HashSet<string>();

        foreach (var file in resources.ContentFindFiles(new ResPath("/Prototypes/_WF/Vehicles")).Where(f => f.Extension == "yml"))
        {
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                         resources.ContentFileReadAllText(file), @"(?m)^  id: (\S+)\s*$"))
            {
                ids.Add(match.Groups[1].Value);
            }
        }

        return ids;
    }

    /// <summary>The checks of <c>HardpointSystem.IsValidHardpoint</c>, made on prototypes.</summary>
    private static bool CanBeFitted(HardpointSlotsComponent slots, HardpointSlot slot, EntityPrototype part, HardpointItemComponent item)
    {
        if (slots.VehicleFamily is { } vehicleFamily && item.VehicleFamily != vehicleFamily)
            return false;

        if (slot.SlotType is { } slotType && item.SlotType != slotType)
            return false;

        if (item.CompatibilityId != slot.CompatibilityId)
            return false;

        if (slot.HardpointType != default && item.HardpointType != slot.HardpointType)
            return false;

        return slot.Whitelist == null || Passes(slot.Whitelist, part);
    }

    /// <summary>The components and tags part of an entity whitelist, read off a prototype.</summary>
    private static bool Passes(EntityWhitelist list, EntityPrototype proto)
    {
        VehicleSmashRuleSystem.TryGetTags(proto, out var tags);

        var results = new List<bool>();
        results.AddRange((list.Components ?? System.Array.Empty<string>()).Select(proto.Components.ContainsKey));
        results.AddRange((list.Tags ?? new()).Select(tag => tags != null && tags.Contains(tag)));

        return list.RequireAll ? results.All(r => r) : results.Any(r => r);
    }

    /// <summary>The RSI a prototype's sprite names, from the nearest prototype in its parents that names one.</summary>
    private static bool TryGetRsi(IPrototypeManager protos, string id, out ResPath rsi)
    {
        foreach (var (parent, _) in protos.EnumerateAllParents<EntityPrototype>(id, includeSelf: true))
        {
            if (TryGetSprite(protos, parent, out var sprite) && sprite.TryGet<ValueDataNode>("sprite", out var value))
            {
                rsi = TextureRoot / value.Value;
                return true;
            }
        }

        rsi = default;
        return false;
    }

    /// <summary>The layer map keys of a prototype's sprite, from the nearest prototype in its parents that lists layers (a child's layers replace its parent's).</summary>
    private static HashSet<string> LayerKeys(IPrototypeManager protos, string id)
    {
        var keys = new HashSet<string>();

        foreach (var (parent, _) in protos.EnumerateAllParents<EntityPrototype>(id, includeSelf: true))
        {
            if (!TryGetSprite(protos, parent, out var sprite) || !sprite.TryGet<SequenceDataNode>("layers", out var layers))
                continue;

            foreach (var layer in layers)
            {
                if (layer is not MappingDataNode mapping || !mapping.TryGet<SequenceDataNode>("map", out var map))
                    continue;

                foreach (var key in map)
                {
                    if (key is ValueDataNode value)
                        keys.Add(value.Value);
                }
            }

            break;
        }

        return keys;
    }

    private static bool TryGetSprite(IPrototypeManager protos, string id, [NotNullWhen(true)] out MappingDataNode? sprite)
    {
        sprite = null;

        if (!protos.TryGetMapping(typeof(EntityPrototype), id, out var mapping) ||
            !mapping.TryGet<SequenceDataNode>("components", out var components))
        {
            return false;
        }

        foreach (var component in components)
        {
            if (component is MappingDataNode node && node.TryGet<ValueDataNode>("type", out var type) && type.Value == "Sprite")
            {
                sprite = node;
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> RsiStates(IResourceManager resources, ResPath rsi)
    {
        using var meta = JsonDocument.Parse(resources.ContentFileReadAllText(rsi / "meta.json"));

        return meta.RootElement.GetProperty("states")
            .EnumerateArray()
            .Select(state => state.GetProperty("name").GetString()!)
            .ToHashSet();
    }
}
