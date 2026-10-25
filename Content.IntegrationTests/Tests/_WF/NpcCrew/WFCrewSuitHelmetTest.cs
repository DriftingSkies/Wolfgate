using System.Linq;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

/// <summary>A suited crewman wears his suit's own helmet, which goes back into the suit when his head comes off.</summary>
[TestFixture]
public sealed class WFCrewSuitHelmetTest
{
    [Test]
    public async Task VoidKitHelmetStaysWithSuit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();
        var inventory = entities.System<InventorySystem>();
        var body = entities.System<SharedBodySystem>();

        EntityUid suit = default;
        EntityUid helmet = default;
        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out var mapId);
            var mob = entities.SpawnEntity("MobHuman", new MapCoordinates(0, 0, mapId));
            entities.System<WFCrewEvaSystem>().GiveVoidKit(mob);

            Assert.That(inventory.TryGetSlotEntity(mob, "outerClothing", out var worn));
            suit = worn!.Value;
            Assert.That(inventory.TryGetSlotEntity(mob, "head", out var head));
            helmet = head!.Value;
            Assert.That(entities.TryGetComponent<AttachedClothingComponent>(helmet, out var attached) && attached.AttachedUid == suit,
                "the crewman should wear his suit's own helmet, not a loose one");

            var part = body.GetBodyChildrenOfType(mob, BodyPartType.Head).Single().Id;
            Assert.That(entities.System<WolfmedBodySystem>().TryDetachPart(part));
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var container = entities.GetComponent<ToggleableClothingComponent>(suit).Container;
            Assert.That(container, Is.Not.Null);
            Assert.That(container!.Contains(helmet), "a lost head should leave the helmet in its suit, not on the floor");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BareHeadGetsSuitHelmet()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var inventory = entities.System<InventorySystem>();
            entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            var mob = entities.SpawnEntity("MobHuman", coords);
            var suit = entities.SpawnEntity("ClothingOuterHardsuitSpatio", coords);
            Assert.That(inventory.TryEquip(mob, suit, "outerClothing", silent: true, force: true));

            entities.System<WFCrewEvaSystem>().FitSuitHelmet(mob);

            Assert.That(inventory.TryGetSlotEntity(mob, "head", out var head)
                && entities.TryGetComponent<AttachedClothingComponent>(head, out var attached) && attached.AttachedUid == suit,
                "a bare-headed crewman should get his suit's helmet");
        });

        await pair.CleanReturnAsync();
    }
}
