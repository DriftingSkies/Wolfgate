#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.NpcCrew.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

/// <summary>A crew's gun holds fire while a ship it must spare is in its line; Reavers fire through it.</summary>
public sealed partial class WFCrewTest
{
    [Test]
    public async Task GunsHoldFireThroughBystandersUnlessHostileOrReckless()
    {
        var deck = await CreateDeck(new Vector2(500, 3900), 3, true);
        var target = await CreateDeck(new Vector2(600, 3900), 3, true);
        var bystander = await CreateDeck(new Vector2(550, 3900), 3, true);
        EntityUid gun = default;
        await Server.WaitAssertion(() =>
        {
            ConvoyCrew(deck, "WFCrewPilot", "line");
            gun = SEntMan.SpawnEntity("WFTestCrewCannon", new EntityCoordinates(deck, new Vector2(1.5f, 1.5f)));
            for (var y = 0; y < 3; y++)
            {
                SEntMan.SpawnEntity("WallSolid", new EntityCoordinates(bystander, new Vector2(1.5f, y + 0.5f)));
            }
        });
        await RunTicks(5);

        var gunners = Server.System<WFGunnerDutySystem>();
        var xforms = Server.System<SharedTransformSystem>();
        MapCoordinates At() => xforms.ToMapCoordinates(new EntityCoordinates(target, new Vector2(1.5f, 1.5f)));
        await Server.WaitAssertion(() =>
        {
            Assert.That(gunners.ClearGuns(deck, "line", new List<EntityUid> { gun }, target, At()), Is.Empty,
                "A ship in the line of fire is spared.");
            xforms.SetCoordinates(bystander, new EntityCoordinates(MapData.MapUid, new Vector2(550, 3950)));
        });
        await RunTicks(Ticks(0.6));
        await Server.WaitAssertion(() =>
        {
            Assert.That(gunners.ClearGuns(deck, "line", new List<EntityUid> { gun }, target, At()), Is.EquivalentTo(new[] { gun }),
                "With the line clear the gun fires.");
            xforms.SetCoordinates(bystander, new EntityCoordinates(MapData.MapUid, new Vector2(550, 3900)));
            Server.System<WFCrewAlertSystem>().ReportShipThreat(deck, "line", bystander);
        });
        await RunTicks(Ticks(0.6));
        await Server.WaitAssertion(() =>
        {
            Assert.That(gunners.ClearGuns(deck, "line", new List<EntityUid> { gun }, target, At()), Is.EquivalentTo(new[] { gun }),
                "A hostile ship in the way is no reason to hold fire.");
        });

        var reaver = await CreateDeck(new Vector2(500, 3800), 3, true);
        var prey = await CreateDeck(new Vector2(600, 3800), 3, true);
        var bystanderTwo = await CreateDeck(new Vector2(550, 3800), 3, true);
        EntityUid reaverGun = default;
        await Server.WaitAssertion(() =>
        {
            reaverGun = SEntMan.SpawnEntity("WFTestCrewCannon", new EntityCoordinates(reaver, new Vector2(1.5f, 1.5f)));
            for (var y = 0; y < 3; y++)
            {
                SEntMan.SpawnEntity("WallSolid", new EntityCoordinates(bystanderTwo, new Vector2(1.5f, y + 0.5f)));
            }

            SEntMan.EnsureComponent<Content.Shared._Mono.Company.CompanyComponent>(reaver).CompanyName = "WFAshfallReavers";
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            var at = xforms.ToMapCoordinates(new EntityCoordinates(prey, new Vector2(1.5f, 1.5f)));
            Assert.That(gunners.ClearGuns(reaver, "line", new List<EntityUid> { reaverGun }, prey, at), Is.EquivalentTo(new[] { reaverGun }),
                "The Reavers fire through whatever is in the way.");
        });
    }
}
