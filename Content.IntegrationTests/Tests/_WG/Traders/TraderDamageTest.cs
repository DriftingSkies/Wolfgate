#nullable enable
using Content.Server._WG.Traders;
using Content.Server.Body.Components;
using Content.Server.NPC.HTN;
using Content.Shared._WG.Traders;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Traders;

/// <summary>
/// Covers what makes a wandering trader killable and what he does when hurt.
/// </summary>
[TestFixture]
[TestOf(typeof(TraderFleeSystem))]
public sealed class TraderDamageTest
{
    private static readonly ProtoId<DamageTypePrototype> BluntProto = "Blunt";

    private const string WandererProto = "WGTraderWanderer";
    private const string StationProto = "WGTraderFuelTechnician";
    private const string AttackerProto = "MobHuman";

    /// <summary>
    /// A wound host only dies through its blood, its breath and its organs, so a killable trader keeps all
    /// three, and the legs and brain to run with. A station trader stays rooted and invulnerable.
    /// </summary>
    [Test]
    public async Task KillableTraderKeepsItsBody()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var gridUid = map.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            var wanderer = entMan.SpawnEntity(WandererProto, new EntityCoordinates(gridUid, 0.5f, 0.5f));
            var station = entMan.SpawnEntity(StationProto, new EntityCoordinates(gridUid, 2.5f, 0.5f));

            Assert.That(entMan.HasComponent<GodmodeComponent>(wanderer), Is.False,
                "A killable trader should not be invulnerable.");
            Assert.That(entMan.HasComponent<BloodstreamComponent>(wanderer), Is.True,
                "A killable trader should be able to bleed.");
            Assert.That(entMan.HasComponent<RespiratorComponent>(wanderer), Is.True,
                "A killable trader should be able to suffocate.");
            Assert.That(entMan.HasComponent<InputMoverComponent>(wanderer), Is.True,
                "A killable trader should be able to move.");
            Assert.That(entMan.HasComponent<HTNComponent>(wanderer), Is.True,
                "A killable trader should have a brain to run with.");
            Assert.That(entMan.GetComponent<PhysicsComponent>(wanderer).BodyType, Is.Not.EqualTo(BodyType.Static),
                "A killable trader should not be rooted in place.");

            Assert.That(entMan.HasComponent<GodmodeComponent>(station), Is.True,
                "A station trader should stay invulnerable.");
            Assert.That(entMan.HasComponent<BloodstreamComponent>(station), Is.False,
                "A station trader should stay bloodless.");
            Assert.That(entMan.GetComponent<PhysicsComponent>(station).BodyType, Is.EqualTo(BodyType.Static),
                "A station trader should stay rooted in place.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Hurt by someone, a killable trader remembers them, runs from them and will not trade with them again.
    /// A station trader, who cannot be hurt, learns nothing.
    /// </summary>
    [Test]
    public async Task HurtTraderRefusesItsAttacker()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var gridUid = map.Grid.Owner;

        var wanderer = EntityUid.Invalid;
        var station = EntityUid.Invalid;
        var attacker = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            wanderer = entMan.SpawnEntity(WandererProto, new EntityCoordinates(gridUid, 0.5f, 0.5f));
            station = entMan.SpawnEntity(StationProto, new EntityCoordinates(gridUid, 2.5f, 0.5f));
            attacker = entMan.SpawnEntity(AttackerProto, new EntityCoordinates(gridUid, 1.5f, 0.5f));

            var damageable = entMan.System<DamageableSystem>();
            var blunt = protoMan.Index<DamageTypePrototype>(BluntProto);
            damageable.TryChangeDamage(wanderer, new DamageSpecifier(blunt, 5), origin: attacker);
            damageable.TryChangeDamage(station, new DamageSpecifier(blunt, 5), origin: attacker);
        });

        await server.WaitAssertion(() =>
        {
            var traderSys = entMan.System<TraderSystem>();

            Assert.That(entMan.TryGetComponent(wanderer, out TraderFleeComponent? flee), Is.True,
                "The hurt trader should have noted his attacker.");
            Assert.That(flee!.Attackers, Does.Contain(attacker), "The attacker should be remembered.");
            Assert.That(flee.Threat, Is.EqualTo(attacker), "The attacker should be who he runs from.");

            var comp = entMan.GetComponent<TraderComponent>(wanderer);
            Assert.That(traderSys.TryStartConversation((wanderer, comp), attacker), Is.False,
                "The trader should refuse to talk to the one who hurt him.");

            Assert.That(entMan.HasComponent<TraderFleeComponent>(station), Is.False,
                "A trader who cannot be hurt has nobody to run from.");
        });

        await pair.CleanReturnAsync();
    }
}
