using System.Collections.Generic;
using System.Linq;
using Content.Server._WG.Genitals;
using Content.Shared._WG.Genitals;
using Content.Shared._WG.Genitals.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Genitals;

/// <summary>The runtime genital slots never cross-match another organ slot, and each organ prototype fits its own slot.</summary>
[TestFixture]
[TestOf(typeof(GenitalOrganSystem))]
public sealed class GenitalOrganSlotTest
{
    /// <summary>No wg_genital_* id is a substring of any organ slot id in a body, part or organ prototype, or the reverse.</summary>
    /// <remarks>Organ attach matching is slotId.Contains("body_organ_slot_" + organ.SlotId) (SharedBodySystem.Parts.cs).</remarks>
    [Test]
    public async Task NoSubstringCollisionTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var genitalIds = GenitalOrganSystem.Slots.Select(GenitalOrganSystem.SlotId).ToList();
            var others = new List<(string Id, string Source)>();

            foreach (var body in proto.EnumeratePrototypes<BodyPrototype>())
            {
                foreach (var (slotName, slot) in body.Slots)
                {
                    foreach (var organSlot in slot.Organs.Keys)
                    {
                        others.Add((organSlot, $"body {body.ID}, slot {slotName}"));
                    }
                }
            }

            foreach (var entity in proto.EnumeratePrototypes<EntityPrototype>())
            {
                if (entity.TryComp<BodyPartComponent>(out var part, factory))
                {
                    foreach (var organSlot in part.Organs.Keys)
                    {
                        others.Add((organSlot, $"part {entity.ID}"));
                    }
                }

                // The genital organs fill the genital slots by design.
                if (entity.TryComp<OrganComponent>(out var organ, factory)
                    && !entity.TryComp<GenitalOrganComponent>(out _, factory))
                    others.Add((organ.SlotId, $"organ {entity.ID}"));
            }

            Assert.Multiple(() =>
            {
                foreach (var (id, source) in others)
                {
                    // An organ without a slot id matches no slot by name.
                    if (string.IsNullOrEmpty(id))
                        continue;

                    foreach (var genital in genitalIds)
                    {
                        Assert.That(id.Contains(genital), Is.False, $"{source}: slot id '{id}' contains '{genital}'.");
                        Assert.That(genital.Contains(id), Is.False, $"{source}: slot id '{id}' is part of '{genital}'.");
                    }
                }

                foreach (var a in genitalIds)
                {
                    foreach (var b in genitalIds)
                    {
                        if (a != b)
                            Assert.That(a.Contains(b), Is.False, $"'{a}' contains '{b}'.");
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Each organ prototype fills its own slot, carries its slot marker and has the neutral name for every viewer.</summary>
    [Test]
    public async Task OrganPrototypesTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var slot in GenitalOrganSystem.Slots)
                {
                    var id = GenitalOrganSystem.OrganPrototype(slot);
                    Assert.That(proto.TryIndex(id, out var entity), $"Missing organ prototype {id}.");
                    if (entity == null)
                        continue;

                    Assert.That(entity.Name, Is.EqualTo("organ tissue"), $"{id} name");
                    Assert.That(entity.TryComp<OrganComponent>(out var organ, factory), $"{id} has no Organ.");
                    Assert.That(organ?.SlotId, Is.EqualTo(GenitalOrganSystem.SlotId(slot)), $"{id} slotId");
                    Assert.That(entity.TryComp<GenitalOrganComponent>(out var genital, factory), $"{id} has no GenitalOrgan.");
                    Assert.That(genital?.Slot, Is.EqualTo(slot), $"{id} slot");
                    Assert.That(HasMarker(entity, slot, factory), $"{id} lacks its slot marker.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Surgery conditions select organs by these marker components.</summary>
    private static bool HasMarker(EntityPrototype entity, GenitalSlot slot, IComponentFactory factory)
    {
        return slot switch
        {
            GenitalSlot.Penis => entity.TryComp<PenisOrganComponent>(out _, factory),
            GenitalSlot.Testicles => entity.TryComp<TesticlesOrganComponent>(out _, factory),
            GenitalSlot.Vagina => entity.TryComp<VaginaOrganComponent>(out _, factory),
            GenitalSlot.Womb => entity.TryComp<WombOrganComponent>(out _, factory),
            GenitalSlot.Breasts => entity.TryComp<BreastsOrganComponent>(out _, factory),
            _ => false,
        };
    }
}
