#nullable enable
using System.Collections.Generic;
using Content.Shared._WF.Vehicles.Weapons;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>Every seat that makes its occupant a weapons operator also has the weapons window to open.</summary>
[TestFixture]
public sealed class VehicleSeatPrototypeTest
{
    [Test]
    public async Task EveryWeaponsSeatHasTheWeaponsWindow()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        var seatIds = new List<string>();
        await server.WaitPost(() =>
        {
            var factory = entMan.ComponentFactory;
            foreach (var proto in server.ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (!proto.Abstract && proto.TryGetComponent<VehicleWeaponsSeatComponent>(out _, factory))
                    seatIds.Add(proto.ID);
            }
        });

        Assert.That(seatIds, Is.Not.Empty, "No weapons seat was found at all.");

        await server.WaitAssertion(() =>
        {
            var ui = server.System<SharedUserInterfaceSystem>();
            using (Assert.EnterMultipleScope())
            {
                foreach (var id in seatIds)
                {
                    var seat = entMan.SpawnEntity(id, new EntityCoordinates(map.Grid.Owner, 1.5f, 1.5f));
                    Assert.That(ui.HasUi(seat, VehicleWeaponsUiKey.Key), $"{id}: a weapons seat needs the VehicleWeaponsUiKey.Key interface.");
                    entMan.DeleteEntity(seat);
                }
            }
        });

        await pair.CleanReturnAsync();
    }
}
