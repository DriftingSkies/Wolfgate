using System.Collections.Generic;
using Content.Server.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Performance;

/// <summary>
/// Bottomless pools such as water and blood tiles never run low, so regenerating them only costs a check per tile
/// every tick.
/// </summary>
[TestFixture]
public sealed class WFBottomlessRegenTest
{
    private const int Bottomless = 1_000_000;

    [Test]
    public async Task BottomlessPoolsDoNotRegenerate()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var factory = server.EntMan.ComponentFactory;
        var offenders = new List<string>();

        await server.WaitAssertion(() =>
        {
            foreach (var proto in server.ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract ||
                    !proto.TryGetComponent<SolutionRegenerationComponent>(out var regen, factory) ||
                    !proto.TryGetComponent<SolutionContainerManagerComponent>(out var manager, factory) ||
                    manager.Solutions is not { } solutions)
                {
                    continue;
                }

                foreach (var (name, solution) in solutions)
                {
                    if (solution.MaxVolume >= Bottomless)
                        offenders.Add($"{proto.ID} ({name}, regenerating {regen.SolutionName})");
                }
            }

            Assert.That(offenders, Is.Empty, "These bottomless pools regenerate for nothing.");
        });

        await pair.CleanReturnAsync();
    }
}
