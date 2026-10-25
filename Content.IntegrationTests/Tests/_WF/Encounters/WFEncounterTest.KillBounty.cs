#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._NF.Bank;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.KillTracking;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stacks;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    /// <summary>
    /// Killing a Reaver crewman pays the killer the company's kill bounty at once, the assist half of it, and it still
    /// pays once the encounter is over, as for boarders left aboard a player's ship.
    /// </summary>
    [Test]
    public async Task KillingAReaverPaysOnTheSpot()
    {
        var config = Server.ResolveDependency<IConfigurationManager>();
        var cap = 0;
        var bounty = 0;
        EntityUid encounter = default;
        await Server.WaitPost(() =>
        {
            cap = config.GetCVar(EncountersCVars.PayoutHourlyCap);
            // Other tests on this pair may have used the hour's allowance.
            config.SetCVar(EncountersCVars.PayoutHourlyCap, int.MaxValue);
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            bounty = prototypes.Index<WFStandingPrototype>("WFAshfallReavers").KillBounty;
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototypes.Index<WFEncounterPrototype>("WFTestSimRaider"),
                new MapCoordinates(new Vector2(-52000, 52000), MapData.MapId), out encounter), Is.True);
        });
        Assert.That(bounty, Is.GreaterThan(0), "The Reavers carry a kill bounty.");
        await Until(() => !Ship(encounter).Virtual && !Ship(encounter).Building, 30, "The raider's hull is built.");

        await Server.WaitAssertion(() =>
        {
            var group = Ship(encounter).Group;
            var crew = SEntMan.EntityQuery<WFCrewComponent>(true).Where(member => member.Group == group).Select(member => member.Owner).ToList();
            Assert.That(crew, Has.Count.GreaterThanOrEqualTo(4));

            var player = SEntMan.GetEntity(Player);
            SEntMan.EnsureComponent<BankAccountComponent>(player);
            var bank = Server.System<BankSystem>();
            bank.TryGetBalance(player, out var before);

            var kill = new KillReportedEvent(crew[0], new KillPlayerSource(ServerSession.UserId), null, false);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref kill);
            bank.TryGetBalance(player, out var afterKill);
            Assert.That(afterKill - before, Is.EqualTo(bounty), "The killer is paid the bounty at once.");

            // An assist by the killer themself is no second share.
            Server.System<WFEncounterSystem>().Resolve(encounter, WFEncounterResolution.Ended);
            var self = new KillReportedEvent(crew[1], new KillPlayerSource(ServerSession.UserId), new KillPlayerSource(ServerSession.UserId), false);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref self);
            bank.TryGetBalance(player, out var afterResolved);
            Assert.That(afterResolved - afterKill, Is.EqualTo(bounty), "Once the encounter is over its crew still pay, and only once each.");

            var npc = new KillReportedEvent(crew[2], new KillNpcSource(crew[1]), new KillPlayerSource(ServerSession.UserId), false);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref npc);
            bank.TryGetBalance(player, out var afterAssist);
            Assert.That(afterAssist - afterResolved, Is.EqualTo(bounty / 2), "An assist is paid half.");

            var again = new KillReportedEvent(crew[0], new KillPlayerSource(ServerSession.UserId), null, false);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref again);
            bank.TryGetBalance(player, out var afterAgain);
            Assert.That(afterAgain, Is.EqualTo(afterAssist), "A crewman revived and downed again pays nothing more.");

            // A laser names the gun: the kill comes in with nobody behind it, and the last player to strike him takes it.
            SEntMan.GetComponent<WFCrewComponent>(crew[3]).Struck[player] = STiming.CurTime;
            var laser = new KillReportedEvent(crew[3], new KillEnvironmentSource(), null, false);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref laser);
            bank.TryGetBalance(player, out var afterLaser);
            Assert.That(afterLaser - afterAgain, Is.EqualTo(bounty), "The last player to strike a crewman takes a kill nobody is named for.");

            Server.System<WFEncounterSystem>().End(encounter);
            config.SetCVar(EncountersCVars.PayoutHourlyCap, cap);
        });
        await RunTicks(10);
    }

    /// <summary>A player beating a Reaver crewman down by hand is paid the bounty, as a boarding party is.</summary>
    [Test]
    public async Task BeatingAReaverDownPays()
    {
        var config = Server.ResolveDependency<IConfigurationManager>();
        var cap = 0;
        EntityUid encounter = default;
        await Server.WaitPost(() =>
        {
            cap = config.GetCVar(EncountersCVars.PayoutHourlyCap);
            config.SetCVar(EncountersCVars.PayoutHourlyCap, int.MaxValue);
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototypes.Index<WFEncounterPrototype>("WFTestSimRaider"),
                new MapCoordinates(new Vector2(-56000, 52000), MapData.MapId), out encounter), Is.True);
        });
        await Until(() => !Ship(encounter).Virtual && !Ship(encounter).Building, 30, "The raider's hull is built.");

        var before = 0;
        EntityUid victim = default;
        await Server.WaitAssertion(() =>
        {
            var group = Ship(encounter).Group;
            victim = SEntMan.EntityQuery<WFCrewComponent>(true).First(member => member.Group == group).Owner;
            var player = SEntMan.GetEntity(Player);
            SEntMan.EnsureComponent<BankAccountComponent>(player);
            Server.System<BankSystem>().TryGetBalance(player, out before);
            var blunt = Server.ResolveDependency<IPrototypeManager>().Index<DamageTypePrototype>("Blunt");
            for (var i = 0; i < 20 && !Server.System<MobStateSystem>().IsIncapacitated(victim); i++)
                Server.System<DamageableSystem>().TryChangeDamage(victim, new DamageSpecifier(blunt, 20), true, origin: player);
            Assert.That(Server.System<MobStateSystem>().IsIncapacitated(victim), "The crewman goes down.");
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            Server.System<BankSystem>().TryGetBalance(SEntMan.GetEntity(Player), out var after);
            Assert.That(after - before, Is.EqualTo(1500), "The player who beat the crewman down is paid.");
            var drop = Server.ResolveDependency<IPrototypeManager>().Index<WFStandingPrototype>("WFAshfallReavers").CashDrop;
            var cash = Server.System<EntityLookupSystem>().GetEntitiesInRange<StackComponent>(SEntMan.GetComponent<TransformComponent>(victim).Coordinates, 1f)
                .Where(stack => SEntMan.GetComponent<MetaDataComponent>(stack).EntityPrototype?.ID == "SpaceCash")
                .Sum(stack => stack.Comp.Count);
            Assert.That(cash, Is.InRange(drop * 3 / 4, drop * 6 * 5 / 4), "The downed crewman drops his thalers.");
            Server.System<WFEncounterSystem>().End(encounter);
            config.SetCVar(EncountersCVars.PayoutHourlyCap, cap);
        });
        await RunTicks(10);
    }
}
