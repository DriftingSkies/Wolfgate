#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WG.ShipShields;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server._WG.ShipShields;
using Content.Server.Projectiles;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.SpaceArtillery;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.ShipShields;
using Content.Shared.Damage;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>Native shield-only impacts alert the whole convoy, while fire from its escorts does not.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task CrewEscortRespondsToShieldAbsorption(bool beam)
    {
        var leader = await CreateDeck(new Vector2(500, 500), 7, true);
        var escort = await CreateDeck(new Vector2(600, 500), 7, true);
        var attacker = await CreateDeck(new Vector2(700, 500), 7, true);
        await Server.WaitAssertion(() =>
        {
            var pilot = ConvoyCrew(escort, "WGCrewPilot", "shield-escort");
            var observer = ConvoyCrew(leader, "WGCrewRadioOperator", "shield-leader");
            Server.System<WGPilotDutySystem>().Escort(pilot, leader, 100);
            foreach (var faction in SEntMan.GetComponent<NpcFactionMemberComponent>(observer).Factions)
                Server.System<NpcFactionSystem>().AddFaction(attacker, faction.Id);
            Server.ResolveDependency<IConsoleHost>().ExecuteCommand($"shieldentity {leader}");
            var shield = SEntMan.GetComponent<ShipShieldedComponent>(leader).Shield;
            var points = SEntMan.GetComponent<WGShipShieldVisualsComponent>(shield).Contours.SelectMany(contour => contour).ToArray();
            var center = (points.Aggregate(Vector2.Min) + points.Aggregate(Vector2.Max)) / 2f;
            var contact = new EntityCoordinates(shield, new Vector2(points.Min(point => point.X), center.Y));
            var hull = SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(leader, center));
            var before = SEntMan.GetComponent<DamageableComponent>(hull).TotalDamage;
            Fire(escort);
            var alerts = Server.System<WGCrewAlertSystem>();
            // A formation partner that opens fire is an attacker to its victim only.
            Assert.That(alerts.GetHostileShips(leader, "shield-leader"), Is.EquivalentTo(new[] { escort }));
            Assert.That(alerts.GetHostileShips(escort, "shield-escort"), Is.Empty);
            Fire(attacker);
            Assert.That(alerts.GetHostileShips(leader, "shield-leader"), Is.EquivalentTo(new[] { escort, attacker }));
            Assert.That(alerts.GetHostileShips(escort, "shield-escort"), Is.EquivalentTo(new[] { attacker }));
            Assert.That(SEntMan.GetComponent<DamageableComponent>(hull).TotalDamage, Is.EqualTo(before),
                "Aggression must be detected even though the shield protected the hull.");

            void Fire(EntityUid source)
            {
                var gun = SEntMan.SpawnAtPosition(null, new EntityCoordinates(source, Vector2.One));
                if (!beam)
                {
                    var shot = SEntMan.SpawnAtPosition("BulletDebugZoom", contact);
                    SEntMan.EnsureComponent<ShipWeaponProjectileComponent>(shot);
                    var projectile = SEntMan.GetComponent<ProjectileComponent>(shot);
                    projectile.Weapon = gun;
                    Server.System<ProjectileSystem>().ProjectileCollide(
                        (shot, projectile, SEntMan.GetComponent<PhysicsComponent>(shot)), shield);
                    Assert.That(projectile.ProjectileSpent, Is.True);
                    return;
                }
                var outside = new EntityCoordinates(shield, contact.Position - new Vector2(10, 0));
                var laser = SEntMan.SpawnAtPosition(null, outside);
                SEntMan.EnsureComponent<HitscanBasicRaycastComponent>(laser).MaxDistance = 100;
                SEntMan.EnsureComponent<HitscanBasicDamageComponent>(laser).Damage = new DamageSpecifier
                {
                    DamageDict = { ["Heat"] = 10 },
                };
                var trace = new HitscanTraceEvent
                {
                    FromCoordinates = outside, ShotDirection = Vector2.UnitX, Gun = gun,
                };
                SEntMan.EventBus.RaiseLocalEvent(laser, ref trace);
            }
        });
    }
    /// <summary>Protected cannon fire passes allied shields while explicit attacks and retaliation still drain them.</summary>
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task CrewCannonShieldProtectionHonorsHostileOrders(bool beam, bool attackOrder)
    {
        var leader = await CreateDeck(new Vector2(500, 500), 7, true);
        var escort = await CreateDeck(new Vector2(600, 500), 7, true);
        await Server.WaitAssertion(() =>
        {
            const string group = "shield-gunner";
            var pilot = ConvoyCrew(escort, "WGCrewPilot", group);
            var gunner = ConvoyCrew(escort, "WGCrewGunner", group);
            ConvoyCrew(leader, "WGCrewRadioOperator", "shield-ward");
            var pilots = Server.System<WGPilotDutySystem>();
            pilots.Escort(pilot, leader, 100);
            Server.ResolveDependency<IConsoleHost>().ExecuteCommand($"shieldentity {leader}");
            var shield = SEntMan.GetComponent<ShipShieldedComponent>(leader).Shield;
            var emitterUid = SEntMan.SpawnAtPosition(null, new EntityCoordinates(leader, Vector2.One));
            var emitter = SEntMan.EnsureComponent<ShipShieldEmitterComponent>(emitterUid);
            emitter.Shield = shield;
            emitter.Shielded = leader;
            SEntMan.GetComponent<ShipShieldComponent>(shield).Source = emitterUid;
            SEntMan.GetComponent<ShipShieldedComponent>(leader).Source = emitterUid;
            var points = SEntMan.GetComponent<WGShipShieldVisualsComponent>(shield).Contours.SelectMany(contour => contour).ToArray();
            var center = (points.Aggregate(Vector2.Min) + points.Aggregate(Vector2.Max)) / 2f;
            var contact = new EntityCoordinates(shield, new Vector2(points.Min(point => point.X), center.Y));
            var gun = SEntMan.SpawnAtPosition(null, new EntityCoordinates(escort, Vector2.One));
            var hull = SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(leader, center));
            var beforeHull = SEntMan.GetComponent<DamageableComponent>(hull).TotalDamage;
            var protection = Server.System<WGCrewFriendlyFireSystem>();
            var alerts = Server.System<WGCrewAlertSystem>();

            Fire(absorbed: false);
            Assert.That(emitter.Damage, Is.Zero, "Allied fire must not consume shield capacity.");
            Assert.That(SEntMan.HasComponent<WGShipShieldImpactAudioComponent>(leader), Is.False);
            Assert.That(alerts.GetHostileShips(leader, "shield-ward"), Is.Empty);
            Assert.That(SEntMan.GetComponent<DamageableComponent>(hull).TotalDamage, Is.EqualTo(beforeHull));

            pilots.Hold(pilot);
            foreach (var faction in SEntMan.GetComponent<NpcFactionMemberComponent>(gunner).Factions)
                Server.System<NpcFactionSystem>().AddFaction(leader, faction.Id);
            if (attackOrder)
            {
                Assert.That(Server.System<WGCrewObjectiveSystem>().SetQueue(escort, group,
                    new List<WGCrewObjective> { new() { Kind = WGCrewObjectiveKind.Attack, Target = SEntMan.GetNetEntity(leader), Range = 100 } }), Is.True);
            }
            else
                alerts.ReportShipThreat(escort, group, leader);
            Fire(absorbed: true);
            Assert.That(emitter.Damage, Is.GreaterThan(0f), "Explicit attacks and retaliation must still damage a same-faction shield.");
            Assert.That(SEntMan.HasComponent<WGShipShieldImpactAudioComponent>(leader), Is.True);
            Assert.That(alerts.GetHostileShips(leader, "shield-ward"), Is.EquivalentTo(new[] { escort }));
            Assert.That(SEntMan.GetComponent<DamageableComponent>(hull).TotalDamage, Is.EqualTo(beforeHull));

            void Fire(bool absorbed)
            {
                protection.TrackWeapon(gun, gunner);
                if (!beam)
                {
                    var shot = SEntMan.SpawnAtPosition("BulletDebugZoom", contact);
                    SEntMan.EnsureComponent<ShipWeaponProjectileComponent>(shot);
                    var projectile = SEntMan.GetComponent<ProjectileComponent>(shot);
                    projectile.Weapon = gun;
                    SEntMan.EventBus.RaiseLocalEvent(gun, new AmmoShotEvent { FiredProjectiles = new List<EntityUid> { shot } });
                    Assert.That(SEntMan.GetComponent<WGCrewShipFireComponent>(shot).Launched, Is.True);
                    protection.TrackWeapon(gun, gun);
                    Assert.That(SEntMan.HasComponent<WGCrewShipFireComponent>(gun), Is.False,
                        "A manual cannon takeover must not reclassify already-launched shots.");
                    var body = SEntMan.GetComponent<PhysicsComponent>(shot);
                    var collision = new PreventCollideEvent(shield, shot,
                        SEntMan.GetComponent<PhysicsComponent>(shield), body,
                        SEntMan.GetComponent<FixturesComponent>(shield).Fixtures.Values.First(),
                        SEntMan.GetComponent<FixturesComponent>(shot).Fixtures.Values.First());
                    SEntMan.EventBus.RaiseLocalEvent(shield, ref collision);
                    Assert.That(collision.Cancelled, Is.EqualTo(!absorbed));
                    if (!collision.Cancelled)
                        Server.System<ProjectileSystem>().ProjectileCollide((shot, projectile, body), shield);
                    var ray = new WGShipShieldProjectileRayHitEvent(shot, projectile,
                        Server.System<SharedTransformSystem>().ToMapCoordinates(contact));
                    SEntMan.EventBus.RaiseLocalEvent(shield, ref ray);
                    Assert.That(projectile.ProjectileSpent, Is.EqualTo(absorbed));
                    SEntMan.QueueDeleteEntity(shot);
                    return;
                }
                var outside = new EntityCoordinates(shield, contact.Position - new Vector2(10, 0));
                var laser = SEntMan.SpawnAtPosition(null, outside);
                SEntMan.EnsureComponent<HitscanBasicRaycastComponent>(laser).MaxDistance = 100;
                SEntMan.EnsureComponent<HitscanBasicDamageComponent>(laser).Damage = new DamageSpecifier
                {
                    DamageDict = { ["Heat"] = 10 },
                };
                SEntMan.EnsureComponent<WGShipShieldHitscanProbeComponent>(laser);
                var trace = new HitscanTraceEvent
                {
                    FromCoordinates = outside, ShotDirection = Vector2.UnitX, Gun = gun, Shooter = gunner,
                };
                SEntMan.EventBus.RaiseLocalEvent(laser, ref trace);
                var distance = Server.System<WGShipShieldHitscanProbeSystem>().Distance;
                Assert.That(distance, absorbed ? Is.EqualTo(10f).Within(.01f) : Is.GreaterThan(10f));
            }
        });
    }
}
