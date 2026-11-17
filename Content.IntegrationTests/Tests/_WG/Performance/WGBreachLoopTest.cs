using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Performance;

/// <summary>Checks that a tile venting into space nonstop is quieted, while an ordinary breach is not.</summary>
[TestFixture]
[TestOf(typeof(AtmosphereSystem))]
public sealed class WGBreachLoopTest
{
    /// <summary>
    /// A tile depressurizing every two seconds keeps its space wind for three minutes and is then quieted. A tile whose
    /// venting stops and starts again later begins a new run instead.
    /// </summary>
    [Test]
    public async Task LoopingBreachIsQuieted()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var atmos = server.System<AtmosphereSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        var looping = new TileAtmosphere(grid, new Vector2i(17, 6));
        var paused = new TileAtmosphere(grid, new Vector2i(-4, 2));

        var quietAt = -1;
        var pausedQuieted = false;
        for (var i = 0; i < 100; i++)
        {
            await server.WaitPost(() =>
            {
                if (atmos.WGBreachLooping(grid, looping) && quietAt < 0)
                    quietAt = i;

                // Vents for a minute, stops for ten seconds, then vents again for two more.
                if (i < 30 || i >= 35)
                    pausedQuieted |= atmos.WGBreachLooping(grid, paused);
            });
            await server.WaitRunTicks(60);
        }

        var stillQuiet = false;
        await server.WaitPost(() => stillQuiet = atmos.WGBreachLooping(grid, looping));

        Assert.Multiple(() =>
        {
            // Two seconds per step: three minutes is step 90.
            Assert.That(quietAt, Is.InRange(89, 92), "the looping tile wasn't quieted after three minutes.");
            Assert.That(stillQuiet, Is.True, "the quieted tile came back before its quiet time ended.");
            Assert.That(pausedQuieted, Is.False, "a breach that stopped and started again was quieted as a loop.");
        });

        await pair.CleanReturnAsync();
    }
}
