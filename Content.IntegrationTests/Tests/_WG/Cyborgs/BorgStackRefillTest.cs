#nullable enable
using System.Linq;
using Content.Server._WG.Cyborgs;
using Content.Server.Power.EntitySystems;
using Content.Server.Silicons.Borgs;
using Content.Server.Stack;
using Content.Shared._NF.Silicons.Borgs;
using Content.Shared._WG.Cyborgs;
using Content.Shared.Power.Components;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Cyborgs;

/// <summary>
/// An engineering cyborg is issued stocked construction stacks and gripper hands, and its refilling stacks restock
/// out of its cell, down to the reserve.
/// </summary>
[TestFixture]
[TestOf(typeof(WGBorgStackRefillSystem))]
public sealed class BorgStackRefillTest
{
    private const string Borg = "BorgChassisEngineer";
    private const string Cell = "PowerCellMedium";
    private const string Construction = "BorgModuleConstruction";
    private const string Tool = "BorgModuleTool";
    private const string Steel = "WGSheetSteelBorg";

    [Test]
    public async Task RefillsFromCellTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var borgs = server.System<BorgSystem>();
            var stacks = server.System<StackSystem>();
            var refill = server.System<WGBorgStackRefillSystem>();
            var containers = server.System<SharedContainerSystem>();
            var batteries = server.System<BatterySystem>();

            var borg = entMan.SpawnEntity(Borg, map.GridCoords);
            var cell = entMan.SpawnEntity(Cell, map.GridCoords);
            // Straight into the container: the chassis is locked, so the slot refuses an insert.
            Assert.That(containers.Insert(cell, containers.GetContainer(borg, "cell_slot"), force: true), "The cyborg did not take a power cell.");
            var battery = entMan.GetComponent<BatteryComponent>(cell);

            var modules = entMan.GetComponent<BorgChassisComponent>(borg).ModuleContainer.ContainedEntities;
            string? Proto(EntityUid uid) => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;

            var tool = modules.Single(m => Proto(m) == Tool);
            Assert.That(entMan.HasComponent<DroppableBorgModuleComponent>(tool), "The tool module has no gripper hands.");

            // Modules install when a mind powers the chassis up; this one has none.
            borgs.InstallAllModules(borg);
            borgs.UnselectModule(borg);
            borgs.SelectModule(borg, modules.Single(m => Proto(m) == Construction));

            var steel = EntityUid.Invalid;
            var query = entMan.EntityQueryEnumerator<WGBorgStackRefillComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.ParentUid == borg && Proto(uid) == Steel)
                    steel = uid;
            }

            Assert.That(steel.IsValid(), "The construction module gave no refilling steel.");
            var stack = entMan.GetComponent<StackComponent>(steel);
            var cost = entMan.GetComponent<WGBorgStackRefillComponent>(steel).ChargePerUnit;
            Assert.That(stack.Count, Is.EqualTo(15), "The steel stack did not start stocked.");

            // Only the steel is short, so only it draws on the cell.
            batteries.SetCharge(cell, battery.MaxCharge);
            stacks.SetCount(steel, 3, stack);
            refill.RefillAll();
            Assert.That(stack.Count, Is.EqualTo(4), "A stack in hand did not refill.");
            Assert.That(battery.CurrentCharge, Is.EqualTo(battery.MaxCharge - cost).Within(0.01f), "The refill was free.");

            // Stowed in its module the stack sits one level deeper.
            borgs.UnselectModule(borg);
            refill.RefillAll();
            Assert.That(stack.Count, Is.EqualTo(5), "A stowed stack did not refill.");

            stacks.SetCount(steel, 15, stack);
            refill.RefillAll();
            Assert.That(stack.Count, Is.EqualTo(15), "A stack refilled past its target.");

            stacks.SetCount(steel, 3, stack);
            batteries.SetCharge(cell, battery.MaxCharge * WGBorgStackRefillSystem.Reserve + cost / 2f);
            refill.RefillAll();
            Assert.That(stack.Count, Is.EqualTo(3), "A refill spent the cell's reserve.");
        });

        await pair.CleanReturnAsync();
    }
}
