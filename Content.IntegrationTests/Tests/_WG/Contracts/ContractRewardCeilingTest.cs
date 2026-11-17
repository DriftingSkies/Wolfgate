#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Content.Server._WG.Contracts;
using Content.Shared._WG.Contracts;

namespace Content.IntegrationTests.Tests._WG.Contracts;

/// <summary>
/// Keeps contract pay inside the economy: every template's best possible pay (top base, most packages, the longest
/// paid run, goods at their cap), shared by its minimum crew, stays under its tier's ceiling for one player.
/// </summary>
[TestFixture]
[TestOf(typeof(WGContractSystem))]
public sealed class ContractRewardCeilingTest
{
    /// <summary>Most a contract of each tier may ever pay one player, deposit aside.</summary>
    private static readonly Dictionary<int, int> Ceilings = new() { [1] = 37500, [2] = 75000, [3] = 125000, [4] = 175000, [5] = 250000 };

    [Test]
    public async Task NoContractPaysPastItsTier()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<WGContractSystem>();
            var table = new StringBuilder();
            var over = new List<string>();
            foreach (var c in server.ProtoMan.EnumeratePrototypes<WGContractPrototype>().OrderBy(c => c.Contractor.Id).ThenBy(c => c.Tier))
            {
                var (min, max) = Range(sys, c);
                // The holder's pay, with the split's remainder, at every crew size that adds a bonus.
                var minCrew = System.Math.Max(1, c.MinCrew);
                var crew = minCrew;
                var each = 0;
                for (var n = minCrew; n <= minCrew + WGContractSystem.MaxBonusCrew; n++)
                {
                    var pool = WGContractSystem.CrewPool(max, c, n);
                    var holderPay = pool / n + pool % n;
                    if (holderPay <= each)
                        continue;

                    each = holderPay;
                    crew = n;
                }
                table.AppendLine($"{c.Contractor.Id,-30} {c.ID,-36} {c.Category,-9} T{c.Tier} {min,7}-{max,7} crew {crew} each {each,7} +{c.CrewBonus:P0} dep {c.Deposit,6} {c.Minutes,3}m cr {c.Credits}");
                if (each > Ceilings[c.Tier])
                    over.Add($"{c.ID}: up to {each} each for a crew of {crew}, tier {c.Tier} ceiling {Ceilings[c.Tier]}");
            }

            TestContext.Out.WriteLine(table.ToString());
            Assert.That(over, Is.Empty, string.Join("\n", over));
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>The least and most a template can be posted for.</summary>
    private static (int Min, int Max) Range(WGContractSystem sys, WGContractPrototype c)
    {
        int min = c.Reward.X, max = c.Reward.Y;
        switch (c.Objective)
        {
            case WGContractCourier k:
                min += k.PerPackage * k.Count.X + WGContractSystem.DistancePay(k.PerKilometre, 1000f);
                max += k.PerPackage * k.Count.Y + WGContractSystem.DistancePay(k.PerKilometre, 30000f);
                break;
            case WGContractSupply s:
            {
                var values = s.Goods.Select(g => (Low: sys.UnitValue(g) * g.Count.X, High: sys.UnitValue(g) * g.Count.Y)).ToList();
                var lines = s.Pick > 0 ? s.Pick : values.Count;
                var low = values.Select(v => v.Low).OrderBy(v => v).Take(lines).Sum() * s.Premium;
                var high = values.Select(v => v.High).OrderByDescending(v => v).Take(lines).Sum() * s.Premium;
                var cap = s.GoodsCap ?? c.Reward.Y;
                min += (int) System.Math.Min(low, cap) + WGContractSystem.DistancePay(s.PerKilometre, 1000f);
                max += (int) System.Math.Min(high, cap) + WGContractSystem.DistancePay(s.PerKilometre, 30000f);
                break;
            }
            case WGContractMailRun m:
                min += m.PerStop * m.Stops.X + WGContractSystem.DistancePay(m.PerKilometre, 1000f);
                max += m.PerStop * m.Stops.Y + WGContractSystem.DistancePay(m.PerKilometre, 30000f);
                break;
            case WGContractSweep w:
                min += w.PerPoint * w.Points.X;
                max += w.PerPoint * w.Points.Y;
                break;
            case WGContractDeploy d:
                min += d.PerPoint * d.Points.X;
                max += d.PerPoint * d.Points.Y;
                break;
            case WGContractRecovery { PerUnlockedNode: > 0 } r:
                // A fully analysed artifact: allow for a large node graph.
                max += r.PerUnlockedNode * 12;
                break;
            case WGContractSurvey v:
                min += v.PerAnomaly * v.Count.X;
                max += v.PerAnomaly * v.Count.Y;
                break;
            case WGContractEscort e:
                min += WGContractSystem.DistancePay(e.PerKilometre, 1000f);
                max += WGContractSystem.DistancePay(e.PerKilometre, 30000f);
                break;
        }

        return (min, max);
    }
}
