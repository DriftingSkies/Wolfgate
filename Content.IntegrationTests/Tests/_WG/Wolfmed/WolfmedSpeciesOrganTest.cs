#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Body.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WG.Wolfmed.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.Humanoid.Prototypes;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Wolfmed;

/// <summary>
/// "Some species not coming with a full set of organs." The analyzer, surgery and organ damage only see an organ that
/// carries Wolfmed data, and most species' livers, stomachs and kidneys (the shared animal organs, Skrell, Protogen,
/// Synth, Arachnid) carried none, so a vulpkanin scanned with no liver or stomach. Every organ in a vital slot of every
/// species that is a wound host now carries it, and the goblin's lungs sit in their slot.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedOrganComponent))]
public sealed class WolfmedSpeciesOrganTest : WolfmedGameTest
{
    private static readonly HashSet<string> VitalSlots = new()
    {
        "brain", "eyes", "heart", "lungs", "liver", "kidneys", "stomach", "posbrain", "pump", "core",
    };

    /// <summary>
    /// Each species against its own body prototype: every organ the prototype lists was built into the body, each one
    /// in a vital slot says so itself (the goblin's lungs sat in the lungs slot calling it nothing, which is what
    /// surgery and the analyzer read), and each of those carries Wolfmed data.
    /// </summary>
    [Test]
    public async Task EverySpeciesOrganCarriesWolfmedDataTest()
    {
        var map = await CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.System<SharedBodySystem>();
            var bare = new List<string>();
            var unslotted = new List<string>();
            var missing = new List<string>();
            var misplaced = new List<string>();
            foreach (var species in SProtoMan.EnumeratePrototypes<SpeciesPrototype>())
            {
                var mob = SEntMan.SpawnEntity(species.Prototype, map.GridCoords);
                if (!SEntMan.HasComponent<WoundHostComponent>(mob))
                {
                    SEntMan.DeleteEntity(mob);
                    continue;
                }

                var built = new List<(string? Prototype, string Slot)>();
                foreach (var (organ, component) in body.GetBodyOrgans(mob))
                {
                    var id = SEntMan.GetComponent<MetaDataComponent>(organ).EntityPrototype?.ID;
                    built.Add((id, component.SlotId));
                    if (VitalSlots.Contains(component.SlotId) && !SEntMan.HasComponent<WolfmedOrganComponent>(organ))
                        bare.Add($"{species.ID}: {component.SlotId} ({id})");

                    var vital = SEntMan.HasComponent<LungComponent>(organ) || SEntMan.HasComponent<HeartComponent>(organ) ||
                                SEntMan.HasComponent<BrainComponent>(organ) || SEntMan.HasComponent<StomachComponent>(organ);
                    if (vital && string.IsNullOrEmpty(component.SlotId))
                        unslotted.Add($"{species.ID}: {id}");
                }

                // What the body prototype says the species is built with, slot by slot.
                if (SEntMan.GetComponent<BodyComponent>(mob).Prototype is { } prototype)
                {
                    foreach (var (slot, organ) in SProtoMan.Index(prototype).Slots.Values.SelectMany(part => part.Organs))
                    {
                        var found = built.Where(entry => entry.Prototype == organ).ToList();
                        if (found.Count == 0)
                            missing.Add($"{species.ID}: {slot} ({organ})");
                        else if (VitalSlots.Contains(slot) && found.All(entry => entry.Slot != slot))
                            misplaced.Add($"{species.ID}: {organ} is in {slot} and says \"{found[0].Slot}\"");
                    }
                }

                SEntMan.DeleteEntity(mob);
            }

            Assert.Multiple(() =>
            {
                Assert.That(bare, Is.Empty, "organs Wolfmed cannot see.");
                Assert.That(unslotted, Is.Empty, "vital organs outside any slot.");
                Assert.That(missing, Is.Empty, "organs a body prototype lists that the body was built without.");
                Assert.That(misplaced, Is.Empty, "vital organs whose own slot is not the one they sit in.");
            });
        });
    }

    /// <summary>
    /// What a destroyed organ leaves behind is a wound its part can take. A synth's chassis parts take no internal
    /// bleeding, so the organic organ data on its lungs, stomach, liver and kidneys left nothing at all.
    /// </summary>
    [Test]
    public async Task EveryDestructionWoundFitsItsPartTest()
    {
        var map = await CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.System<SharedBodySystem>();
            var wounds = SEntMan.System<WoundSystem>();
            var refused = new List<string>();
            foreach (var species in SProtoMan.EnumeratePrototypes<SpeciesPrototype>())
            {
                var mob = SEntMan.SpawnEntity(species.Prototype, map.GridCoords);
                if (SEntMan.HasComponent<WoundHostComponent>(mob))
                {
                    foreach (var (organ, component) in body.GetBodyOrgans(mob))
                    {
                        if (!SEntMan.TryGetComponent(organ, out WolfmedOrganComponent? health) ||
                            health.DestructionWound is not { } wound)
                            continue;

                        var part = SEntMan.GetComponent<TransformComponent>(organ).ParentUid;
                        if (!wounds.CanCreateWound(part, wound))
                            refused.Add($"{species.ID}: {component.SlotId} leaves {wound}");
                    }
                }

                SEntMan.DeleteEntity(mob);
            }

            Assert.That(refused, Is.Empty, "destruction wounds the organ's own part refuses.");
        });
    }
}
