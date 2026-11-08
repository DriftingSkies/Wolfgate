#nullable enable
using Content.Server._WF.StructureIntegrity;
using Content.Server.Destructible;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WF.StructureIntegrity;

/// <summary>An anchored wall reports its remaining integrity out of the damage that breaks it; an unanchored one doesn't.</summary>
[TestFixture]
public sealed class StructureIntegrityTest
{
    [Test]
    public async Task WallReportsIntegrity()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var integrity = server.System<StructureIntegritySystem>();
            var destructible = server.System<DestructibleSystem>();
            var damageable = server.System<DamageableSystem>();
            var xforms = server.System<SharedTransformSystem>();
            var examine = server.System<ExamineSystemShared>();

            var wall = em.SpawnEntity("WallSolid", map.GridCoords);
            var ent = new Entity<DestructibleComponent>(wall, em.GetComponent<DestructibleComponent>(wall));
            Assert.That(em.GetComponent<TransformComponent>(wall).Anchored, "The wall should spawn anchored.");

            var destroyedAt = destructible.DestroyedAt(wall).Int();
            Assert.That(integrity.TryGetIntegrity(ent, out var current, out var max));
            Assert.That(max, Is.EqualTo(destroyedAt));
            Assert.That(current, Is.EqualTo(max));

            var examiner = em.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(examine.GetExamineText(wall, examiner).ToMarkup(), Does.Contain($"[color=lime]{max}/{max}[/color]"));

            damageable.TryChangeDamage(wall, new DamageSpecifier { DamageDict = { ["Blunt"] = 37 } }, ignoreResistances: true);
            Assert.That(integrity.TryGetIntegrity(ent, out current, out _));
            Assert.That(current, Is.EqualTo(max - 37));

            var heavy = max - 37 - max / 4;
            damageable.TryChangeDamage(wall, new DamageSpecifier { DamageDict = { ["Blunt"] = heavy } }, ignoreResistances: true);
            Assert.That(examine.GetExamineText(wall, examiner).ToMarkup(), Does.Contain($"[color=red]{max / 4}/{max}[/color]"),
                "A badly damaged wall should show red.");

            xforms.Unanchor(wall);
            Assert.That(integrity.TryGetIntegrity(ent, out _, out _), Is.False, "An unanchored structure shows no integrity.");
            Assert.That(examine.GetExamineText(wall, examiner).ToMarkup(), Does.Not.Contain("/" + max),
                "An unanchored structure shows no integrity line.");

            Assert.That(server.ResolveDependency<ILocalizationManager>().HasString("wf-structure-integrity-examine"));
        });

        await pair.CleanReturnAsync();
    }
}
