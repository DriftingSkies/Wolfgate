#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Company;
using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>
    /// A crew passes a hull showing its colours, otherwise goes by the free players aboard, and answers its own side
    /// only after a great many hits.
    /// </summary>
    [Test]
    public async Task OwnSidePlayerShipsNeedMassiveFriendlyFire()
    {
        var pdvPatrol = await CreateDeck(new Vector2(1020, 820), 7, true);
        var tsfPatrol = await CreateDeck(new Vector2(1080, 820), 7, true);
        var hull = await CreateDeck(new Vector2(1020, 880), 7, true);
        var npcPdv = await CreateDeck(new Vector2(1140, 820), 7, true);
        var stranger = await CreateDeck(new Vector2(1140, 880), 7, true);
        var spared = Server.System<WFCrewSparedSystem>();
        var alerts = Server.System<WFCrewAlertSystem>();
        await Server.WaitAssertion(() =>
        {
            void Flag(EntityUid uid, string company) =>
                SEntMan.EnsureComponent<CompanyComponent>(uid).CompanyName = company;

            var setup = Server.System<WFCrewSetupSystem>();
            Flag(pdvPatrol, "PDV");
            Flag(tsfPatrol, "TSF");
            Flag(hull, "TSF");
            Flag(npcPdv, "PDV");
            Flag(stranger, "USSP");
            setup.MarkProfile(pdvPatrol, "WFCrewProfileHauler");
            setup.MarkProfile(tsfPatrol, "WFCrewProfileHauler");
            setup.MarkProfile(npcPdv, "WFCrewProfileHauler");
            ConvoyCrew(pdvPatrol, "WFCrewGunner", "pdv");

            // A PDV crew flying a stolen TSF hull with its IFF on.
            Flag(SPlayer, "PDVCivilian");
            Server.System<SharedTransformSystem>().SetCoordinates(SPlayer, new EntityCoordinates(hull, new Vector2(0.5f, 0.5f)));
        });
        await RunTicks(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(spared.Spared(tsfPatrol, hull), Is.True, "TSF colours pass a TSF patrol.");
            Assert.That(spared.Spared(pdvPatrol, hull), Is.True, "A free PDV crew passes a PDV patrol under any flag.");
            Assert.That(spared.Spared(pdvPatrol, stranger), Is.False);
            Assert.That(spared.Spared(pdvPatrol, npcPdv), Is.False, "NPC crews settle their own fights.");
            Server.System<ShuttleSystem>().AddIFFFlag(hull, IFFFlags.HideLabel);
        });
        await RunTicks(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(spared.Spared(tsfPatrol, hull), Is.False, "Running dark, the TSF patrol goes by the PDV crew.");
            Assert.That(spared.Spared(pdvPatrol, hull), Is.True);
            alerts.ReportShipThreat(pdvPatrol, "pdv", hull);
            Assert.That(alerts.IsHostileShip(pdvPatrol, "pdv", hull), Is.False, "No report turns it on its own side.");
        });

        // One hit a second for 25 seconds stays inside the allowance of any 30 s window.
        for (var i = 0; i < 25; i++)
        {
            await Server.WaitAssertion(() =>
                Assert.That(alerts.ReportIncomingFire(pdvPatrol, "pdv", hull), Is.False, $"Hit {i} is excused."));
            await RunTicks((int) STiming.TickRate);
        }

        await Server.WaitAssertion(() => Assert.That(alerts.IsAttacker(pdvPatrol, hull), Is.False));

        // Sustained fire, two hits a second, runs past it.
        var attacked = false;
        for (var i = 0; i < 60 && !attacked; i++)
        {
            await Server.WaitAssertion(() => attacked = alerts.ReportIncomingFire(pdvPatrol, "pdv", hull));
            await RunTicks((int) STiming.TickRate / 2 + 1);
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(attacked && alerts.IsAttacker(pdvPatrol, hull), Is.True, "Massive friendly fire is answered.");

            // The PDV player is now a prisoner aboard.
            SEntMan.EnsureComponent<CuffableComponent>(SPlayer);
            var cuffs = SEntMan.SpawnEntity("Handcuffs", SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates);
            Assert.That(Server.System<SharedCuffableSystem>().TryAddNewCuffs(SPlayer, SPlayer, cuffs), Is.True);
        });
        await RunTicks(1);

        await Server.WaitAssertion(() =>
            Assert.That(spared.Spared(pdvPatrol, hull), Is.False, "A captive does not count as crew."));
    }
}
