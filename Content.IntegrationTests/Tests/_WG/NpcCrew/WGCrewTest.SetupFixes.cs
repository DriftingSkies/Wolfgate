#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.Administration.Managers;
using Content.Server.NPC.HTN;
using Content.Shared._Mono.Company;
using Content.Shared._WG.NpcCrew;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>Makes the test session an admin and returns whether it already was one.</summary>
    private async Task<bool> FixesPromote()
    {
        var admins = Server.ResolveDependency<IAdminManager>();
        var wasAdmin = admins.IsAdmin(ServerSession);
        await Server.WaitPost(() =>
        {
            if (admins.IsAdmin(ServerSession, includeDeAdmin: true))
                admins.ReAdmin(ServerSession);
            else
                admins.PromoteHost(ServerSession);
        });
        return wasAdmin;
    }

    private async Task FixesRestore(bool wasAdmin)
    {
        if (!wasAdmin)
            await Server.WaitPost(() => Server.ResolveDependency<IAdminManager>().DeAdmin(ServerSession));
    }

    /// <summary>Spawns a pilot and a deckhand as one crew with their AI off. Server thread only.</summary>
    private List<EntityUid> FixesSpawn(EntityUid deck, string group, string company = "")
    {
        var posts = new List<WGCrewSetupPost>
        {
            new() { Role = WGCrewRoles.Pilot.Id, Position = new Vector2(2.5f) },
            new() { Role = WGCrewRoles.Deckhand.Id, Position = new Vector2(4.5f) },
        };
        var mission = new WGCrewMission { Group = group, Company = company };
        Assert.That(Server.System<WGCrewSetupSystem>().TrySpawn(deck, posts, mission, out var spawned), Is.True);
        foreach (var uid in spawned)
            SEntMan.GetComponent<HTNComponent>(uid).Enabled = false;
        return spawned;
    }

    /// <summary>A crewman on the hull is still his ship's crew: rules and Clear reach him by home ship, not by the grid under him.</summary>
    [Test]
    public async Task CrewSetupReachesCrewOffTheHomeGrid()
    {
        await AddAtmosphere();
        var deck = await CreateDeck(new Vector2(6, 0), 7, gravity: true);
        var wasAdmin = await FixesPromote();
        try
        {
            List<EntityUid> crew = new();
            await Server.WaitAssertion(() =>
            {
                FillCrewTestAir(deck);
                crew = FixesSpawn(deck, "offgrid");
                Server.System<SharedTransformSystem>().SetCoordinates(crew[1], new EntityCoordinates(MapData.MapUid, new Vector2(60, 0)));
                Assert.That(SEntMan.GetComponent<TransformComponent>(crew[1]).GridUid, Is.Not.EqualTo(deck));
                var setup = Server.System<WGCrewSetupSystem>();
                var grid = SEntMan.GetNetEntity(deck);

                var rules = setup.Handle(new WGCrewSetupRequest
                {
                    Action = WGCrewSetupAction.Rules, Grid = grid,
                    Mission = new WGCrewMission { Group = "offgrid", Battlegroup = "fleet" },
                }, ServerSession);
                Assert.That(rules!.Message, Is.Empty);
                Assert.That(SEntMan.GetComponent<WGCrewComponent>(crew[1]).Battlegroup, Is.EqualTo("fleet"));

                var cleared = setup.Handle(new WGCrewSetupRequest
                {
                    Action = WGCrewSetupAction.Clear, Grid = grid, Mission = new WGCrewMission { Group = "offgrid" },
                }, ServerSession);
                Assert.That(cleared!.Message, Is.Empty);
            });
            await RunTicks(3);
            await Server.WaitAssertion(() =>
            {
                foreach (var uid in crew)
                    Assert.That(SEntMan.Deleted(uid), Is.True, "Clear must remove the crewman who was off the ship too.");
            });
        }
        finally
        {
            await FixesRestore(wasAdmin);
        }
    }

    /// <summary>Choosing no company takes the company off the ship and every crewman.</summary>
    [Test]
    public async Task CrewSetupEmptyCompanyRemovesCompany()
    {
        await AddAtmosphere();
        var deck = await CreateDeck(new Vector2(6, 0), 7, gravity: true);
        await Server.WaitAssertion(() =>
        {
            FillCrewTestAir(deck);
            var crew = FixesSpawn(deck, "company", "MMC");
            Assert.That(SEntMan.GetComponent<CompanyComponent>(deck).CompanyName.Id, Is.EqualTo("MMC"));
            foreach (var uid in crew)
                Assert.That(SEntMan.HasComponent<CompanyComponent>(uid), Is.True);
            var mission = new WGCrewMission { Group = "company", Company = string.Empty, Order = WGPilotOrder.Hold };
            Assert.That(Server.System<WGCrewSetupSystem>().TryApplyMission(deck, mission), Is.True);
            Assert.That(SEntMan.HasComponent<CompanyComponent>(deck), Is.False);
            foreach (var uid in crew)
                Assert.That(SEntMan.HasComponent<CompanyComponent>(uid), Is.False);
        });
    }

    /// <summary>A queue never outlives its crew: clearing, or simply respawning over a deleted crew, starts with an empty queue.</summary>
    [TestCase("window")]
    [TestCase("console")]
    [TestCase("deleted")]
    public async Task CrewSetupRespawnDoesNotInheritOldQueue(string how)
    {
        await AddAtmosphere();
        var deck = await CreateDeck(new Vector2(6, 0), 7, gravity: true);
        var wasAdmin = await FixesPromote();
        try
        {
            await Server.WaitAssertion(() =>
            {
                FillCrewTestAir(deck);
                var crew = FixesSpawn(deck, "queued");
                var queue = Server.System<WGCrewObjectiveSystem>();
                Assert.That(queue.SetQueue(deck, "queued",
                    new List<WGCrewObjective> { new() { Kind = WGCrewObjectiveKind.Hold, Duration = 100 } }), Is.True);
                Assert.That(queue.Snapshot().Single(row => row.Group == "queued").Objectives, Has.Count.EqualTo(1));
                switch (how)
                {
                    case "window":
                        Server.System<WGCrewSetupSystem>().Handle(new WGCrewSetupRequest
                        {
                            Action = WGCrewSetupAction.Clear, Grid = SEntMan.GetNetEntity(deck),
                            Mission = new WGCrewMission { Group = "queued" },
                        }, ServerSession);
                        break;
                    case "console":
                        Server.ResolveDependency<IConsoleHost>().ExecuteCommand(ServerSession, "wg_crew clear queued");
                        break;
                    default:
                        foreach (var uid in crew)
                            SEntMan.DeleteEntity(uid);
                        break;
                }
            });
            await RunTicks(3);
            await Server.WaitAssertion(() =>
            {
                FixesSpawn(deck, "queued");
                Assert.That(Server.System<WGCrewObjectiveSystem>().Snapshot().Single(row => row.Group == "queued").Objectives, Is.Empty,
                    "The new crew must not run the old crew's queue.");
            });
        }
        finally
        {
            await FixesRestore(wasAdmin);
        }
    }

    /// <summary>Null fields and oversize text are rejected with a specific reason instead of throwing or being applied.</summary>
    [Test]
    public async Task CrewSetupRejectsNullAndOversizeFields()
    {
        await AddAtmosphere();
        var deck = await CreateDeck(new Vector2(6, 0), 7, gravity: true);
        var wasAdmin = await FixesPromote();
        try
        {
            await Server.WaitAssertion(() =>
            {
                FillCrewTestAir(deck);
                var setup = Server.System<WGCrewSetupSystem>();
                var loc = Server.ResolveDependency<ILocalizationManager>();
                var grid = SEntMan.GetNetEntity(deck);

                var hollow = new WGCrewSetupRequest
                {
                    Action = WGCrewSetupAction.Spawn, Grid = grid, Vessel = null!, Posts = null!, Objectives = null!,
                    Mission = new WGCrewMission
                    {
                        Group = null!, Callsign = null!, Battlegroup = null!, Company = null!, Faction = null!,
                        LocalChannel = null!, AlertChannel = null!, Navigation = null!,
                    },
                };
                WGCrewSetupResponse? reply = null;
                Assert.DoesNotThrow(() => reply = setup.Handle(hollow, ServerSession));
                Assert.That(reply!.Message, Is.Not.Empty);

                var noMission = new WGCrewSetupRequest { Action = WGCrewSetupAction.Clear, Grid = grid, Mission = null! };
                Assert.DoesNotThrow(() => reply = setup.Handle(noMission, ServerSession));
                Assert.That(reply!.Message, Is.Not.Empty);

                var nullPost = new WGCrewSetupRequest
                {
                    Action = WGCrewSetupAction.Spawn, Grid = grid, Posts = new List<WGCrewSetupPost> { null! },
                    Mission = new WGCrewMission { Group = "a9-null-post" },
                };
                Assert.DoesNotThrow(() => reply = setup.Handle(nullPost, ServerSession));
                Assert.That(reply!.Message, Is.EqualTo(loc.GetString("wg-crew-setup-invalid")));

                var valid = new List<WGCrewSetupPost> { new() { Position = new Vector2(1.5f) } };
                void Rejects(string key, Action<WGCrewSetupRequest> tweak)
                {
                    var request = new WGCrewSetupRequest
                    {
                        Action = WGCrewSetupAction.Spawn, Grid = grid, Posts = valid, Mission = new WGCrewMission { Group = "a9-oversize" },
                    };
                    tweak(request);
                    Assert.DoesNotThrow(() => reply = setup.Handle(request, ServerSession));
                    Assert.That(reply!.Message, Is.EqualTo(loc.GetString(key)), key);
                }

                Rejects("wg-crew-setup-group-too-long", request => request.Mission.Group = new string('g', 33));
                Rejects("wg-crew-setup-callsign-too-long", request => request.Mission.Callsign = new string('c', 101));
                Rejects("wg-crew-setup-battlegroup-too-long", request => request.Mission.Battlegroup = new string('b', 33));
                Rejects("wg-crew-setup-too-many-posts", request => request.Posts = Enumerable.Range(0, 65)
                    .Select(_ => new WGCrewSetupPost { Position = new Vector2(1.5f) }).ToList());
                Rejects("wg-crew-setup-too-many-objectives", request => request.Objectives = Enumerable.Range(0, 65)
                    .Select(_ => new WGCrewObjective { Kind = WGCrewObjectiveKind.Hold }).ToList());

                Assert.That(Server.System<WGCrewObjectiveSystem>().Snapshot().Any(row => row.Group.StartsWith("a9-")), Is.False,
                    "No rejected request may have created a crew.");
            });
        }
        finally
        {
            await FixesRestore(wasAdmin);
        }
    }

    /// <summary>Requests that would change nothing say so instead of reporting success.</summary>
    [Test]
    public async Task CrewSetupExplainsRequestsThatDidNothing()
    {
        await AddAtmosphere();
        var deck = await CreateDeck(new Vector2(6, 0), 7, gravity: true);
        var wasAdmin = await FixesPromote();
        try
        {
            await Server.WaitAssertion(() =>
            {
                FillCrewTestAir(deck);
                var setup = Server.System<WGCrewSetupSystem>();
                var loc = Server.ResolveDependency<ILocalizationManager>();
                var grid = SEntMan.GetNetEntity(deck);

                string Reply(WGCrewSetupAction action, string group) => setup.Handle(new WGCrewSetupRequest
                {
                    Action = action, Grid = grid, Mission = new WGCrewMission { Group = group },
                }, ServerSession)!.Message;

                Assert.That(Reply(WGCrewSetupAction.Clear, "nobody"), Is.EqualTo(loc.GetString("wg-crew-setup-no-crew")));
                FixesSpawn(deck, "idle");
                Assert.That(Reply(WGCrewSetupAction.Pause, "idle"), Is.EqualTo(loc.GetString("wg-crew-setup-no-queue")));
                Assert.That(Reply(WGCrewSetupAction.Skip, "idle"), Is.EqualTo(loc.GetString("wg-crew-setup-no-queue")));
            });
        }
        finally
        {
            await FixesRestore(wasAdmin);
        }
    }

    /// <summary>A second arena is refused while one exists, and "arena clear" removes its crews and ships.</summary>
    [Test]
    public async Task ArenaRefusesSecondRunAndClears()
    {
        var console = Server.ResolveDependency<IConsoleHost>();
        var wasAdmin = await FixesPromote();
        try
        {
            await Server.WaitPost(() => console.ExecuteCommand(ServerSession, "wg_crew arena"));
            await RunTicks(5);
            var grids = new List<NetEntity>();
            await Server.WaitAssertion(() =>
            {
                var crews = Server.System<WGCrewObjectiveSystem>().Snapshot().Where(crew => crew.Group.StartsWith("arena-")).ToList();
                Assert.That(crews, Has.Count.EqualTo(3));
                grids = crews.Select(crew => crew.Grid).ToList();
            });

            await Server.WaitPost(() => console.ExecuteCommand(ServerSession, "wg_crew arena"));
            await RunTicks(5);
            await Server.WaitAssertion(() => Assert.That(
                Server.System<WGCrewObjectiveSystem>().Snapshot().Count(crew => crew.Group.StartsWith("arena-")), Is.EqualTo(3),
                "A second run must be refused while the first arena exists."));

            await Server.WaitPost(() => console.ExecuteCommand(ServerSession, "wg_crew arena clear"));
            await RunTicks(5);
            await Server.WaitAssertion(() =>
            {
                Assert.That(Server.System<WGCrewObjectiveSystem>().Snapshot().Any(crew => crew.Group.StartsWith("arena-")), Is.False);
                foreach (var grid in grids)
                    Assert.That(SEntMan.Deleted(SEntMan.GetEntity(grid)), Is.True);
            });
        }
        finally
        {
            await FixesRestore(wasAdmin);
        }
    }
}
