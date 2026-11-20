#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Server.GameTicking.Presets;
using Content.Server.Maps;
using Content.Shared.CCVar;
using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.MapPool;

/// <summary>Round start always finds a loadable map, however many players are connected.</summary>
[TestFixture]
[TestOf(typeof(GameMapManager))]
public sealed class WGMapPoolTest
{
    /// <summary>Well above the live server's player cap (soft_max_players 150).</summary>
    private const uint FullServer = 300;

    /// <summary>Fallback maps are only picked when no pool map is eligible, so each one must be a default pool map.</summary>
    [Test]
    public async Task FallbackMapsAreInTheDefaultPool()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var protos = server.ResolveDependency<IPrototypeManager>();
            var pool = protos.Index<GameMapPoolPrototype>(CCVars.GameMapPool.DefaultValue);
            var fallbacks = protos.EnumeratePrototypes<GameMapPrototype>().Where(m => m.Fallback).ToList();

            Assert.That(fallbacks, Is.Not.Empty, "Round start needs a fallback map.");
            foreach (var map in fallbacks)
                Assert.That(pool.Maps, Does.Contain(map.ID), $"Fallback map {map.ID} isn't in {pool.ID}.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Every votable preset's pool has a map that takes a full server, so the fallback is never needed for player count.</summary>
    [Test]
    public async Task VotablePresetPoolsTakeAFullServer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var protos = server.ResolveDependency<IPrototypeManager>();
            foreach (var preset in protos.EnumeratePrototypes<GamePresetPrototype>().Where(p => p.ShowInVote))
            {
                var pool = protos.Index<GameMapPoolPrototype>(preset.MapPool ?? CCVars.GameMapPool.DefaultValue);
                var fits = pool.Maps
                    .Select(id => protos.Index<GameMapPrototype>(id))
                    .Any(m => m.MinPlayers == 0 && m.MaxPlayers >= FullServer);
                Assert.That(fits, Is.True, $"No map in {pool.ID} (preset {preset.ID}) takes {FullServer} players.");
            }
        });
        await pair.CleanReturnAsync();
    }
}
