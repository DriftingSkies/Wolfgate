#nullable enable
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.ReaverCampaign.Components;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.Encounters;
using Content.Shared._WG.NpcCrew;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.ReaverCampaign;

/// <summary>A simulated stronghold answers a simulated attack on it as it does a real one.</summary>
[TestOf(typeof(WGEncounterSystem))]
public sealed class WGReaverSimulationTest : WGReaverTestBase
{
    private static ProtoId<WGEncounterPrototype> WGTestSimHaulerProto => "WGTestSimHauler";

    /// <summary>Each round of a simulated fight on a simulated hub counts as a hit by the attacker on the stronghold.</summary>
    [Test]
    public async Task ASimulatedAttackOnASimulatedHubCountsAsHits()
    {
        await StartCampaign();
        var config = Server.ResolveDependency<IConfigurationManager>();
        EntityUid stronghold = default, attacker = default;
        await Server.WaitAssertion(() =>
        {
            config.SetCVar(EncountersCVars.Simulate, true);
            config.SetCVar(EncountersCVars.MaterializeRange, 100f);
            // No losses: only the hits are wanted.
            config.SetCVar(EncountersCVars.DuelMeanSeconds, 1000000f);
            stronghold = FoundAt(Home);
            var hub = SEntMan.GetComponent<WGEncounterComponent>(stronghold).Ships[SEntMan.GetComponent<WGReaverStrongholdComponent>(stronghold).Lead];
            Assert.That(hub.Virtual, Is.True, "Nobody near: the hub is simulated.");

            var encounters = Server.System<WGEncounterSystem>();
            var hubAt = Server.System<SharedTransformSystem>().GetWorldPosition(hub.Grid);
            Assert.That(encounters.TrySpawn(ProtoMan.Index<WGEncounterPrototype>(WGTestSimHaulerProto),
                new MapCoordinates(hubAt + new Vector2(800f, 0f), MapData.MapId), out attacker, offBudget: true), Is.True);
            Assert.That(encounters.SetOrders(attacker, "ship", new List<WGCrewObjective>
            {
                new() { Kind = WGCrewObjectiveKind.Attack, Target = SEntMan.GetNetEntity(hub.Grid), Range = 600f },
            }), Is.True);
        });

        try
        {
            var hit = false;
            for (var i = 0; i < 15 && !hit; i++)
            {
                await RunTicks(60);
                await Server.WaitPost(() =>
                {
                    var attackerShip = SEntMan.GetComponent<WGEncounterComponent>(attacker).Ships["ship"];
                    hit = SEntMan.GetComponent<WGReaverStrongholdComponent>(stronghold).NpcHits.ContainsKey(attackerShip.Grid);
                });
            }

            Assert.That(hit, Is.True, "The stronghold counts the simulated attacker's hits.");
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                Server.System<WGEncounterSystem>().End(attacker);
                config.SetCVar(EncountersCVars.Simulate, false);
                config.SetCVar(EncountersCVars.MaterializeRange, EncountersCVars.MaterializeRange.DefaultValue);
                config.SetCVar(EncountersCVars.DuelMeanSeconds, EncountersCVars.DuelMeanSeconds.DefaultValue);
            });
        }
    }
}
