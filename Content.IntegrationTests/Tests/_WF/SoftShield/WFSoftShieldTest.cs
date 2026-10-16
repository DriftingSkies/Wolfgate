using System.Collections.Generic;
using Content.Shared._Mono.PersonalShield;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.SoftShield;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.SoftShield;

/// <summary>Checks the soft-shield tacsuits let brute through dampened, leave burns to the armour and stop bleeding.</summary>
[TestFixture]
public sealed class WFSoftShieldTest
{
    private static readonly string[] Suits = ["ClothingOuterHardsuitAshenElite", "ClothingOuterHardsuitNfsdExperimental"];

    [Test]
    [TestCaseSource(nameof(Suits))]
    public async Task SoftShieldDampensAndStopsBleeding(string suitId)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = entities.System<SharedMapSystem>();
        var inventory = entities.System<InventorySystem>();
        var toggle = entities.System<ItemToggleSystem>();
        var damageable = entities.System<DamageableSystem>();
        var bleedGate = entities.System<WolfmedBleedGateSystem>();

        EntityUid human = default;
        EntityUid suit = default;
        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            human = entities.SpawnEntity("MobHuman", coords);
            suit = entities.SpawnEntity(suitId, coords);

            Assert.That(entities.HasComponent<WFSoftShieldComponent>(suit), Is.True);
            Assert.That(inventory.TryEquip(human, suit, "outerClothing", silent: true, force: true), Is.True);
            Assert.That(toggle.TryActivate(suit, human, predicted: false), Is.True);
        });

        await server.WaitRunTicks(300);

        await server.WaitAssertion(() =>
        {
            var shield = entities.GetComponent<PersonalShieldComponent>(suit);
            Assert.That(shield.IsUp, Is.True, "The field should be up after spin-up.");
            Assert.That(bleedGate.CanStartBleeding(human), Is.False, "An active soft shield refuses bleeds.");

            var charge = shield.Runtime.Charge;
            var hit = new DamageSpecifier();
            hit.DamageDict["Piercing"] = FixedPoint2.New(20);
            var applied = damageable.TryChangeDamage(human, hit, origin: human);

            Assert.That(applied, Is.Not.Null, "A soft shield lets the hit through.");
            Assert.That(applied!.DamageDict.GetValueOrDefault("Piercing"), Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(applied.DamageDict.GetValueOrDefault("Piercing"), Is.LessThanOrEqualTo(FixedPoint2.New(8)),
                "At most 40% of the round should reach the wearer.");
            Assert.That(shield.Runtime.Charge, Is.EqualTo(charge - 12f).Within(0.5f), "The field soaks 60% of the round.");

            var burnCharge = shield.Runtime.Charge;
            var burn = new DamageSpecifier();
            burn.DamageDict["Heat"] = FixedPoint2.New(20);
            damageable.TryChangeDamage(human, burn, origin: human);
            Assert.That(shield.Runtime.Charge, Is.EqualTo(burnCharge).Within(0.01f), "Burns pass the field untouched.");

            var query = entities.EntityQueryEnumerator<WoundBleedingComponent, WoundComponent>();
            while (query.MoveNext(out _, out _, out var wound))
            {
                Assert.That(entities.GetComponent<BodyPartComponent>(wound.HoldingPart).Body, Is.Not.EqualTo(human),
                    "No bleed opens under an active soft shield.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
