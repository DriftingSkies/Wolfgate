#nullable enable
using Content.Shared._WF.Vehicles.Hardpoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>Every hardpoint item and slot in the prototypes points at a real family, type, slot type and compatibility entity.</summary>
[TestFixture]
public sealed class HardpointPrototypeTest
{
    [Test]
    public async Task EveryHardpointReferenceResolves()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var factory = server.EntMan.ComponentFactory;

        await server.WaitAssertion(() =>
        {
            var items = 0;

            bool Has<T>(EntProtoId id) where T : IComponent, new()
            {
                return protos.TryIndex(id, out var target) && target.TryGetComponent<T>(out _, factory);
            }

            foreach (var proto in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract)
                    continue;

                if (proto.TryGetComponent<HardpointItemComponent>(out var item, factory))
                {
                    items++;

                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(Has<HardpointTypeComponent>(item.HardpointType), $"{proto.ID}: hardpointType {item.HardpointType} is not a hardpoint type.");

                        if (item.VehicleFamily is { } family)
                            Assert.That(Has<HardpointVehicleFamilyComponent>(family), $"{proto.ID}: vehicleFamily {family} is not a family.");

                        if (item.SlotType is { } slotType)
                            Assert.That(Has<HardpointSlotTypeComponent>(slotType), $"{proto.ID}: slotType {slotType} is not a slot type.");

                        if (item.CompatibilityId is { } compatibility)
                            Assert.That(Has<HardpointCompatibilityComponent>(compatibility), $"{proto.ID}: compatibilityId {compatibility} is not a compatibility id.");

                        Assert.That(proto.TryGetComponent<HardpointIntegrityComponent>(out _, factory), $"{proto.ID}: a hardpoint with no integrity can never wear or break.");
                    }
                }

                if (proto.TryGetComponent<HardpointSlotsComponent>(out var slots, factory))
                {
                    using (Assert.EnterMultipleScope())
                    {
                        if (slots.VehicleFamily is { } family)
                            Assert.That(Has<HardpointVehicleFamilyComponent>(family), $"{proto.ID}: vehicleFamily {family} is not a family.");

                        foreach (var slot in slots.Slots)
                        {
                            Assert.That(Has<HardpointTypeComponent>(slot.HardpointType), $"{proto.ID}: slot {slot.Id} hardpointType {slot.HardpointType} is not a hardpoint type.");

                            if (slot.SlotType is { } slotType)
                                Assert.That(Has<HardpointSlotTypeComponent>(slotType), $"{proto.ID}: slot {slot.Id} slotType {slotType} is not a slot type.");

                            if (slot.CompatibilityId is { } compatibility)
                                Assert.That(Has<HardpointCompatibilityComponent>(compatibility), $"{proto.ID}: slot {slot.Id} compatibilityId {compatibility} is not a compatibility id.");
                        }
                    }
                }
            }

            Assert.That(items, Is.GreaterThan(0), "No hardpoint items were found at all.");
        });

        await pair.CleanReturnAsync();
    }
}
