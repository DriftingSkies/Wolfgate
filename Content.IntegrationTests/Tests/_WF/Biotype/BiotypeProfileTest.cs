using System.Linq;
using Content.Server.Database;
using Content.Shared._WF.Biotype;
using Content.Shared.Body.Prototypes;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WF.Biotype;

/// <summary>
/// The biotype data: every round-start species has a native biotype, Default resolves to it, validation stores it,
/// copies and the database keep the choice, and there is one well-formed overlay per biotype.
/// </summary>
[TestFixture]
[TestOf(typeof(WFBiotypeRules))]
public sealed class BiotypeProfileTest
{
    private static readonly ProtoId<SpeciesPrototype> Ipc = "IPC";
    private static readonly ProtoId<SpeciesPrototype> Synth = "Synth";
    private static readonly ProtoId<SpeciesPrototype> Human = "Human";

    [Test]
    public async Task SpeciesAndOverlaysTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var species in proto.EnumeratePrototypes<SpeciesPrototype>().Where(s => s.RoundStart))
                {
                    Assert.That(species.Biotype, Is.AnyOf(WFBiotype.Organic, WFBiotype.Synthetic),
                        $"{species.ID} has no native biotype.");
                    Assert.That(WFBiotypeRules.Resolve(WFBiotype.Default, species), Is.EqualTo(species.Biotype));
                    Assert.That(WFBiotypeRules.Resolve(WFBiotype.Organic, species), Is.EqualTo(WFBiotype.Organic));
                    Assert.That(WFBiotypeRules.Resolve(WFBiotype.Synthetic, species), Is.EqualTo(WFBiotype.Synthetic));
                }

                Assert.That(proto.Index(Ipc).Biotype, Is.EqualTo(WFBiotype.Synthetic));
                Assert.That(proto.Index(Synth).Biotype, Is.EqualTo(WFBiotype.Synthetic));
                Assert.That(proto.Index(Human).Biotype, Is.EqualTo(WFBiotype.Organic));

                var overlays = proto.EnumeratePrototypes<WFBiotypePrototype>().ToList();
                Assert.That(overlays.Select(o => o.Biotype),
                    Is.EquivalentTo(new[] { WFBiotype.Organic, WFBiotype.Synthetic }),
                    "There must be exactly one wfBiotype per biotype.");

                foreach (var overlay in overlays)
                {
                    Assert.That(proto.HasIndex<BodyPrototype>(overlay.Body), $"{overlay.ID}: no body {overlay.Body}.");
                    Assert.That(overlay.Components, Is.Not.Empty, $"{overlay.ID} adds nothing.");

                    foreach (var name in overlay.Remove)
                    {
                        Assert.That(factory.GetComponentAvailability(name), Is.Not.EqualTo(ComponentAvailability.Unknown),
                            $"{overlay.ID}: {name} in remove is not a component.");
                    }

                    foreach (var (name, entry) in overlay.Components.Concat(overlay.Ensure))
                    {
                        Assert.That(factory.GetRegistration(name).Type, Is.EqualTo(entry.Component.GetType()),
                            $"{overlay.ID}: {name} did not parse.");
                        Assert.That(overlay.Remove, Does.Not.Contain(name), $"{overlay.ID} both removes and adds {name}.");
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ProfileTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var db = GetDb(server);
        var user = new NetUserId(Guid.NewGuid());
        HumanoidCharacterProfile organic = default!;

        await server.WaitAssertion(() =>
        {
            var ipc = HumanoidCharacterProfile.RandomWithSpecies("IPC");
            var human = HumanoidCharacterProfile.RandomWithSpecies("Human");
            organic = (HumanoidCharacterProfile) ipc.WithBiotype(WFBiotype.Organic).Validated(pair.Player!, IoCManager.Instance!);

            Assert.Multiple(() =>
            {
                Assert.That(ipc.Biotype, Is.EqualTo(WFBiotype.Default), "Random profiles should leave the biotype alone.");
                Assert.That(HumanoidCharacterProfile.DefaultWithSpecies("IPC").Biotype, Is.EqualTo(WFBiotype.Default));
                Assert.That(((HumanoidCharacterProfile) ipc.Validated(pair.Player!, IoCManager.Instance!)).Biotype,
                    Is.EqualTo(WFBiotype.Synthetic), "Validation should turn Default into the species' own.");
                Assert.That(((HumanoidCharacterProfile) human.Validated(pair.Player!, IoCManager.Instance!)).Biotype,
                    Is.EqualTo(WFBiotype.Organic));
                Assert.That(((HumanoidCharacterProfile) human.WithBiotype((WFBiotype) 7).Validated(pair.Player!, IoCManager.Instance!)).Biotype,
                    Is.EqualTo(WFBiotype.Organic), "An unknown value should fall back to the species' own.");
                Assert.That(organic.Biotype, Is.EqualTo(WFBiotype.Organic), "Validation dropped a non-native choice.");
                Assert.That(organic.Clone().Biotype, Is.EqualTo(WFBiotype.Organic), "Copies drop the biotype.");
                Assert.That(organic.MemberwiseEquals(organic.WithBiotype(WFBiotype.Synthetic)), Is.False);
                Assert.That(organic.GetHashCode(), Is.Not.EqualTo(organic.WithBiotype(WFBiotype.Synthetic).GetHashCode()));
                Assert.That(proto.HasIndex<SpeciesPrototype>(organic.Species));
            });
        });

        await db.InitPrefsAsync(user, organic);
        var prefs = await db.GetPlayerPreferencesAsync(user);
        var loaded = (HumanoidCharacterProfile) prefs!.Characters.Single().Value;
        Assert.That(loaded.Biotype, Is.EqualTo(WFBiotype.Organic), "The biotype was not saved.");

        await pair.CleanReturnAsync();
    }

    private static ServerDbSqlite GetDb(RobustIntegrationTest.ServerIntegrationInstance server)
    {
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var opsLog = server.ResolveDependency<ILogManager>().GetSawmill("db.ops");
        var builder = new DbContextOptionsBuilder<SqliteServerDbContext>();
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        builder.UseSqlite(conn);
        return new ServerDbSqlite(() => builder.Options, true, cfg, true, opsLog);
    }
}
