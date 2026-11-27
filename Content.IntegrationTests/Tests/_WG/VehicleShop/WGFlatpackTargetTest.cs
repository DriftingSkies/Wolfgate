#nullable enable
using Content.Shared.Construction.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

#pragma warning disable RA0002 // reads FlatpackComponent.Entity

namespace Content.IntegrationTests.Tests._WG.VehicleShop;

/// <summary>Every flatpack builds something with fixtures; the flatpack system won't unpack anything else.</summary>
[TestFixture]
public sealed class WGFlatpackTargetTest
{
    [Test]
    public async Task EveryFlatpackTargetHasFixtures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var factory = server.EntMan.ComponentFactory;

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var proto in server.ProtoMan.EnumeratePrototypes<EntityPrototype>())
                {
                    if (proto.Abstract || !proto.TryGetComponent<FlatpackComponent>(out var flatpack, factory) || flatpack.Entity is not { } target)
                        continue;

                    Assert.That(server.ProtoMan.Index(target).TryGetComponent<FixturesComponent>(out _, factory),
                        $"{proto.ID} unpacks {target}, which has no fixtures and so never unpacks");
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
