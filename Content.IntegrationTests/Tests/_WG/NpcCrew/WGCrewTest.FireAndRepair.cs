#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._Mono.FireControl;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Power.EntitySystems;
using Content.Shared._Mono.CCVar;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    [TestPrototypes]
    private const string FireAndRepairPrototypes = @"
- type: entity
  parent: WeaponPistolMk58
  id: WGTestCrewCannon
  components:
  - type: Gun
    projectileSpeed: 10
  - type: ChamberMagazineAmmoProvider
    boltClosed: true
  - type: ApcPowerReceiver
    needsPower: false
  - type: FireControllable
    ignoreLos: true
  - type: Transform
    anchored: true
  - type: Physics
    bodyType: Static

- type: entity
  parent: WallSolid
  id: WGTestRestrictedCrewWall
  components:
  - type: ShipRepairableRestrict
    toolWhitelist:
      tags: [ADSRepairTool]
";

    /// <summary>Real automatic cannon fire protects allies, while manual takeover affects only later shots.</summary>
    [Test]
    public async Task CrewCannonShotsPreserveTheirOriginalController()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        var friendly = await CreateDeck(new Vector2(45, 0), 5, gravity: true);
        EntityUid crew = default, cannon = default, ally = default, wall = default, round = default;
        await Server.WaitAssertion(() =>
        {
            crew = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(1.5f)), "cannon")!.Value;
            ally = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Deckhand,
                new EntityCoordinates(friendly, new Vector2(1.5f)), "escort")!.Value;
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
            SEntMan.GetComponent<HTNComponent>(ally).Enabled = false;
            cannon = SEntMan.SpawnAtPosition("WGTestCrewCannon", new EntityCoordinates(deck, new Vector2(3.5f)));
            wall = SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(friendly, new Vector2(3.5f)));
            Server.System<NpcFactionSystem>().AddFaction(friendly, "NanoTrasen");
            Server.System<NpcFactionSystem>().AddFaction(crew, "NanoTrasen");
        });
        await WaitUntil(() => Server.System<PowerReceiverSystem>().IsPowered(cannon), 120,
            () => "The fixture cannon's power receiver did not initialize.");
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<TransformComponent>(cannon).Anchored, Is.True);
            var targeting = Server.System<ShipTargetingSystem>();
            var target = targeting.Target(crew, new EntityCoordinates(friendly, new Vector2(3.5f)))!;
            target.Cannons.Add(cannon);
            target.WeaponCheckAccum = 10;
        });

        // Ships aim every ShipTargetingSystem.WGAimPeriod ticks, so give it real ticks instead of one direct call.
        await WaitUntil(() => SEntMan.HasComponent<WGCrewShipFireComponent>(cannon), 60,
            () => "The powered anchored cannon should accept a shot within the projectile's lifetime.");
        await Server.WaitAssertion(() =>
        {
            var targeting = Server.System<ShipTargetingSystem>();
            Assert.That(SEntMan.GetComponent<WGCrewShipFireComponent>(cannon).Crew, Is.EqualTo(crew),
                "ShipTargeting must preserve the controller when requesting automatic cannon fire.");
            targeting.Stop(crew);
        });
        await WaitUntil(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<WGCrewShipFireComponent, ProjectileComponent>();
            while (query.MoveNext(out var uid, out var firing, out _))
            {
                if (!firing.Launched || firing.Crew != crew)
                    continue;
                round = uid;
                return true;
            }
            return false;
        }, 120, () => "The cannon should launch a projectile attributed to its autonomous controller.");
        await Server.WaitAssertion(() =>
        {
            var protection = Server.System<WGCrewFriendlyFireSystem>();
            Assert.That(protection.Protected(cannon, ally), Is.True);
            Assert.That(protection.Protected(cannon, wall), Is.True, "Allied hulls are protected too.");
            Assert.That(SEntMan.GetComponent<ProjectileComponent>(round).Shooter, Is.EqualTo(crew));
            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Piercing", 5);
            var before = SEntMan.GetComponent<DamageableComponent>(wall).TotalDamage;
            Server.System<DamageableSystem>().TryChangeDamage(wall, damage, ignoreResistances: true, origin: cannon);
            Assert.That(SEntMan.GetComponent<DamageableComponent>(wall).TotalDamage, Is.EqualTo(before),
                "Hitscan damage uses the cannon itself as its origin and must also protect allies.");
            SEntMan.GetComponent<FireControllableComponent>(cannon).NextFire = TimeSpan.Zero;
            Assert.That(Server.System<FireControlSystem>().AttemptFire(cannon, cannon,
                new EntityCoordinates(friendly, new Vector2(3.5f)), noServer: true), Is.True);
            Assert.That(protection.Protected(cannon, ally), Is.False, "A manual fire-control command is not an NPC shot.");
            Server.System<MobStateSystem>().ChangeMobState(crew, MobState.Dead);
            Server.System<DamageableSystem>().TryChangeDamage(wall, damage, ignoreResistances: true, origin: crew, tool: round);
            Assert.That(SEntMan.GetComponent<DamageableComponent>(wall).TotalDamage, Is.EqualTo(before),
                "Rounds already launched remain protected after takeover or the gunner's death.");
            Server.System<DamageableSystem>().TryChangeDamage(wall, damage, ignoreResistances: true, origin: cannon);
            Assert.That(SEntMan.GetComponent<DamageableComponent>(wall).TotalDamage > before, Is.True,
                "Later manual cannon damage must not inherit the old controller's protection.");
            SEntMan.DeleteEntity(round);
            SEntMan.RemoveComponent<AutoShootGunComponent>(cannon);
        });
        await RunTicks(90);
    }

    /// <summary>A restricted missing structure must not prevent the crew rebuilding an ordinary wall after it.</summary>
    [Test]
    public async Task CrewSkipsRestrictedSnapshotStructures()
    {
        var config = Client.ResolveDependency<IConfigurationManager>();
        var echo = config.GetCVar(MonoCVars.AreaEchoEnabled);
        await Client.WaitPost(() => config.SetCVar(MonoCVars.AreaEchoEnabled, false));
        try
        {
            var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
            var normalPosition = new EntityCoordinates(deck, new Vector2(2.5f, 3.5f));
            EntityUid crew = default;
            var work = Server.System<WGCrewWorkSystem>();
            await Server.WaitAssertion(() =>
            {
                var restricted = SEntMan.SpawnAtPosition("WGTestRestrictedCrewWall",
                    new EntityCoordinates(deck, new Vector2(3.5f, 2.5f)));
                var normal = SEntMan.SpawnAtPosition("WallSolid", normalPosition);
                crew = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Deckhand,
                    new EntityCoordinates(deck, new Vector2(2.5f)), "restricted-repair")!.Value;
                SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
                SEntMan.DeleteEntity(restricted);
                SEntMan.DeleteEntity(normal);
                Assert.That(work.Advance(deck, "restricted-repair", WGCrewObjectiveKind.Repair, default), Is.EqualTo("working"));
                Assert.That(work.Destination(crew, out var destination), Is.True);
                Assert.That(destination, Is.EqualTo(normalPosition), "Skip the unsupported snapshot entry before assigning work.");
            });
            await WaitUntil(() =>
            {
                work.Perform(crew);
                return !work.Destination(crew, out _);
            }, 900, () => "The supported wall should rebuild through the ordinary SRD do-after.");
            await Server.WaitAssertion(() =>
            {
                Assert.That(work.Advance(deck, "restricted-repair", WGCrewObjectiveKind.Repair, default), Is.EqualTo("complete"));
                var data = SEntMan.GetComponent<ShipRepairDataComponent>(deck);
                var restricted = data.Chunks.Values.SelectMany(chunk => chunk.Entities.Values)
                    .Single(spec => data.EntityPalette[spec.ProtoIndex].Id == "WGTestRestrictedCrewWall");
                Assert.That(restricted.OriginalEntity == null || !SEntMan.TryGetEntity(restricted.OriginalEntity, out var restored)
                    || restored == null || !SEntMan.EntityExists(restored.Value), Is.True,
                    "Skipping a restricted target must not bypass its repair whitelist.");
            });
            await RunTicks(90);
        }
        finally
        {
            await Client.WaitPost(() => config.SetCVar(MonoCVars.AreaEchoEnabled, echo));
        }
    }

    /// <summary>Snapshot assignment also honors grid restrictions and disabled tool repair modes.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task CrewRepairHonorsGridAndToolRestrictions(bool gridRestricted)
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var wall = SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(deck, new Vector2(3.5f, 2.5f)));
            var crew = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(2.5f)), "repair-modes")!.Value;
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
            SEntMan.DeleteEntity(wall);
            if (gridRestricted)
                SEntMan.EnsureComponent<ShipRepairRestrictComponent>(deck).ToolWhitelist = new EntityWhitelist { Components = ["Actor"] };
            else
                SEntMan.GetComponent<ShipRepairToolComponent>(crew).EnableEntityRepair = false;
            var work = Server.System<WGCrewWorkSystem>();
            Assert.That(work.Advance(deck, "repair-modes", WGCrewObjectiveKind.Repair, default), Is.EqualTo("complete"));
            Assert.That(work.Destination(crew, out _), Is.False, "Do not queue an SRD interaction the normal tool will reject.");
        });
    }
}
