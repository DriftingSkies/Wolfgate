#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.JudgeLine;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server._NF.Bank;
using Content.Server._WG.NpcCrew;
using Content.Server.Mind;
using Content.Server.Shuttles.Components;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WG.Encounters;
using Content.Shared._WG.JudgeLine;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Systems;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Station.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WG.Encounters;

/// <summary>
/// The Judge Line: a call brings a judge responder to the caller, which deals with the real emergency, the chosen one or
/// not, and counts a call with none as a hoax.
/// </summary>
public sealed partial class WGEncounterTest
{
    /// <summary>Runs a test with the responder dispatched at once and dropping out of FTL close by.</summary>
    private async Task WithQuickJudges(Func<Task> test)
    {
        var config = Server.ResolveDependency<IConfigurationManager>();
        await Server.WaitPost(() =>
        {
            config.SetCVar(JudgeLineCVars.DispatchDelay, 0f);
            config.SetCVar(JudgeLineCVars.ArrivalDistance, 400f);
        });
        try
        {
            await test();
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                var line = Server.System<WGJudgeLineSystem>();
                foreach (var call in line.Calls.Where(c => c.Caller == SPlayer).ToList())
                {
                    line.Cancel(call);
                    if (call.Encounter is { } encounter && SEntMan.EntityExists(encounter))
                        Server.System<WGEncounterSystem>().End(encounter);
                }

                line.Pardon(SEntMan.GetComponent<ActorComponent>(SPlayer).PlayerSession.UserId);
                line.SectorOverride = null;
                config.SetCVar(JudgeLineCVars.Responders, JudgeLineCVars.Responders.DefaultValue);
                config.SetCVar(JudgeLineCVars.DispatchDelay, JudgeLineCVars.DispatchDelay.DefaultValue);
                config.SetCVar(JudgeLineCVars.ArrivalDistance, JudgeLineCVars.ArrivalDistance.DefaultValue);
            });
        }
    }

    /// <summary>
    /// A medical call from a ship that is really under fire: the responder takes the fight instead, allies with the
    /// caller's ship and turns on the ship that hit it.
    /// </summary>
    [Test]
    public async Task JudgeLineAnswersTheRealEmergencyAndFightsTheAttacker()
    {
        await WithQuickJudges(async () =>
        {
            var line = Server.System<WGJudgeLineSystem>();
            var alerts = Server.System<WGCrewAlertSystem>();
            var escorts = Server.System<WGCrewEscortSystem>();
            var at = new Vector2(-90000, -90000);
            WGJudgeLineCall call = default!;
            EntityUid caller = default, raider = default;
            await Server.WaitAssertion(() =>
            {
                caller = ClampDeck(at).Grid;
                raider = ClampDeck(at + new Vector2(150, 0)).Grid;
                SEntMan.GetComponent<ShuttleComponent>(caller).LinearThrust[0] = 1000f;
                Server.System<SharedTransformSystem>().SetCoordinates(SPlayer, new EntityCoordinates(caller, new Vector2(1.5f, 0.5f)));
                line.RecordHit(caller, raider);
                call = line.Place(SPlayer, WGJudgeLineKind.Medical);
                Assert.That(call.RealAtCall, Is.True, "A hit ship is a real emergency.");
            });

            await RunTicks(Ticks(4));
            await Server.WaitAssertion(() =>
            {
                var ship = line.Ship(call);
                Assert.That(ship, Is.Not.Null, $"The responder is out (phase {call.Phase}).");
                Assert.That(call.Arrived, Is.Not.Null, "It has arrived.");
                Assert.That(call.Kind, Is.EqualTo(WGJudgeLineKind.UnderFire), "It deals with the real emergency.");
                Assert.That(escorts.AreInFormation(ship!.Grid, caller), Is.True, "It sides with the caller's ship.");
                Assert.That(alerts.IsHostileShip(ship.Grid, ship.Group, raider), Is.True, "It takes the attacker for hostile.");
                var roles = new List<string>();
                var crew = SEntMan.EntityQueryEnumerator<WGCrewComponent>();
                while (crew.MoveNext(out var member))
                {
                    if (member.Group == ship.Group && member.Role is { } role)
                        roles.Add(role.Id);
                }

                Assert.That(roles.Count(role => role == "WGCrewMarine"), Is.EqualTo(2), $"Two guards aboard: {string.Join(", ", roles)}.");
                Assert.That(roles, Does.Contain("WGCrewParamedic"), "A paramedic aboard.");
                Server.System<WGEncounterSystem>().End(call.Encounter!.Value);
            });
        });
    }

    /// <summary>A call with no emergency, then or on arrival, is a hoax: a strike, and the responder leaves.</summary>
    [Test]
    public async Task JudgeLineCountsAHoax()
    {
        await WithQuickJudges(async () =>
        {
            var line = Server.System<WGJudgeLineSystem>();
            var at = new Vector2(-90000, 90000);
            WGJudgeLineCall call = default!;
            var strikes = 0;
            await Server.WaitAssertion(() =>
            {
                Assert.That(line.IsVagrant(SPlayer), Is.False, "Someone with no job is no vagrant.");
                Assert.That(line.Refusal(SPlayer), Is.EqualTo(Server.ResolveDependency<ILocalizationManager>().GetString("wg-judgeline-refuse-not-vagrant")));
                strikes = line.Strikes(SEntMan.GetComponent<ActorComponent>(SPlayer).PlayerSession.UserId);
                var caller = ClampDeck(at).Grid;
                SEntMan.GetComponent<ShuttleComponent>(caller).LinearThrust[0] = 1000f;
                Server.System<SharedTransformSystem>().SetCoordinates(SPlayer, new EntityCoordinates(caller, new Vector2(1.5f, 0.5f)));
                call = line.Place(SPlayer, WGJudgeLineKind.Boarded);
                Assert.That(call.RealAtCall, Is.False);
            });

            await RunTicks(Ticks(4));
            await Server.WaitAssertion(() =>
            {
                Assert.That(call.Phase, Is.EqualTo(WGJudgeLinePhase.Leaving), "The responder leaves.");
                var user = SEntMan.GetComponent<ActorComponent>(SPlayer).PlayerSession.UserId;
                Assert.That(line.Strikes(user), Is.EqualTo(strikes + 1), "The hoax counts against the caller.");
                Assert.That(line.CallOf(SPlayer), Is.Null);
                Assert.That(SEntMan.GetComponent<WGEncounterComponent>(call.Encounter!.Value).Resolution,
                    Is.EqualTo(WGEncounterResolution.Completed));
                Server.System<WGEncounterSystem>().End(call.Encounter!.Value);
            });
        });
    }

    /// <summary>
    /// A vagrant's call takes the fee. Cancelled while queued, it is refunded and starts the cooldown; cancelled once a
    /// responder is dispatched, the fee is kept.
    /// </summary>
    [Test]
    public async Task JudgeLineChargesTheFeeAndRefundsOnlyAQueuedCancel()
    {
        await WithQuickJudges(async () =>
        {
            var line = Server.System<WGJudgeLineSystem>();
            var bank = Server.System<BankSystem>();
            var config = Server.ResolveDependency<IConfigurationManager>();
            var fee = config.GetCVar(JudgeLineCVars.Fee);
            EntityUid mindId = default;
            await Server.WaitAssertion(() =>
            {
                line.SectorOverride = MapData.MapId;
                var grid = ClampDeck(new Vector2(90000, -90000)).Grid;
                Server.System<SharedTransformSystem>().SetCoordinates(SPlayer, new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));

                var minds = Server.System<MindSystem>();
                mindId = minds.CreateMind(ServerSession.UserId).Owner;
                minds.TransferTo(mindId, SPlayer, ghostCheckOverride: true, createGhost: false);
                Server.System<SharedRoleSystem>().MindAddJobRole(mindId, silent: true, jobPrototype: SharedGameTicker.FallbackOverflowJob);
                Assert.That(line.IsVagrant(SPlayer), Is.True, "A Contractor in no company is a vagrant.");

                SEntMan.EnsureComponent<BankAccountComponent>(SPlayer);
                bank.TryGetBalance(SPlayer, out var balance);
                if (balance < fee * 2)
                    bank.TryBankDeposit(SPlayer, fee * 2 - balance, tax: false);
                bank.TryGetBalance(SPlayer, out var before);

                config.SetCVar(JudgeLineCVars.Responders, 0);
                Assert.That(line.TryCall(SPlayer, WGJudgeLineKind.Stranded, out var refusal), Is.True, refusal);
                bank.TryGetBalance(SPlayer, out var charged);
                Assert.That(charged, Is.EqualTo(before - fee), "The call takes the fee.");
                Assert.That(line.CallOf(SPlayer)!.Phase, Is.EqualTo(WGJudgeLinePhase.Queued), "No responder is free.");
                Assert.That(line.Refusal(SPlayer), Is.Not.Null, "One call at a time.");

                Assert.That(line.Cancel(SPlayer), Is.True);
                bank.TryGetBalance(SPlayer, out var refunded);
                Assert.That(refunded, Is.EqualTo(before), "A queued cancel is refunded.");
                Assert.That(line.CallOf(SPlayer), Is.Null);
                Assert.That(line.Refusal(SPlayer), Is.Not.Null, "Closing a call starts the cooldown.");

                line.Pardon(SEntMan.GetComponent<ActorComponent>(SPlayer).PlayerSession.UserId);
                config.SetCVar(JudgeLineCVars.Responders, 2);
                Assert.That(line.TryCall(SPlayer, WGJudgeLineKind.Stranded, out refusal), Is.True, refusal);
                Assert.That(line.CallOf(SPlayer)!.Phase, Is.EqualTo(WGJudgeLinePhase.Dispatched));
                Assert.That(line.Cancel(SPlayer), Is.True);
                bank.TryGetBalance(SPlayer, out var kept);
                Assert.That(kept, Is.EqualTo(before - fee), "A dispatched cancel keeps the fee.");

                Server.System<SharedRoleSystem>().MindRemoveRole<JobRoleComponent>(mindId);
            });
        });
    }

    /// <summary>
    /// A caller down in open space is pulled aboard once the judges are alongside and flown to the nearest station; a
    /// station that turns the ship away is passed over for the next.
    /// </summary>
    [Test]
    public async Task JudgeLineHaulsADownedCallerAndTriesTheNextStation()
    {
        await WithQuickJudges(async () =>
        {
            var line = Server.System<WGJudgeLineSystem>();
            var transforms = Server.System<SharedTransformSystem>();
            var at = new Vector2(90000, 90000);
            WGJudgeLineCall call = default!;
            EntityUid near = default, far = default, caller = default;
            try
            {
                await Server.WaitAssertion(() =>
                {
                    near = ClampDeck(at + new Vector2(2000, 0)).Grid;
                    far = ClampDeck(at + new Vector2(5000, 0)).Grid;
                    SEntMan.EnsureComponent<StationMemberComponent>(near);
                    SEntMan.EnsureComponent<StationMemberComponent>(far);
                    caller = SEntMan.SpawnEntity("MobHuman", new MapCoordinates(at, MapData.MapId));
                    // Down and kept so: open space would otherwise hurt them and their thresholds set them back on their feet.
                    Server.System<SharedGodmodeSystem>().EnableGodmode(caller);
                    Server.System<MobStateSystem>().ChangeMobState(caller, MobState.Critical);
                    Assert.That(Server.System<MobStateSystem>().IsIncapacitated(caller), Is.True, "The caller is down.");
                    call = line.Place(caller, WGJudgeLineKind.Adrift);
                });

                await RunTicks(Ticks(4));
                await Server.WaitAssertion(() =>
                {
                    var ship = line.Ship(call);
                    Assert.That(ship, Is.Not.Null, $"The responder is out (phase {call.Phase}).");
                    transforms.SetWorldPosition(ship!.Grid, at + new Vector2(40, 0));
                });

                await RunTicks(Ticks(4));
                await Server.WaitAssertion(() =>
                {
                    var ship = line.Ship(call)!;
                    Assert.That(SEntMan.GetComponent<TransformComponent>(caller).GridUid, Is.EqualTo(ship.Grid), "The downed caller is pulled aboard.");
                    Assert.That(call.Station, Is.EqualTo(near), "Flown to the nearest station.");
                    var pilot = SEntMan.EntityQuery<WGCrewComponent>().First(crew => crew.Group == ship.Group).Owner;
                    var failed = new WGPilotDockFailedEvent(pilot, ship.Grid, near);
                    SEntMan.EventBus.RaiseEvent(EventSource.Local, ref failed);
                });

                await RunTicks(Ticks(4));
                await Server.WaitAssertion(() =>
                {
                    Assert.That(call.Station, Is.EqualTo(far), "Turned away, it heads for the next one.");
                    Server.System<WGEncounterSystem>().End(call.Encounter!.Value);
                });
            }
            finally
            {
                await Server.WaitPost(() =>
                {
                    line.Cancel(call);
                    SEntMan.DeleteEntity(caller);
                    SEntMan.DeleteEntity(near);
                    SEntMan.DeleteEntity(far);
                });
            }
        });
    }
}
