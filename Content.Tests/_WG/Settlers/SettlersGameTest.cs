using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WG.Settlers;
using NUnit.Framework;

namespace Content.Tests._WG.Settlers;

/// <summary>Settlers of Colossus: the board, setup, production, the 7, contracts, bonuses, trades, winning and absent players.</summary>
[TestFixture]
public sealed class SettlersGameTest
{
    private static SettlersAction Act(SettlersActionKind kind, int target = 0, int first = 0, int second = 0)
    {
        return new SettlersAction(kind, target, first, second);
    }

    private static SettlersAction Counts(SettlersActionKind kind, int[] give, int[] want = null)
    {
        return new SettlersAction(kind) { Give = give, Want = want ?? new int[SettlersRules.ResourceCount] };
    }

    /// <summary>Accepts an offer on the terms the player saw.</summary>
    private static SettlersAction Take(int offerer, int[] give, int[] want)
    {
        return new SettlersAction(SettlersActionKind.Accept, offerer) { Give = give, Want = want };
    }

    private static int[] Res(int scrap = 0, int ice = 0, int gas = 0, int biomass = 0, int ore = 0)
    {
        return new[] { scrap, ice, gas, biomass, ore };
    }

    /// <summary>A started game with players in seats 0 to count-1, setup played on the first legal spots.</summary>
    private static SettlersGame Started(int count, int seed = 1)
    {
        var game = new SettlersGame(new Random(seed));
        for (var seat = 0; seat < count; seat++)
        {
            game.SetPresent(seat, true);
        }

        Assert.That(game.Start(), Is.True);
        PlaySetup(game);
        return game;
    }

    private static void PlaySetup(SettlersGame game)
    {
        var state = game.State;
        while (state.Phase is SettlersPhase.SetupOutpost or SettlersPhase.SetupLane)
        {
            var seat = state.Turn;
            if (state.Phase == SettlersPhase.SetupOutpost)
            {
                var vertex = Enumerable.Range(0, SettlersBoard.VertexCount).First(v => state.CanPlaceOutpost(seat, v));
                Assert.That(game.TryAct(seat, Act(SettlersActionKind.BuildOutpost, vertex)), Is.True);
            }
            else
            {
                var edge = Enumerable.Range(0, SettlersBoard.EdgeCount).First(e => state.CanPlaceLane(seat, e));
                Assert.That(game.TryAct(seat, Act(SettlersActionKind.BuildLane, edge)), Is.True);
            }
        }
    }

    /// <summary>A started game wiped bare: no pieces, empty hands, a full bank, seat 0's turn after the roll.</summary>
    private static SettlersGame Bare(int count, int seed = 1)
    {
        var game = Started(count, seed);
        var state = game.State;
        Array.Fill(state.VertexOwner, -1);
        Array.Fill(state.EdgeOwner, -1);
        Array.Clear(state.Colony);
        Array.Fill(state.Bank, SettlersRules.BankStock);
        foreach (var player in state.Players)
        {
            Array.Clear(player.Hand);
            player.Cards.Clear();
            player.NewCards.Clear();
            player.Route = 0;
        }

        state.RouteHolder = -1;
        state.Turn = 0;
        state.Phase = SettlersPhase.Main;
        state.Rolled = true;
        return game;
    }

    [Test]
    public void BoardShape()
    {
        Assert.That(SettlersBoard.VertexPos, Has.Length.EqualTo(SettlersBoard.VertexCount));
        Assert.That(SettlersBoard.EdgeA, Has.Length.EqualTo(SettlersBoard.EdgeCount));

        for (var tile = 0; tile < SettlersBoard.TileCount; tile++)
        {
            Assert.That(SettlersBoard.TileVertices[tile].Distinct().Count(), Is.EqualTo(6));
            Assert.That(SettlersBoard.TileNeighbours[tile], Has.Length.InRange(3, 6));
        }

        for (var vertex = 0; vertex < SettlersBoard.VertexCount; vertex++)
        {
            Assert.That(SettlersBoard.VertexTiles[vertex], Has.Length.InRange(1, 3));
            Assert.That(SettlersBoard.VertexEdges[vertex], Has.Length.InRange(2, 3));
        }

        var dockCorners = new HashSet<int>();
        foreach (var edge in SettlersBoard.DockEdges)
        {
            Assert.That(SettlersBoard.VertexTiles[SettlersBoard.EdgeA[edge]].Intersect(SettlersBoard.VertexTiles[SettlersBoard.EdgeB[edge]]).Count(),
                Is.EqualTo(1), "A dock sits on a coast side.");
            Assert.That(dockCorners.Add(SettlersBoard.EdgeA[edge]) && dockCorners.Add(SettlersBoard.EdgeB[edge]), Is.True, "Two docks share a corner.");
        }
    }

