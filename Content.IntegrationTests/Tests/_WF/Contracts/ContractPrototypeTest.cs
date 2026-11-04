#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.Contracts;
using Content.Shared._WF.Contracts;
using Content.Shared._WF.Encounters;
using Content.Shared.Stacks;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Contracts;

/// <summary>
/// Every contract template can be posted as written: its strings exist, its goods resolve and have a name, its bounty
/// targets are manual encounters with no warrant of their own, and its wrecks and cargo exist. Every contractor has work
/// and every category but Planetary is offered somewhere.
/// </summary>
[TestFixture]
[TestOf(typeof(WFContractPrototype))]
public sealed class ContractPrototypeTest
{
    [Test]
    public async Task EveryContractTemplateIsValid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var loc = server.ResolveDependency<ILocalizationManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var resources = server.ResolveDependency<IResourceManager>();

        await server.WaitAssertion(() =>
        {
            var problems = new List<string>();
            void Check(bool ok, string id, string what)
            {
                if (!ok)
                    problems.Add($"{id}: {what}");
            }

            var contracts = protos.EnumeratePrototypes<WFContractPrototype>().ToList();
            Assert.That(contracts, Has.Count.GreaterThanOrEqualTo(40), "The board should have plenty of work.");

            foreach (var contractor in protos.EnumeratePrototypes<WFContractorPrototype>())
            {
                Check(loc.HasString(contractor.Name) && loc.HasString(contractor.Description), contractor.ID, "missing strings");
                Check(contractor.Stations.Count > 0, contractor.ID, "no stations");
                Check(contracts.Any(c => c.Contractor == contractor.ID), contractor.ID, "no contracts");
                Check(contractor.Companies.All(c => protos.HasIndex(c)), contractor.ID, "unknown company");
                Check(contractor.Drift >= 0, contractor.ID, "negative drift");
                Check(contractor.Board == WFContractBoards.Central || protos.EnumeratePrototypes<EntityPrototype>().Any(e =>
                        e.TryComp<WFContractBoardComponent>(out var board, factory) && board.Board == contractor.Board),
                    contractor.ID, $"no board entity posts for board {contractor.Board}");
            }

            foreach (var faction in protos.EnumeratePrototypes<WFContractFactionPrototype>())
            {
                Check(faction.Companies.Count > 0 && faction.Companies.All(c => protos.HasIndex(c)), faction.ID, "unknown or no companies");
            }

            foreach (var category in System.Enum.GetValues<WFContractCategory>())
            {
                if (category != WFContractCategory.Planetary)
                    Check(contracts.Any(c => c.Category == category), category.ToString(), "nothing offered in this category");
            }

            foreach (var contract in contracts)
            {
                var id = contract.ID;
                Check(loc.HasString(contract.Title) && loc.HasString(contract.Briefing), id, "missing title or briefing");
                Check(contract.Tier is >= 1 and <= 5, id, "tier out of 1-5");
                Check(contract.Reward.X > 0 && contract.Reward.X <= contract.Reward.Y, id, "bad reward range");
                Check(contract.Minutes > 0 && contract.Deposit >= 0, id, "bad time or deposit");
                Check(contract.Tier > 1 || contract.Deposit <= 500, id, "a tier 1 contract should cost a new player little or nothing");
                Check(contract.Harms.Keys.All(h => protos.HasIndex(h) && h != contract.Contractor), id, "harms an unknown contractor or its own");
                Check(contract.Credits == 0 || contract.CreditEntity is { } credit && protos.HasIndex<EntityPrototype>(credit), id,
                    "credits without a credit entity");

                switch (contract.Objective)
                {
                    case WFContractCourier courier:
                        Check(protos.HasIndex<EntityPrototype>(courier.Package), id, $"unknown package {courier.Package}");
                        Check(courier.Count.X >= 1 && courier.Count.X <= courier.Count.Y, id, "bad package count");
                        Check(courier.Destinations.All(d => protos.HasIndex(d)), id, "unknown destination contractor");
                        Check(courier.From != courier.To || courier.From == WFContractEnd.Other, id, "goes nowhere");
                        Check(contract.Category is WFContractCategory.Delivery or WFContractCategory.Hauling, id, "courier outside Delivery or Hauling");
                        break;
                    case WFContractSupply supply:
                        Check(supply.Goods.Count > 0 && supply.Pick <= supply.Goods.Count, id, "bad goods list");
                        Check(protos.HasIndex<EntityPrototype>(supply.Crate), id, "unknown crate");
                        foreach (var goods in supply.Goods)
                        {
                            var kinds = new[] { goods.Stack != null, goods.Entity != null, goods.Tag != null, goods.Component != null }.Count(k => k);
                            Check(kinds == 1, id, "a goods line needs exactly one of stack, entity, tag or component");
                            Check(goods.Count.X >= 1 && goods.Count.X <= goods.Count.Y, id, "bad goods count");
                            Check(goods.Stack == null || protos.HasIndex<StackPrototype>(goods.Stack.Value), id, $"unknown stack {goods.Stack}");
                            Check(goods.Entity == null || protos.HasIndex<EntityPrototype>(goods.Entity.Value), id, $"unknown entity {goods.Entity}");
                            Check(goods.Component == null || factory.TryGetRegistration(goods.Component, out _), id, $"unknown component {goods.Component}");
                            Check(goods.Stack != null || goods.Entity != null || goods.Name != null && goods.Value != null, id,
                                "a tag or component line needs a name and a value");
                            Check(goods.Name == null || loc.HasString(goods.Name.Value), id, "missing goods name string");
                        }
                        break;
                    case WFContractBounty bounty:
                        Check(bounty.Encounters.Count > 0, id, "no targets");
                        Check(bounty.MinDistance > 0 && bounty.MinDistance <= bounty.MaxDistance, id, "bad range");
                        foreach (var target in bounty.Encounters)
                        {
                            if (!protos.TryIndex(target, out var encounter))
                            {
                                Check(false, id, $"unknown target {target}");
                                continue;
                            }

                            Check(encounter.Start == WFEncounterStart.Manual, id, $"{target} could be scheduled on its own");
                            Check(encounter.Bounty == null, id, $"{target} posts a warrant and would pay twice");
                            Check(encounter.Duration >= contract.Minutes * 60f, id, $"{target} ends before the contract's clock");
                        }
                        break;
                    case WFContractEscort escort:
                        Check(escort.Encounters.Count > 0, id, "no escort runs");
                        Check(escort.Destinations.All(d => protos.HasIndex(d)), id, "unknown destination contractor");
                        foreach (var run in escort.Encounters)
                        {
                            if (!protos.TryIndex(run, out var encounter))
                            {
                                Check(false, id, $"unknown escort run {run}");
                                continue;
                            }

                            Check(encounter.Start == WFEncounterStart.Manual && encounter.Bounty == null, id, $"{run} must be manual with no warrant");
                            Check(encounter.Route != null && encounter.StartRadius > 0f, id, $"{run} needs a route and a rendezvous");
                            Check(encounter.Ships.Any(s => s.Key == escort.Ship && s.FlyRoute), id, $"{run} has no ship '{escort.Ship}' flying the route");
                            Check(encounter.Duration >= contract.Minutes * 60f, id, $"{run} ends before the contract's clock");
                        }
                        break;
                    case WFContractRecovery recovery:
                        Check(protos.HasIndex<EntityPrototype>(recovery.Item), id, "unknown item");
                        Check(recovery.To != WFContractEnd.Other, id, "a recovery goes to its contractor or the board");
                        Check(recovery.Wrecks.Count > 0, id, "no wrecks");
                        foreach (var wreck in recovery.Wrecks)
                        {
                            Check(resources.ContentFileExists(wreck), id, $"missing wreck {wreck}");
                        }
                        break;
                    default:
                        Check(false, id, "objective the board cannot run");
                        break;
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A crew's slots double the crew they need each, starting at one; standing drifts toward neutral.</summary>
    [Test]
    public void SlotsAndDrift()
    {
        Assert.That(new[] { 1, 2, 3, 4, 7, 8, 16 }.Select(WFContractSystem.SlotsFor), Is.EqualTo(new[] { 1, 2, 2, 3, 3, 4, 5 }));
        Assert.That(WFContractSystem.NextSlotAt(1), Is.EqualTo(2));
        Assert.That(WFContractSystem.NextSlotAt(5), Is.EqualTo(8));
        Assert.That(WFContractSystem.Drifted(5, 1), Is.EqualTo(4));
        Assert.That(WFContractSystem.Drifted(-3, 2), Is.EqualTo(-1));
        Assert.That(WFContractSystem.Drifted(1, 3), Is.EqualTo(0));
        Assert.That(WFContractSystem.RankOf(-6), Is.EqualTo(0), "At -6 a contractor should be hostile.");
        Assert.That(WFContractSystem.RankOf(-1), Is.EqualTo(1), "Below zero a contractor should distrust you.");
        Assert.That(WFContractSystem.RankForTier(1), Is.EqualTo(1), "Tier 1 should be open to the distrusted.");
    }

    /// <summary>Standing is stored per character and contractor, and a second save updates the row.</summary>
    [Test]
    public async Task StandingIsStored()
    {
        await using var pair = await PoolManager.GetServerClient();
        var db = pair.Server.ResolveDependency<Content.Server.Database.IServerDbManager>();
        var user = System.Guid.NewGuid();
        await db.SetContractStandingAsync(user, "Stored Contractor", "WFContractorMMC", 4, 7);
        await db.SetContractStandingAsync(user, "Stored Contractor", "WFContractorMMC", -2, 8);
        await db.SetContractStandingAsync(user, "Other Character", "WFContractorMMC", 9, 8);
        var stored = await db.GetContractStandingAsync(user, "Stored Contractor");
        Assert.That(stored, Has.Count.EqualTo(1));
        Assert.That(stored[0].Points, Is.EqualTo(-2));
        Assert.That(stored[0].LastRound, Is.EqualTo(8));
        await pair.CleanReturnAsync();
    }
}
