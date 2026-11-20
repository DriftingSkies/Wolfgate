#nullable enable
using System.Linq;
using Content.Server._Mono.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._NF.BountyContracts;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Research.Prototypes;
using Content.Shared.Radio.Components;
using Content.Shared.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Feedback;

/// <summary>Reports from the 2026-11-18 feedback export.</summary>
[TestFixture]
public sealed class WGFeedbackBatch1118Test
{
    /// <summary>#1063: MMC headsets talk on the company channel by default; #1008: TSFMC command gets shortband.</summary>
    [TestCase("ClothingHeadsetMieyo", "Mieyo", null)]
    [TestCase("ClothingHeadsetMieyoCorpsec", "Mieyo", null)]
    [TestCase("ClothingHeadsetAltTsfmcCommander", null, "Traffic")]
    public async Task HeadsetChannels(string headset, string? defaultChannel, string? channel)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var uid = server.EntMan.SpawnEntity(headset, map.GridCoords);
            var holder = server.EntMan.GetComponent<EncryptionKeyHolderComponent>(uid);
            if (defaultChannel != null)
                Assert.That(holder.DefaultChannel, Is.EqualTo(defaultChannel));
            if (channel != null)
                Assert.That(holder.Channels, Does.Contain(channel));
            server.EntMan.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>#1090, #1091: a Remade can't work a PDA, but other interfaces stay open to it.</summary>
    [Test]
    public async Task RemadeCannotUsePda()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var remade = server.EntMan.SpawnEntity("WGRemadeExploderEnhanced", map.GridCoords);
            var pda = server.EntMan.SpawnEntity("PassengerPDA", map.GridCoords);
            var paper = server.EntMan.SpawnEntity("Paper", map.GridCoords);

            var onPda = new UserOpenActivatableUIAttemptEvent(remade, pda);
            server.EntMan.EventBus.RaiseLocalEvent(remade, onPda);
            Assert.That(onPda.Cancelled, Is.True);

            var onPaper = new UserOpenActivatableUIAttemptEvent(remade, paper);
            server.EntMan.EventBus.RaiseLocalEvent(remade, onPaper);
            Assert.That(onPaper.Cancelled, Is.False);

            server.EntMan.DeleteEntity(remade);
            server.EntMan.DeleteEntity(pda);
            server.EntMan.DeleteEntity(paper);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>#1053: Caelestinus command, such as the Overseer, may post and remove command bounties.</summary>
    [Test]
    public async Task CommandBountiesTakeCaelestinusCommand()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var command = server.ProtoMan.Index<BountyContractCollectionPrototype>("Command");
            Assert.That(command.WriteAccess.Select(a => a.Id), Does.Contain("CaelestinusCommand"));
            Assert.That(command.DeleteAccess.Select(a => a.Id), Does.Contain("CaelestinusCommand"));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>#1062: super components can be assembled.</summary>
    [Test]
    public async Task SuperComponentsHaveARecipe()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var recipes = server.ProtoMan.EnumeratePrototypes<LatheRecipePrototype>()
                .Where(r => r.Result?.Id == "ComponentBrickEconomy3")
                .Select(r => r.ID)
                .ToHashSet();
            Assert.That(recipes, Is.Not.Empty);
            Assert.That(server.ProtoMan.EnumeratePrototypes<LatheRecipePackPrototype>()
                .Any(pack => pack.Recipes.Any(r => recipes.Contains(r.Id))), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>#1061: a fitting queued for deletion, as a hull mod leaves the one it replaced, is not in the repair snapshot.</summary>
    [Test]
    public async Task RepairSnapshotSkipsQueuedDeletions()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var kept = server.EntMan.SpawnEntity("MachineThermobath", map.GridCoords);
            var replaced = server.EntMan.SpawnEntity("KitchenReagentGrinder", map.GridCoords);
            Assert.That(server.EntMan.GetComponent<TransformComponent>(kept).Anchored, Is.True);
            Assert.That(server.EntMan.GetComponent<TransformComponent>(replaced).Anchored, Is.True);

            server.EntMan.QueueDeleteEntity(replaced);
            server.System<ShipRepairSystem>().GenerateRepairData(map.Grid);

            var palette = server.EntMan.GetComponent<ShipRepairDataComponent>(map.Grid).EntityPalette.Select(p => p.Id).ToList();
            Assert.That(palette, Does.Contain("MachineThermobath"));
            Assert.That(palette, Does.Not.Contain("KitchenReagentGrinder"));
        });

        await pair.CleanReturnAsync();
    }
}
