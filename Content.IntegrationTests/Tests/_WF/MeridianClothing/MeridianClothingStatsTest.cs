#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server.Temperature.Components;
using Content.Shared.Clothing.Components;
using Content.Shared.Electrocution;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.MeridianClothing;

/// <summary>Ported clothing does what it did on Meridian Rift, and nothing that stacks into immunity here.</summary>
[TestFixture]
public sealed class MeridianClothingStatsTest
{
    /// <summary>Nova's combat gloves plus are insulated there, though their typepath isn't a combat glove's.</summary>
    private const string InsulatedGloves = "WFMeridianClothingGlovesKazaRukCombatglovesplus";

    [Test]
    public async Task StatsCarryOverTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var factory = server.ResolveDependency<IComponentFactory>();
        var stacking = new List<string>();
        var insulated = false;

        await server.WaitPost(() =>
        {
            insulated = server.ProtoMan.Index<EntityPrototype>(InsulatedGloves).TryGetComponent<InsulatedComponent>(out _, factory);

            foreach (var proto in server.ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || !proto.ID.StartsWith("WFMeridianClothing") ||
                    !proto.TryGetComponent<ClothingComponent>(out var clothing, factory) ||
                    !proto.TryGetComponent<TemperatureProtectionComponent>(out _, factory))
                    continue;

                // Coefficients multiply across worn items, so only suits and helmets may carry them.
                if ((clothing.Slots & (SlotFlags.OUTERCLOTHING | SlotFlags.HEAD)) == 0)
                    stacking.Add(proto.ID);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(insulated, Is.True, $"{InsulatedGloves} should be insulated");
            Assert.That(stacking, Is.Empty, "temperature protection outside the suit and helmet slots: " + string.Join(", ", stacking.Take(20)));
        });

        await pair.CleanReturnAsync();
    }
}