    [Test]
    public void LayoutIsLegal()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var state = new SettlersGame(new Random(seed)).State;
            for (var sector = 0; sector < SettlersRules.SectorCounts.Length; sector++)
            {
                Assert.That(state.Sectors.Count(s => (int) s == sector), Is.EqualTo(SettlersRules.SectorCounts[sector]));
            }

            Assert.That(state.Sectors[state.Pirates], Is.EqualTo(SettlersSector.DeadZone), "The pirates start on the dead zone.");
            Assert.That(state.Numbers[state.Pirates], Is.Zero);
            Assert.That(state.Numbers.Where(n => n > 0).OrderBy(n => n), Is.EqualTo(SettlersRules.Numbers.OrderBy(n => n)));
            Assert.That(state.Docks.OrderBy(d => d), Is.EqualTo(SettlersRules.Docks.OrderBy(d => d)));
            Assert.That(state.DeckCount, Is.EqualTo(SettlersRules.CardCounts.Sum()));

            for (var tile = 0; tile < SettlersBoard.TileCount; tile++)
            {
                if (!SettlersRules.IsHot(state.Numbers[tile]))
                    continue;

                foreach (var neighbour in SettlersBoard.TileNeighbours[tile])
                {
                    Assert.That(SettlersRules.IsHot(state.Numbers[neighbour]), Is.False, $"Seed {seed}: a 6 or 8 sits beside another.");
                }
            }
        }
    }

    [Test]
    public void SetupRunsForwardsAndBackAndPaysTheSecondOutpost()
    {
        var game = new SettlersGame(new Random(3));
        game.SetPresent(0, true);
        game.SetPresent(1, true);
        game.SetPresent(3, true);
        Assert.That(game.Start(), Is.True);

        var state = game.State;
        var order = state.SetupOrder;
        Assert.That(order, Has.Length.EqualTo(6));
        Assert.That(order.Take(3), Is.EqualTo(order.Skip(3).Reverse()));
        Assert.That(order.Distinct().OrderBy(s => s), Is.EqualTo(new[] { 0, 1, 3 }));
        Assert.That(state.Players[2].InGame, Is.False);

        var first = order[0];
        Assert.That(game.TryAct(order[1], Act(SettlersActionKind.BuildOutpost, 0)), Is.False, "Out of turn.");
        Assert.That(game.TryAct(first, Act(SettlersActionKind.BuildLane, 0)), Is.False, "The outpost comes first.");

        PlaySetup(game);
        Assert.That(state.Phase, Is.EqualTo(SettlersPhase.Roll));
        Assert.That(state.Turn, Is.EqualTo(first));

        foreach (var seat in new[] { 0, 1, 3 })
        {
            Assert.That(state.Count(seat, false), Is.EqualTo(2));
            Assert.That(state.Lanes(seat), Is.EqualTo(2));

            // Paid for the second outpost, which is one of the two.
            var yields = Enumerable.Range(0, SettlersBoard.VertexCount).Where(v => state.VertexOwner[v] == seat)
                .Select(v => SettlersBoard.VertexTiles[v].Count(t => state.Sectors[t] != SettlersSector.DeadZone)).ToList();
            Assert.That(SettlersRules.Total(state.Players[seat].Hand), Is.AnyOf(yields[0], yields[1]), $"Seat {seat} was paid for one outpost.");
        }
    }

    [Test]
    public void OutpostsKeepTheirDistance()
    {
        var game = Bare(2);
        var state = game.State;
        state.VertexOwner[0] = 1;
        foreach (var neighbour in SettlersBoard.VertexNeighbours[0])
        {
            Assert.That(state.SpotFree(neighbour), Is.False);
        }

        var edge = SettlersBoard.VertexEdges[0][0];
        state.EdgeOwner[edge] = 0;
        var far = SettlersBoard.OtherEnd(edge, 0);
        state.Players[0].Hand = Res(1, 1, 1, 1);
        Assert.That(game.TryAct(0, Act(SettlersActionKind.BuildOutpost, far)), Is.False, "Beside another outpost.");
    }

    [Test]
    public void ProductionColoniesPiratesAndAShortBank()
    {
        var game = Bare(2);
        var state = game.State;
        // A tile whose number no neighbour shares, so its corners earn from it alone.
        var tile = Enumerable.Range(0, SettlersBoard.TileCount).First(t => state.Numbers[t] > 0
            && SettlersBoard.TileNeighbours[t].All(n => state.Numbers[n] != state.Numbers[t]));
        var number = state.Numbers[tile];
        var yield = (int) SettlersRules.Yield(state.Sectors[tile])!.Value;
        var corners = SettlersBoard.TileVertices[tile];

        state.VertexOwner[corners[0]] = 0;
        state.Colony[corners[0]] = true;
        state.VertexOwner[corners[3]] = 1;

        state.Phase = SettlersPhase.Roll;
        state.Rolled = false;
        Assert.That(game.Roll(number / 2, number - number / 2), Is.True);
        Assert.That(state.Players[0].Hand[yield], Is.EqualTo(2), "A colony yields two.");
        Assert.That(state.Players[1].Hand[yield], Is.EqualTo(1));
        Assert.That(state.Phase, Is.EqualTo(SettlersPhase.Main));

        state.Pirates = tile;
        state.Phase = SettlersPhase.Roll;
        Assert.That(game.Roll(number / 2, number - number / 2), Is.True);
        Assert.That(state.Players[0].Hand[yield], Is.EqualTo(2), "The pirates block the sector.");

        state.Pirates = Array.IndexOf(state.Sectors, SettlersSector.DeadZone);

        state.Bank[yield] = 2;
        state.Phase = SettlersPhase.Roll;
        game.Roll(number / 2, number - number / 2);
        Assert.That(state.Players[0].Hand[yield], Is.EqualTo(2), "Two players owed more than the bank holds get nothing.");

        state.VertexOwner[corners[3]] = -1;
        state.Bank[yield] = 1;
        state.Phase = SettlersPhase.Roll;
        game.Roll(number / 2, number - number / 2);
        Assert.That(state.Players[0].Hand[yield], Is.EqualTo(3), "A single player owed gets what is left.");
        Assert.That(state.Bank[yield], Is.Zero);
    }

    [Test]
    public void SevenDiscardsMovesThePiratesAndRobs()
    {
        var game = Bare(3);
        var state = game.State;
        state.Players[0].Hand = Res(9);
        state.Players[1].Hand = Res(ore: 7);
        state.Players[2].Hand = Res(gas: 3);
        state.Phase = SettlersPhase.Roll;
        state.Rolled = false;

        Assert.That(game.Roll(3, 4), Is.True);
        Assert.That(state.Phase, Is.EqualTo(SettlersPhase.Discard));
        Assert.That(state.Discard, Is.EqualTo(new[] { 4, 0, 0, 0 }));

        Assert.That(game.TryAct(0, Counts(SettlersActionKind.Discard, Res(3))), Is.False, "Too few.");
        Assert.That(game.TryAct(0, Counts(SettlersActionKind.Discard, Res(ice: 4))), Is.False, "Not held.");
        Assert.That(game.TryAct(0, Counts(SettlersActionKind.Discard, Res(4))), Is.True);
        Assert.That(state.Phase, Is.EqualTo(SettlersPhase.MovePirates));
        Assert.That(game.TryAct(0, Act(SettlersActionKind.Roll)), Is.False);
        Assert.That(game.TryAct(0, Act(SettlersActionKind.MovePirates, state.Pirates)), Is.False, "The pirates must move.");

        var tile = Enumerable.Range(0, SettlersBoard.TileCount).First(t => t != state.Pirates);
        state.VertexOwner[SettlersBoard.TileVertices[tile][0]] = 2;
        Assert.That(game.TryAct(0, Act(SettlersActionKind.MovePirates, tile)), Is.True);
        Assert.That(state.Pirates, Is.EqualTo(tile));
        Assert.That(state.Phase, Is.EqualTo(SettlersPhase.Main), "A lone victim is robbed without asking.");
        Assert.That(state.Players[2].Hand[2], Is.EqualTo(2));
        Assert.That(state.Players[0].Hand[2], Is.EqualTo(1));
    }

    [Test]
    public void StealAsksWhenThereAreTwoVictims()
    {
        var game = Bare(3);
        var state = game.State;
        state.Players[1].Hand = Res(ore: 1);
        state.Players[2].Hand = Res(ice: 1);
        state.Phase = SettlersPhase.MovePirates;

        var tile = Enumerable.Range(0, SettlersBoard.TileCount).First(t => t != state.Pirates);
        state.VertexOwner[SettlersBoard.TileVertices[tile][0]] = 1;
        state.VertexOwner[SettlersBoard.TileVertices[tile][3]] = 2;
        Assert.That(game.TryAct(0, Act(SettlersActionKind.MovePirates, tile)), Is.True);
        Assert.That(state.Phase, Is.EqualTo(SettlersPhase.Steal));
        Assert.That(game.TryAct(0, Act(SettlersActionKind.Steal, 0)), Is.False, "Not from yourself.");
        Assert.That(game.TryAct(0, Act(SettlersActionKind.Steal, 2)), Is.True);
        Assert.That(state.Players[0].Hand, Is.EqualTo(Res(ice: 1)));
    }

    [Test]
    public void ContractsWaitATurnAndOneIsPlayedATurn()
    {
        var game = Bare(2);
        var state = game.State;
        var me = state.Players[0];
        me.Hand = Res(gas: 1, biomass: 1, ore: 1);
        Assert.That(game.TryAct(0, Act(SettlersActionKind.BuyCard)), Is.True);
        Assert.That(state.DeckCount, Is.EqualTo(SettlersRules.CardCounts.Sum() - 1));
        Assert.That(me.NewCards, Has.Count.EqualTo(1));

        var bought = me.NewCards[0];
        if (bought != SettlersCard.Charter)
            Assert.That(game.TryAct(0, Act(SettlersActionKind.PlayCard, (int) bought, 0, 1)), Is.False, "Bought this turn.");

        me.NewCards.Clear();
        me.Cards.AddRange(new[] { SettlersCard.Windfall, SettlersCard.Embargo, SettlersCard.Charter });
        state.Players[1].Hand = Res(ore: 3, ice: 2);

        Assert.That(game.TryAct(0, Act(SettlersActionKind.PlayCard, (int) SettlersCard.Charter)), Is.False, "A charter is never played.");
        Assert.That(game.TryAct(0, Act(SettlersActionKind.PlayCard, (int) SettlersCard.Windfall, 4, 4)), Is.True);
        Assert.That(me.Hand[4], Is.EqualTo(2));
        Assert.That(game.TryAct(0, Act(SettlersActionKind.PlayCard, (int) SettlersCard.Embargo, 4)), Is.False, "One a turn.");

        Assert.That(game.TryAct(0, Act(SettlersActionKind.EndTurn)), Is.True);
        state.Turn = 0;
        state.Phase = SettlersPhase.Roll;
        state.PlayedCard = false;
        Assert.That(game.TryAct(0, Act(SettlersActionKind.PlayCard, (int) SettlersCard.Embargo, 4)), Is.True, "Before the roll is fine.");
        Assert.That(me.Hand[4], Is.EqualTo(5));
        Assert.That(state.Players[1].Hand, Is.EqualTo(Res(ice: 2)));
        Assert.That(state.Points(0), Is.EqualTo(1), "The charter counts.");
        Assert.That(state.PublicPoints(0), Is.Zero);
    }

    [Test]
    public void SurveyLaysTwoFreeLanes()
    {
        var game = Bare(2);
        var state = game.State;
        state.VertexOwner[0] = 0;
        state.Players[0].Cards.Add(SettlersCard.Survey);

        Assert.That(game.TryAct(0, Act(SettlersActionKind.PlayCard, (int) SettlersCard.Survey)), Is.True);
        Assert.That(state.Phase, Is.EqualTo(SettlersPhase.Survey));
        for (var i = 0; i < 2; i++)
        {
            var edge = Enumerable.Range(0, SettlersBoard.EdgeCount).First(e => state.CanPlaceLane(0, e));
            Assert.That(game.TryAct(0, Act(SettlersActionKind.BuildLane, edge)), Is.True);
        }

        Assert.That(state.Lanes(0), Is.EqualTo(2));
        Assert.That(state.Phase, Is.EqualTo(SettlersPhase.Main));
        Assert.That(SettlersRules.Total(state.Players[0].Hand), Is.Zero);
    }

    [Test]
    public void EscortsWinTheLargestFleet()
    {
        var game = Bare(2);
        var state = game.State;
        for (var i = 0; i < 3; i++)
        {
            state.Players[0].Cards.Add(SettlersCard.Escort);
            state.PlayedCard = false;
            state.Phase = SettlersPhase.Main;
            Assert.That(game.TryAct(0, Act(SettlersActionKind.PlayCard, (int) SettlersCard.Escort)), Is.True);
            Assert.That(state.Phase, Is.EqualTo(SettlersPhase.MovePirates));
            var tile = state.Pirates == 0 ? 1 : 0;
            Assert.That(game.TryAct(0, Act(SettlersActionKind.MovePirates, tile)), Is.True);
        }

        Assert.That(state.FleetHolder, Is.EqualTo(0));
        Assert.That(state.PublicPoints(0), Is.EqualTo(SettlersRules.FleetPoints));
    }

    /// <summary>Six corners joined in a line whose middle corner has a third side free for a rival's lane.</summary>
    private static List<int> Path()
    {
        for (var start = 0; start < SettlersBoard.VertexCount; start++)
        {
            var path = new List<int> { start };
            if (Extend(path))
                return path;
        }

        throw new InvalidOperationException("No path on the board.");
    }

    private static bool Extend(List<int> path)
    {
        if (path.Count == 6)
            return SettlersBoard.VertexEdges[path[2]].Length == 3;

        foreach (var next in SettlersBoard.VertexNeighbours[path[^1]])
        {
            if (path.Contains(next))
                continue;

            path.Add(next);
            if (Extend(path))
                return true;

            path.RemoveAt(path.Count - 1);
        }

        return false;
    }

    private static int EdgeBetween(int a, int b)
    {
        return SettlersBoard.VertexEdges[a].First(e => SettlersBoard.Touches(e, b));
    }

    [Test]
    public void LongestRouteNeedsFiveAndIsCutByARival()
    {
        var game = Bare(2);
        var state = game.State;
        var path = Path();
        for (var i = 0; i < 4; i++)
        {
            state.EdgeOwner[EdgeBetween(path[i], path[i + 1])] = 0;
        }

        Assert.That(state.LongestRoute(0), Is.EqualTo(4));
        state.Players[0].Hand = Res(1, 1);
        Assert.That(game.TryAct(0, Act(SettlersActionKind.BuildLane, EdgeBetween(path[4], path[5]))), Is.True);
        Assert.That(state.Players[0].Route, Is.EqualTo(5));
        Assert.That(state.RouteHolder, Is.EqualTo(0));

        // A rival outpost in the middle of the route splits it into 2 and 3.
        var middle = path[2];
        var spur = SettlersBoard.VertexEdges[middle].First(e => state.EdgeOwner[e] < 0);
        state.EdgeOwner[spur] = 1;
        state.Turn = 1;
        state.Players[1].Hand = Res(1, 1, 1, 1);
        Assert.That(game.TryAct(1, Act(SettlersActionKind.BuildOutpost, middle)), Is.True);
        Assert.That(state.Players[0].Route, Is.EqualTo(3));
        Assert.That(state.RouteHolder, Is.EqualTo(-1));
    }

    [Test]
    public void BankAndDockRatios()
    {
        var game = Bare(2);
        var state = game.State;
        state.Players[0].Hand = Res(ore: 4);
        Assert.That(state.TradeRatio(0, 4), Is.EqualTo(4));
        Assert.That(game.TryAct(0, Act(SettlersActionKind.BankTrade, 0, 4, 4)), Is.False, "The same resource.");
        Assert.That(game.TryAct(0, Act(SettlersActionKind.BankTrade, 0, 4, 0)), Is.True);
        Assert.That(state.Players[0].Hand, Is.EqualTo(Res(1)));

        for (var dock = 0; dock < SettlersBoard.DockCount; dock++)
        {
            state.Docks[dock] = dock == 0 ? 4 : SettlersRules.AnyDock;
        }

        var edge = SettlersBoard.DockEdges[0];
        state.VertexOwner[SettlersBoard.EdgeA[edge]] = 0;
        Assert.That(state.TradeRatio(0, 4), Is.EqualTo(2));
        Assert.That(state.TradeRatio(0, 1), Is.EqualTo(4));
        state.VertexOwner[SettlersBoard.EdgeA[SettlersBoard.DockEdges[1]]] = 0;
        Assert.That(state.TradeRatio(0, 1), Is.EqualTo(3));
    }

    [Test]
    public void OffersAlwaysInvolveThePlayerWhoseTurnItIs()
    {
        var game = Bare(3);
        var state = game.State;
        state.Players[0].Hand = Res(ore: 2);
        state.Players[1].Hand = Res(ice: 1);
        state.Players[2].Hand = Res(ice: 1, gas: 1);

        Assert.That(game.TryAct(0, Counts(SettlersActionKind.Offer, Res(ore: 3), Res(ice: 1))), Is.False, "Not held.");
        Assert.That(game.TryAct(0, Counts(SettlersActionKind.Offer, Res(ore: 1), Res(ice: 1))), Is.True);
        Assert.That(game.TryAct(0, Take(0, Res(ore: 1), Res(ice: 1))), Is.False, "Your own offer.");
        Assert.That(game.TryAct(1, Take(0, Res(ore: 1), Res(ice: 1))), Is.True);
        Assert.That(state.Players[0].Hand, Is.EqualTo(Res(ice: 1, ore: 1)));
        Assert.That(state.Players[1].Hand, Is.EqualTo(Res(ore: 1)));
        Assert.That(state.Offers[0], Is.Null);

        Assert.That(game.TryAct(2, Counts(SettlersActionKind.Offer, Res(gas: 1), Res(ore: 1))), Is.True);
        Assert.That(game.TryAct(1, Take(2, Res(gas: 1), Res(ore: 1))), Is.False, "Two players off turn can't trade.");
        Assert.That(game.TryAct(0, Take(2, Res(gas: 1), Res(ore: 1))), Is.True);
        Assert.That(state.Players[0].Hand, Is.EqualTo(Res(ice: 1, gas: 1)));

        Assert.That(game.TryAct(1, Counts(SettlersActionKind.Offer, Res(ore: 1), Res(gas: 1))), Is.True);
        Assert.That(game.TryAct(0, Act(SettlersActionKind.EndTurn)), Is.True);
        Assert.That(state.Offers.All(o => o == null), Is.True, "Offers end with the turn.");
    }

    [Test]
    public void AcceptingAChangedOfferIsRefused()
    {
        var game = Bare(2);
        var state = game.State;
        state.Players[0].Hand = Res(ore: 1);
        state.Players[1].Hand = Res(ice: 3);

        Assert.That(game.TryAct(0, Counts(SettlersActionKind.Offer, Res(ore: 1), Res(ice: 1))), Is.True);
        Assert.That(game.TryAct(0, Counts(SettlersActionKind.Offer, Res(ore: 1), Res(ice: 3))), Is.True);
        Assert.That(game.TryAct(1, Take(0, Res(ore: 1), Res(ice: 1))), Is.False, "The offer changed after it was seen.");
        Assert.That(state.Players[1].Hand, Is.EqualTo(Res(ice: 3)));

        Assert.That(game.TryAct(1, Take(0, Res(ore: 1), Res(ice: 3))), Is.True);
        Assert.That(state.Players[1].Hand, Is.EqualTo(Res(ore: 1)));
    }

    [Test]
    public void TenPointsOnYourTurnWins()
    {
        var game = Bare(2);
        var state = game.State;

        // Four colonies and a lane to build an outpost from: 8 + 1 points before it.
        var spots = new List<int>();
        for (var vertex = 0; vertex < SettlersBoard.VertexCount && spots.Count < 6; vertex++)
        {
            if (!state.SpotFree(vertex))
                continue;

            state.VertexOwner[vertex] = 0;
            spots.Add(vertex);
        }

        for (var i = 0; i < 4; i++)
        {
            state.Colony[spots[i]] = true;
        }

        state.VertexOwner[spots[5]] = -1;
        var target = spots[5];
        state.EdgeOwner[SettlersBoard.VertexEdges[target][0]] = 0;
        Assert.That(state.Points(0), Is.EqualTo(9));

        state.Players[0].Hand = Res(1, 1, 1, 1);
        Assert.That(game.TryAct(0, Act(SettlersActionKind.BuildOutpost, target)), Is.True);
        Assert.That(state.Phase, Is.EqualTo(SettlersPhase.Finished));
        Assert.That(state.Winner, Is.EqualTo(0));
        Assert.That(game.TryAct(0, Act(SettlersActionKind.EndTurn)), Is.False, "The game is over.");
    }

    [Test]
    public void AbsentPlayersArePlayedOutAndAnEmptyTablePauses()
    {
        var game = Bare(2);
        var state = game.State;
        state.Players[1].Hand = Res(ore: 10);
        Assert.That(game.TryAct(0, Act(SettlersActionKind.EndTurn)), Is.True);
        Assert.That(state.Turn, Is.EqualTo(1));

        game.SetPresent(1, false);
        Assert.That(state.Turn, Is.EqualTo(0), "An absent player's turn passes.");
        Assert.That(state.Phase, Is.EqualTo(SettlersPhase.Roll));

        Assert.That(game.Roll(3, 4), Is.True);
        Assert.That(state.Discard[1], Is.Zero, "An absent player discards at once.");
        Assert.That(state.Players[1].Hand[4], Is.EqualTo(5));
        Assert.That(state.Phase, Is.EqualTo(SettlersPhase.MovePirates));

        Assert.That(game.TryAct(1, Act(SettlersActionKind.EndTurn)), Is.False, "Not seated.");
        game.SetPresent(0, false);
        Assert.That(state.Phase, Is.EqualTo(SettlersPhase.MovePirates), "Nobody left: the game waits.");

        game.SetPresent(1, true);
        Assert.That(state.Phase, Is.EqualTo(SettlersPhase.Roll), "Seat 0's turn was played out once someone sat down.");
        Assert.That(state.Turn, Is.EqualTo(1));
    }

    [Test]
    public void AnEmptySeatStillGetsItsSetupPieces()
    {
        var game = new SettlersGame(new Random(1));
        game.SetPresent(0, true);
        game.SetPresent(1, true);
        Assert.That(game.Start(), Is.True);
        var state = game.State;

        var absent = state.Turn;
        game.SetPresent(absent, false);
        Assert.That(state.Turn, Is.Not.EqualTo(absent), "The empty seat's setup turn is played for it.");
        Assert.That(state.Count(absent, false), Is.EqualTo(1));
        Assert.That(state.Lanes(absent), Is.EqualTo(1));

        PlaySetup(game);
        Assert.That(state.Phase, Is.EqualTo(SettlersPhase.Roll));
        Assert.That(state.Count(absent, false), Is.EqualTo(2), "Whoever takes the seat has outposts to build from.");
        Assert.That(state.Lanes(absent), Is.EqualTo(2));
    }

    [Test]
    public void ViewersOnlySeeTheirOwnHand()
    {
        var game = Bare(2);
        var state = game.State;
        state.Players[0].Hand = Res(ore: 2);
        state.Players[0].Cards.Add(SettlersCard.Charter);
        state.Players[1].Hand = Res(ice: 3);
        state.Bank[4] = 0;

        var mine = state.CopyFor(0);
        Assert.That(mine.Bank, Is.EqualTo(Res(1, 1, 1, 1, 0)), "Only whether the bank has each resource.");
        Assert.That(mine.Players[0].Hand, Is.EqualTo(Res(ore: 2)));
        Assert.That(mine.Players[0].HiddenPoints, Is.EqualTo(1));
        Assert.That(mine.Players[1].Hand, Is.EqualTo(Res()));
        Assert.That(mine.Players[1].HandCount, Is.EqualTo(3));
        Assert.That(mine.HandSize(1), Is.EqualTo(3));

        var spectator = state.CopyFor(null);
        Assert.That(spectator.Bank, Is.EqualTo(Res(1, 1, 1, 1, 0)));
        Assert.That(spectator.Players[0].Cards, Is.Empty);
        Assert.That(spectator.Players[0].CardCount, Is.EqualTo(1));
        Assert.That(spectator.Points(0), Is.Zero, "A hidden charter stays hidden.");
    }
}
