using System.Collections.Generic;
using System.Linq;
using Content.Server.Wagging;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Species;

/// <summary>Wagging swaps a tail for its animated version on the client, leaving none of the still tail behind.</summary>
[TestFixture]
public sealed class WaggingTailLayersTest
{
    [TestCase("MobReptilian", "LizardTailSmooth", "LizardTailSmoothAnimated", false)]
    [TestCase("MobVulpkanin", "VulpTailFox", "VulpTailFoxAnimated", false)]
    [TestCase("MobVulpkanin", "FoxTail", "FoxTailAnimated", false)]
    [TestCase("MobVulpkanin", "VulpTail", "VulpTailAnimated", false)]
    [TestCase("MobVulpkanin", "VulpTail", "VulpTailAnimated", true)]
    public async Task WaggingLeavesNoStillTail(string mob, string Still, string Wag, bool fromProfile)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEnt = server.EntMan;
        var cEnt = client.EntMan;
        var markings = server.ResolveDependency<MarkingManager>();

        var stillStates = markings.Markings[Still].Sprites.OfType<Robust.Shared.Utility.SpriteSpecifier.Rsi>().Select(s => s.RsiState).ToHashSet();
        var wagStates = markings.Markings[Wag].Sprites.OfType<Robust.Shared.Utility.SpriteSpecifier.Rsi>().Select(s => s.RsiState).ToHashSet();

        var map = await pair.CreateTestMap();
        EntityUid lizard = default;
        await server.WaitPost(() =>
        {
            lizard = sEnt.SpawnEntity(mob, map.GridCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, lizard);
            var humanoid = sEnt.GetComponent<HumanoidAppearanceComponent>(lizard);
            var appearance = sEnt.System<Content.Shared.Humanoid.SharedHumanoidAppearanceSystem>();
            if (fromProfile)
            {
                // A spawned character: the tail comes from the profile and the species fills in what is missing.
                var profile = Content.Shared.Preferences.HumanoidCharacterProfile.DefaultWithSpecies(humanoid.Species);
                var tail = new Marking(Still, markings.Markings[Still].Sprites.Count);
                profile = profile.WithCharacterAppearance(profile.Appearance.WithMarkings(new List<Marking> { tail }));
                appearance.LoadProfile(lizard, profile, humanoid);
                Assert.That(humanoid.MarkingSet.Markings[MarkingCategories.Tail].Select(m => m.MarkingId), Is.EqualTo(new[] { Still }),
                    "the loaded character should wear exactly its profile's tail");
            }
            else
            {
                humanoid.MarkingSet.RemoveCategory(MarkingCategories.Tail);
                appearance.AddMarking(lizard, Still, Robust.Shared.Maths.Color.Orange, forced: true);
            }
        });
        await pair.RunTicksSync(10);

        // Counts the drawn layers showing each set of tail states, by the layers themselves rather than the map.
        (int still, int wag) Count()
        {
            var sprite = cEnt.GetComponent<SpriteComponent>(cEnt.GetEntity(sEnt.GetNetEntity(lizard)));
            int still = 0, wag = 0;
            foreach (var layer in sprite.AllLayers)
            {
                var state = layer.RsiState.Name;
                if (state == null)
                    continue;
                if (stillStates.Contains(state))
                    still++;
                if (wagStates.Contains(state))
                    wag++;
            }
            return (still, wag);
        }

        // Every drawn layer's state, so a leftover from any RSI shows up, not just the two known tail sets.
        List<string> Layers()
        {
            var sprite = cEnt.GetComponent<SpriteComponent>(cEnt.GetEntity(sEnt.GetNetEntity(lizard)));
            return sprite.AllLayers.Select(l => $"{l.ActualRsi?.Path}:{l.RsiState.Name}").OrderBy(s => s).ToList();
        }

        async Task Toggle(int times)
        {
            await server.WaitPost(() =>
            {
                for (var i = 0; i < times; i++)
                    sEnt.System<WaggingSystem>().TryToggleWagging(lizard);
            });
            await pair.RunTicksSync(10);
        }

        await client.WaitAssertion(() => Assert.That(Count(), Is.EqualTo((stillStates.Count, 0)), "the still tail is drawn once"));
        List<string> before = default!;
        await client.WaitPost(() => before = Layers());
        await Toggle(2);
        await client.WaitAssertion(() => Assert.That(Layers(), Is.EqualTo(before), "a wag on and off should leave exactly the layers it started with"));

        await Toggle(1);
        await client.WaitAssertion(() => Assert.That(Count(), Is.EqualTo((0, wagStates.Count)), "wagging leaves a still tail behind"));

        await Toggle(1);
        await client.WaitAssertion(() => Assert.That(Count(), Is.EqualTo((stillStates.Count, 0)), "stopping leaves a wagging tail behind"));

        // Clothing that hides or covers the tail rebuilds the tail layers on its own path.
        await server.WaitPost(() =>
        {
            var inv = sEnt.System<Content.Server.Inventory.ServerInventorySystem>();
            inv.SpawnItemInSlot(lizard, "jumpsuit", "ClothingUniformJumpsuitChef", silent: true, force: true);
            inv.SpawnItemInSlot(lizard, "outerClothing", "ClothingOuterApronChef", silent: true, force: true);
        });
        await pair.RunTicksSync(10);
        await Toggle(1);
        await client.WaitAssertion(() => Assert.That(Count(), Is.EqualTo((0, wagStates.Count)), "wagging in clothes leaves a still tail behind"));
        await Toggle(1);
        await client.WaitAssertion(() => Assert.That(Count(), Is.EqualTo((stillStates.Count, 0)), "stopping in clothes leaves a wagging tail behind"));

        // Several toggles land in one state.
        await Toggle(3);
        await client.WaitAssertion(() => Assert.That(Count(), Is.EqualTo((0, wagStates.Count)), "quick toggles leave a still tail behind"));

        // The client forgets which markings it last drew (as when a layer is drawn outside a full rebuild); a wag must still take the still tail away.
        await Toggle(1);
        await client.WaitAssertion(() => Assert.That(Count(), Is.EqualTo((stillStates.Count, 0))));
        await client.WaitPost(() => cEnt.GetComponent<HumanoidAppearanceComponent>(cEnt.GetEntity(sEnt.GetNetEntity(lizard))).ClientOldMarkings.Clear());
        await Toggle(1);
        await client.WaitAssertion(() => Assert.That(Count(), Is.EqualTo((0, wagStates.Count)), "a forgotten still tail stayed under the wagging one"));

        // Toggled while the client can't see it, then seen again.
        EntityUid away = default;
        await server.WaitPost(() =>
        {
            var other = sEnt.System<Robust.Shared.GameObjects.SharedMapSystem>().CreateMap(out var otherMap);
            away = sEnt.SpawnEntity(null, new Robust.Shared.Map.MapCoordinates(0, 0, otherMap));
            server.PlayerMan.SetAttachedEntity(pair.Player!, away);
        });
        await pair.RunTicksSync(10);
        await Toggle(1);
        await Toggle(1);
        await Toggle(1);
        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, lizard));
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() => Assert.That(Count(), Is.EqualTo((stillStates.Count, 0)), "toggling out of view leaves a tail behind"));

        await server.WaitPost(() =>
        {
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            sEnt.DeleteEntity(lizard);
        });
        await pair.CleanReturnAsync();
    }
}
