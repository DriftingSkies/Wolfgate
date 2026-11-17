#nullable enable
using Content.Server.Power.Generator;
using Content.Shared.Materials;
using Content.Shared.Power.Generator;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.NetTraffic;

/// <summary>A running solid-fuel generator only sends its fuel storage again when a whole unit of fuel is burnt.</summary>
[TestFixture]
[TestOf(typeof(GeneratorSystem))]
public sealed class WGGeneratorFuelNetTest
{
    private const string Generator = "PortableGeneratorSuperPacmanShuttle";
    private const int Ticks = 60;

    [Test]
    public async Task FractionalBurnDoesNotResendStorage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entMan = server.EntMan;
        EntityUid generator = default;

        await server.WaitAssertion(() =>
        {
            generator = entMan.SpawnEntity(Generator, map.GridCoords);
            server.System<GeneratorSystem>().SetFuelGeneratorOn(generator, true);
            Assert.That(entMan.GetComponent<FuelGeneratorComponent>(generator).On, "The generator did not start.");
        });

        await pair.RunTicksSync(5);
        var storage = entMan.GetComponent<MaterialStorageComponent>(generator);
        var last = storage.LastModifiedTick;
        var sends = 0;

        for (var i = 0; i < Ticks; i++)
        {
            await pair.RunTicksSync(1);
            if (storage.LastModifiedTick == last)
                continue;

            last = storage.LastModifiedTick;
            sends++;
        }

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<FuelGeneratorComponent>(generator).On, "The generator stopped during the test."));
        Assert.That(sends, Is.LessThan(Ticks / 2),
            $"The fuel storage was marked changed on {sends} of {Ticks} ticks; it should only change when a unit of fuel is burnt.");

        await pair.CleanReturnAsync();
    }
}
