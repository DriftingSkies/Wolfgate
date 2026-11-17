#nullable enable
using System.Linq;
using Content.Server._WG.Drydock;
using Content.Server.Silicons.Borgs;
using Content.Shared._NF.Interaction.Components;
using Content.Shared._WG.Cyborgs;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.Cyborgs;

/// <summary>
/// A cyborg picks a drydock repair voucher up with its gripper hand, and the drydock finds it there and put away.
/// </summary>
[TestFixture]
public sealed class BorgRepairVoucherTest
{
    private const string Borg = "BorgChassisEngineer";
    private const string Cell = "PowerCellHigh";
    private const string Tool = "BorgModuleTool";
    private const string Gripped = "PowerCellSmallPrinted";
    private const string Voucher = "WGDrydockRepairVoucher";

    [Test]
    public async Task GripperTakesRepairVoucherTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var borgs = entMan.System<BorgSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var minds = entMan.System<SharedMindSystem>();
        var drydock = entMan.System<WGDrydockSystem>();

        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        var borg = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var coords = new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f);
            borg = entMan.SpawnEntity(Borg, coords);
            containers.Insert(entMan.SpawnEntity(Cell, coords), containers.GetContainer(borg, "cell_slot"), force: true);

            // A cyborg only selects modules with a player in it.
            minds.WipeMind(session.ContentData()?.Mind);
            minds.TransferTo(minds.CreateMind(session.UserId).Owner, borg);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            string? Proto(EntityUid uid) => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;

            var modules = entMan.GetComponent<BorgChassisComponent>(borg).ModuleContainer.ContainedEntities;
            borgs.InstallAllModules(borg);
            borgs.UnselectModule(borg);
            var tool = modules.Single(m => Proto(m) == Tool);
            borgs.SelectModule(borg, tool);

            var cell = hands.EnumerateHeld(borg).Single(held => Proto(held) == Gripped);
            Assert.That(hands.TryDrop(borg, cell), "The cyborg could not empty its gripper hand.");

            var coords = entMan.GetComponent<TransformComponent>(borg).Coordinates;
            var voucher = entMan.SpawnEntity(Voucher, coords);
            var interaction = entMan.System<SharedInteractionSystem>();
            foreach (var placeholder in hands.EnumerateHeld(borg).Where(entMan.HasComponent<HandPlaceholderComponent>).ToArray())
            {
                interaction.RangedInteractDoBefore(borg, placeholder, voucher, coords, canReach: true);
            }

            Assert.That(hands.IsHolding(borg, voucher), "The gripper hand should take a repair voucher.");
            Assert.That(drydock.FindRepairVoucher(borg, map.Grid.Owner), Is.EqualTo(voucher), "The drydock should find a held voucher.");

            borgs.UnselectModule(borg);
            Assert.That(entMan.System<WGBorgIdCardSystem>().StowedItems(borg), Does.Contain(voucher),
                "The voucher should be put away with the tool module.");
            Assert.That(drydock.FindRepairVoucher(borg, map.Grid.Owner), Is.EqualTo(voucher),
                "The drydock should find a voucher put away in the tool module.");
        });

        // A client still attached to a cyborg throws while the pair's cleanup deletes it.
        await server.WaitPost(() => minds.WipeMind(session.ContentData()?.Mind));
        await pair.RunTicksSync(5);

        await pair.CleanReturnAsync();
    }
}
