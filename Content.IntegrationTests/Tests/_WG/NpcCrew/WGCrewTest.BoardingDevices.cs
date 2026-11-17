#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>AI equipment is neither a boarder nor a radio contact, even with a hostile faction.</summary>
    [Test]
    public async Task CrewIgnoresAiEquipmentAsBoarders()
    {
        var deck = await CreateDeck(new Vector2(20, 20), 7, gravity: true);
        var devices = new List<EntityUid>();
        EntityUid witness = default, radio = default;
        await Server.WaitAssertion(() =>
        {
            var crew = Server.System<WGCrewSystem>();
            witness = crew.SpawnCrewman(WGCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(1.5f, 2.5f)), "ai-devices")!.Value;
            radio = crew.SpawnCrewman(WGCrewRoles.RadioOperator,
                new EntityCoordinates(deck, new Vector2(1.5f, 4.5f)), "ai-devices")!.Value;
            foreach (var member in new[] { witness, radio })
            {
                SEntMan.GetComponent<HTNComponent>(member).Enabled = false;
                SEntMan.EnsureComponent<WGCrewSecurityComponent>(member).Boarding = WGCrewSecurityResponse.Hostile;
            }
            var prototypes = new[] { "PersonalAI", "SyndicatePersonalAI", "PotatoAI", "PositronicBrain", "StationAiBrain" };
            for (var i = 0; i < prototypes.Length; i++)
                devices.Add(SEntMan.SpawnAtPosition(prototypes[i], new EntityCoordinates(deck, new Vector2(3.5f, i + 1.5f))));

            var core = SEntMan.SpawnAtPosition(null, new EntityCoordinates(deck, new Vector2(5.5f, 3.5f)));
            var ai = SEntMan.EnsureComponent<StationAiCoreComponent>(core);
            Assert.That(ai.RemoteEntity, Is.Not.Null);
            devices.Add(core);
            devices.Add(ai.RemoteEntity!.Value);
            foreach (var device in devices)
            {
                // Exercise AI cores/eyes with MobState too, and the radio officer's separate hostile scan.
                SEntMan.EnsureComponent<MobStateComponent>(device);
                Server.System<NpcFactionSystem>().AddFaction(device, "SimpleHostile");
                Assert.That(Server.System<WGCrewSecuritySystem>().IsBoardingCandidate(device), Is.False);
            }
        });
        await RunTicks(70);
        await Server.WaitAssertion(() =>
        {
            var security = Server.System<WGCrewSecuritySystem>();
            var comms = Server.System<WGCrewCommsSystem>();
            foreach (var member in new[] { witness, radio })
            {
                foreach (var device in devices)
                {
                    // Direct reports and delivered sightings must use the same classification as polling.
                    security.ReceiveSighting(member, device);
                    comms.Report(member, device);
                    Assert.That(security.IsHostileVisitor(member, device), Is.False);
                    Assert.That(SEntMan.TryGetComponent<FactionExceptionComponent>(member, out var exceptions)
                        && exceptions.Hostiles.Any(target => target == device), Is.False);
                    Assert.That(comms.Knows(member, device), Is.False);
                }
                Assert.That(SEntMan.GetComponent<WGCrewComponent>(member).NextReport, Is.EqualTo(TimeSpan.Zero));
            }
            Assert.That(Sent(radio), Is.Empty);
        });
    }

    /// <summary>A physical borg or mindless hostile NPC still triggers boarding security and radio reports.</summary>
    [TestCase("PlayerBorgBattery")]
    [TestCase(Hostile)]
    public async Task CrewDetectsPhysicalBoarders(string prototype)
    {
        var deck = await CreateDeck(new Vector2(20, 20), 7, gravity: true);
        EntityUid radio = default, visitor = default;
        await Server.WaitAssertion(() =>
        {
            radio = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.RadioOperator,
                new EntityCoordinates(deck, new Vector2(1.5f, 3.5f)), "physical-boarder")!.Value;
            SEntMan.GetComponent<HTNComponent>(radio).Enabled = false;
            SEntMan.EnsureComponent<WGCrewSecurityComponent>(radio).Boarding = WGCrewSecurityResponse.Hostile;
            visitor = SEntMan.SpawnAtPosition(prototype, new EntityCoordinates(deck, new Vector2(3.5f, 3.5f)));
            if (prototype != Hostile)
                Server.System<NpcFactionSystem>().ClearFactions(visitor);
            Assert.That(Server.System<WGCrewSecuritySystem>().IsBoardingCandidate(visitor), Is.True);
            Assert.That(Server.System<WGCrewSecuritySystem>().IsAuthorized(radio, visitor), Is.False);
            Assert.That(Server.System<WGCrewWeaponSystem>().CanSee(radio, visitor), Is.True);
        });
        await RunTicks(70);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WGCrewSecuritySystem>().IsHostileVisitor(radio, visitor), Is.True);
            Assert.That(SEntMan.GetComponent<FactionExceptionComponent>(radio).Hostiles.Any(target => target == visitor), Is.True);
            Assert.That(SEntMan.GetComponent<WGCrewComponent>(radio).NextReport, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(Sent(radio).Any(line => line.Line == WGRadioLine.BoardWarning), Is.True);
        });
    }
}
