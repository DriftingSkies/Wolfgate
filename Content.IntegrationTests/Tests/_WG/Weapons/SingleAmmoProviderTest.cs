#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared._FinalHorizon.Guns.AlternateGun;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Weapons;

/// <summary>
/// A gun takes its rounds from one ammo provider. With two, both answer the same shot and it draws two rounds, which
/// fails a debug assertion in the gun system whenever the gun fires.
/// </summary>
[TestFixture]
[TestOf(typeof(SharedGunSystem))]
public sealed class SingleAmmoProviderTest
{
    /// <summary>The components that add rounds to a shot. The last three don't derive from the common base.</summary>
    private static readonly Type[] Providers =
    {
        typeof(AmmoProviderComponent),
        typeof(BallisticAmmoProviderComponent),
        typeof(SolutionAmmoProviderComponent),
        typeof(AlternateGunAmmoProviderComponent),
    };

    private static bool AddsRounds(Type component)
    {
        return Providers.Any(provider => provider.IsAssignableFrom(component));
    }

    /// <summary>No entity prototype carries two components that add rounds to a shot.</summary>
    [Test]
    public async Task NoPrototypeHasTwoAmmoProviders()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var guns = 0;
            var doubled = new List<string>();
            foreach (var proto in prototypes.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract)
                    continue;

                var providers = proto.Components
                    .Where(entry => AddsRounds(entry.Value.Component.GetType()))
                    .Select(entry => entry.Key)
                    .Order()
                    .ToArray();

                if (providers.Length > 0)
                    guns++;
                if (providers.Length > 1)
                    doubled.Add($"{proto.ID} ({string.Join(", ", providers)})");
            }

            doubled.Sort(StringComparer.Ordinal);
            var names = string.Join("\n", doubled);
            Assert.Multiple(() =>
            {
                Assert.That(guns, Is.GreaterThan(100), "The check needs the real guns.");
                Assert.That(doubled, Is.Empty,
                    $"These prototypes have more than one ammo provider, so one shot draws a round from each:\n{names}");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A provider added later under the usual name is one the check above counts.</summary>
    [Test]
    public async Task EveryAmmoProviderIsCounted()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var uncounted = factory.AllRegisteredTypes
                .Where(type => type.Name.EndsWith("AmmoProviderComponent", StringComparison.Ordinal) && !AddsRounds(type))
                .Select(type => type.Name)
                .Order()
                .ToArray();
            var names = string.Join(", ", uncounted);

            Assert.That(uncounted, Is.Empty,
                $"Add these to {nameof(Providers)}, or derive them from {nameof(AmmoProviderComponent)}: {names}");
        });

        await pair.CleanReturnAsync();
    }
}
