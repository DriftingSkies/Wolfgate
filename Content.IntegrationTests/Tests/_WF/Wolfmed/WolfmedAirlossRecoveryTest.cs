#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid.Prototypes;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// "Races that don't breathe can't heal oxygen damage on their own like normal." Every species a player can pick,
/// a minute in station air with full blood. None of them suffocates standing there (the thaven did: its port left
/// out the gas entries that feed its lungs), and each sheds the suffocation and blood loss damage it was given at
/// something like the usual rate. A species with no respirator had nothing to take suffocation damage back, and
/// the shadekin healed blood loss at a quarter of everybody else's rate.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedBreathingSystem))]
public sealed class WolfmedAirlossRecoveryTest : WolfmedGameTest
{
    private const string Asphyxiation = "Asphyxiation";
    private const string Bloodloss = "Bloodloss";

    /// <summary>What each body starts with, where its damage container can hold it.</summary>
    private static readonly FixedPoint2 Given = FixedPoint2.New(40);

    /// <summary>
    /// What may be left a minute later. The usual species are at 11 and 21 by then, and the Proto shadekin, whose
    /// trait halves all its healing, at 25 and 30.5. At the old quarter rate blood loss was still over 35.
    /// </summary>
    private static readonly FixedPoint2 Left = FixedPoint2.New(33);

    [Test]
    public async Task EverySpeciesBreathesAndRecoversTest()
    {
        var map = await CreateTestMap();
        var bodies = new List<(string Species, EntityUid Clean, EntityUid Hurt, FixedPoint2 Asph, FixedPoint2 Blood)>();

        await Server.WaitAssertion(() =>
        {
            var damageable = SEntMan.System<DamageableSystem>();
            foreach (var species in SProtoMan.EnumeratePrototypes<SpeciesPrototype>().OrderBy(s => s.ID))
            {
                if (!species.RoundStart)
                    continue;

                var clean = SEntMan.SpawnEntity(species.Prototype, map.GridCoords);
                if (!SEntMan.HasComponent<WoundHostComponent>(clean))
                {
                    SEntMan.DeleteEntity(clean);
                    continue;
                }

                var hurt = SEntMan.SpawnEntity(species.Prototype, map.GridCoords);
                var held = SEntMan.GetComponent<DamageableComponent>(hurt).Damage.DamageDict;
                var hit = new DamageSpecifier();
                foreach (var type in new[] { Asphyxiation, Bloodloss })
                {
                    if (held.ContainsKey(type))
                        hit.DamageDict[type] = Given;
                }

                damageable.TryChangeDamage(hurt, hit, ignoreResistances: true, interruptsDoAfters: false);
                bodies.Add((species.ID, clean, hurt, Read(hurt, Asphyxiation), Read(hurt, Bloodloss)));
            }
        });
        await RunSeconds(60);

        await Server.WaitAssertion(() =>
        {
            var suffocating = new List<string>();
            var stuck = new List<string>();
            foreach (var (species, clean, hurt, asph, blood) in bodies)
            {
                TestContext.Out.WriteLine($"{species}: suffocation {asph} to {Read(hurt, Asphyxiation)}, blood loss " +
                                          $"{blood} to {Read(hurt, Bloodloss)}; untouched body {Read(clean, Asphyxiation)}.");

                if (Read(clean, Asphyxiation) > FixedPoint2.Zero)
                    suffocating.Add($"{species} ({Read(clean, Asphyxiation)})");

                if (asph > Left && Read(hurt, Asphyxiation) > Left)
                    stuck.Add($"{species} suffocation {asph} to {Read(hurt, Asphyxiation)}");

                if (blood > Left && Read(hurt, Bloodloss) > Left)
                    stuck.Add($"{species} blood loss {blood} to {Read(hurt, Bloodloss)}");
            }

            Assert.Multiple(() =>
            {
                Assert.That(bodies, Is.Not.Empty);
                Assert.That(suffocating, Is.Empty, "species that suffocate standing in station air.");
                Assert.That(stuck, Is.Empty, "oxygen damage that does not heal on its own.");
            });
        });
    }

    private FixedPoint2 Read(EntityUid body, string type) =>
        SEntMan.GetComponent<DamageableComponent>(body).Damage.DamageDict.GetValueOrDefault(type);
}
