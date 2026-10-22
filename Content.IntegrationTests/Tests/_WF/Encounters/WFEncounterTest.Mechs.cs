#nullable enable
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.Encounters;
using Content.Shared.Damage;
using Content.Shared.Mech.Components;
using Content.Shared.Damage.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

/// <summary>A piloted mech flying free of any grid is a vessel to encounter ships: zones and hits make it a target.</summary>
public sealed partial class WFEncounterTest
{
    [TestPrototypes]
    private const string MechPrototypes = @"
- type: wfEncounter
  id: WFTestMechQuiet
  name: wf-encounter-name-convoy
  start: Manual
  ships:
  - key: quiet
    vessel: WFDredger
";

    /// <summary>Seats a pilot in a mech's pilot slot directly; the fixture's player can't climb in by hand.</summary>
    private void Board(EntityUid mech, EntityUid pilot)
    {
        var slot = SEntMan.GetComponent<MechComponent>(mech).PilotSlot;
        Assert.That(Server.System<SharedContainerSystem>().Insert(pilot, slot), Is.True);
    }

    /// <summary>Takes the pilot out by the container; ejecting the fixture's mob logs an eye error.</summary>
    private void Unboard(EntityUid mech, EntityUid pilot)
    {
        Server.System<SharedContainerSystem>().Remove(pilot, SEntMan.GetComponent<MechComponent>(mech).PilotSlot);
    }

    /// <summary>A mech in the attack zone is engaged and a ship target while piloted, and dropped once empty.</summary>
    [Test]
    public async Task ZoneEngagesFreeMech()
    {
        EntityUid encounter = default, mech = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestAuditZoneGuarded");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(57000, -57000), MapData.MapId), out encounter), Is.True);

            mech = SEntMan.SpawnEntity("MechRipley", new MapCoordinates(new Vector2(57100, -57000), MapData.MapId));
            // The patrol's guns would otherwise break the mech, and throw its pilot out, before the check.
            Server.System<SharedGodmodeSystem>().EnableGodmode(mech);
            Board(mech, SEntMan.GetEntity(Player));
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["guarded"];
            var alerts = Server.System<WFCrewAlertSystem>();
            Assert.That(state.Engaged, Does.Contain(mech), "A piloted mech in the attack zone is engaged.");
            Assert.That(alerts.GetHostileShips(state.Grid, state.Group), Does.Contain(mech));

            Unboard(mech, SEntMan.GetEntity(Player));
            Assert.That(alerts.GetHostileShips(state.Grid, state.Group), Does.Not.Contain(mech), "An empty mech is no vessel.");

            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WFEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(mech);
        });
        await RunTicks(10);
    }

    /// <summary>A mech pilot's hit on a ship's crew, from beyond any zone, makes the mech an attacker the ship fires on.</summary>
    [Test]
    public async Task MechHitIsAnAttack()
    {
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestMechQuiet");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(58000, -58000), MapData.MapId), out var encounter), Is.True);
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["quiet"];

            var mech = SEntMan.SpawnEntity("MechRipley", new MapCoordinates(new Vector2(58300, -58000), MapData.MapId));
            var pilot = SEntMan.GetEntity(Player);
            Board(mech, pilot);

            EntityUid? victim = null;
            var members = SEntMan.EntityQueryEnumerator<WFCrewComponent>();
            while (members.MoveNext(out var uid, out var member))
            {
                if (member.Group == state.Group)
                    victim = uid;
            }

            Assert.That(victim, Is.Not.Null);
            var damage = new DamageSpecifier(Server.ResolveDependency<IPrototypeManager>().Index<Content.Shared.Damage.Prototypes.DamageTypePrototype>("Blunt"), 1);
            Server.System<DamageableSystem>().TryChangeDamage(victim, damage, origin: pilot);

            var alerts = Server.System<WFCrewAlertSystem>();
            Assert.That(alerts.IsAttacker(state.Grid, mech), Is.True, "The mech, not its pilot, is the attacker.");
            Assert.That(alerts.GetHostileShips(state.Grid, state.Group), Does.Contain(mech));

            Unboard(mech, pilot);
            Server.System<SharedTransformSystem>().SetCoordinates(pilot, new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WFEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(mech);
        });
        await RunTicks(10);
    }
}
