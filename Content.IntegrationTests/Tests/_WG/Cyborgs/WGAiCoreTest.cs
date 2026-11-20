#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._CorvaxNext.Silicons.Borgs;
using Content.Server._WG.Cyborgs;
using Content.Server.Ghost;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Preferences.Managers;
using Content.Shared._CorvaxNext.Silicons.Borgs.Components;
using Content.Shared._Mono.Company;
using Content.Shared._Rat.Overwatch;
using Content.Shared.Mind;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.Cyborgs;

/// <summary>Faction AI cores reopen when abandoned, take the player's AI name, reach linked borgs off-grid and view overwatch feeds; built faction borgs carry their company.</summary>
[TestFixture]
[TestOf(typeof(WGAiCoreGhostRoleSystem))]
public sealed class WGAiCoreTest
{
    private const string AiName = "Testing Designation";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WGAiCoreTestBorg
  components:
  - type: MindContainer
  - type: MobState
  - type: Actions
  - type: AiRemoteController
";

    [Test]
    public async Task AbandonedCoreReopensTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var session = pair.Player!;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();

        var aiLoadout = new RoleLoadout("JobBorg") { EntityName = AiName };
        var profile = ((HumanoidCharacterProfile) prefs.GetPreferences(session.UserId).SelectedCharacter).WithLoadout(aiLoadout);
        var slot = prefs.GetPreferences(session.UserId).SelectedCharacterIndex;
        Task? save = null;
        await server.WaitPost(() => save = prefs.SetProfile(session.UserId, slot, profile));
        await PoolManager.WaitUntil(server, () => save!.IsCompleted, maxTicks: 300);
        Assert.That(save!.IsCompletedSuccessfully, $"The profile save failed: {save.Exception}");

        EntityUid core = default;
        EntityUid brain = default;
        await server.WaitPost(() =>
        {
            core = entMan.SpawnEntity("PlayerStationAiTSFMC", map.GridCoords);
            Assert.That(entMan.System<SharedStationAiSystem>().TryGetHeld(new Entity<StationAiCoreComponent?>(core, null), out brain), "The core spawned empty.");
        });
        await pair.RunTicksSync(5);

        // Taking the role names the AI after the player's Station AI loadout.
        await Take(pair, brain);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<MetaDataComponent>(brain).EntityName, Does.StartWith(AiName));
            Assert.That(entMan.GetComponent<MetaDataComponent>(core).EntityName, Does.StartWith(AiName));
        });

        // Ghosting out of the core puts the role back on the list.
        await Ghost(pair);
        await pair.RunSeconds(6);
        await server.WaitAssertion(() => AssertOpen(entMan, brain, true));

        // Taken again, then out in a borg: the core stays taken.
        await Take(pair, brain);
        EntityUid borg = default;
        await server.WaitPost(() =>
        {
            borg = entMan.SpawnEntity("WGAiCoreTestBorg", map.GridCoords);
            entMan.System<AiRemoteControlSystem>().AiTakeControl(brain, borg);
        });
        await pair.RunSeconds(6);
        await server.WaitAssertion(() =>
        {
            Assert.That(session.AttachedEntity, Is.EqualTo(borg), "The AI never reached the borg.");
            Assert.That(entMan.GetComponent<WGAiRemoteLinkComponent>(borg).Ai, Is.EqualTo(brain));
            AssertOpen(entMan, brain, false);
        });

        // Ghosting out of the borg abandons the core too.
        await Ghost(pair);
        await pair.RunSeconds(6);
        await server.WaitAssertion(() =>
        {
            AssertOpen(entMan, brain, true);
            var remote = entMan.GetComponent<AiRemoteControllerComponent>(borg);
            Assert.That(remote.AiHolder, Is.Null, "The borg still hands the ghosted player back to the reopened core.");
            Assert.That(remote.LinkedMind, Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>An AI keeps its borg when its core is destroyed, and deleting that borg afterwards doesn't throw.</summary>
    [Test]
    public async Task CoreDestroyedWhileInBorgTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var session = pair.Player!;

        EntityUid core = default;
        EntityUid brain = default;
        EntityUid borg = default;
        await server.WaitPost(() =>
        {
            core = entMan.SpawnEntity("PlayerStationAiTSFMC", map.GridCoords);
            entMan.System<SharedStationAiSystem>().TryGetHeld(new Entity<StationAiCoreComponent?>(core, null), out brain);
            borg = entMan.SpawnEntity("WGAiCoreTestBorg", map.GridCoords);
        });
        await pair.RunTicksSync(5);

        await Take(pair, brain);
        await server.WaitPost(() =>
        {
            entMan.System<AiRemoteControlSystem>().AiTakeControl(brain, borg);
            entMan.DeleteEntity(core);
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(brain), "Deleting the core left its brain behind.");
            Assert.That(session.AttachedEntity, Is.EqualTo(borg), "The AI was thrown out of its borg when the core went.");
        });

        await server.WaitPost(() => entMan.DeleteEntity(borg));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() => Assert.That(entMan.Deleted(borg)));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LinkedBorgOffGridTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid brain = default;
        EntityUid linked = default;
        EntityUid stranger = default;
        await server.WaitPost(() =>
        {
            var core = entMan.SpawnEntity("PlayerStationAiTSFMC", map.GridCoords);
            entMan.System<SharedStationAiSystem>().TryGetHeld(new Entity<StationAiCoreComponent?>(core, null), out brain);
            linked = entMan.SpawnEntity("WGAiCoreTestBorg", map.GridCoords);
            entMan.System<WGAiRemoteLinkSystem>().Link(brain, linked);
        });
        await pair.RunTicksSync(5);

        await Take(pair, brain);
        await server.WaitAssertion(() =>
        {
            var space = new EntityCoordinates(map.MapUid, new Vector2(500f, 500f));
            var xforms = entMan.System<SharedTransformSystem>();
            xforms.SetCoordinates(linked, space);
            stranger = entMan.SpawnEntity("WGAiCoreTestBorg", space);

            var links = entMan.System<WGAiRemoteLinkSystem>();
            Assert.That(entMan.GetComponent<TransformComponent>(linked).GridUid, Is.Null, "The borg is still on the grid.");
            Assert.That(links.CanReach(brain, linked), Is.True, "A linked borg off the grid is out of reach.");
            Assert.That(links.CanReach(brain, stranger), Is.False, "An unlinked borg off the grid is in reach.");

            entMan.System<AiRemoteControlSystem>().AiTakeControl(brain, linked);
            Assert.That(entMan.System<SharedMindSystem>().TryGetMind(linked, out _, out _), Is.True,
                "The AI couldn't take its linked borg off the grid.");

            // Pulling the network device breaks the link.
            entMan.RemoveComponent<AiRemoteControllerComponent>(linked);
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() => Assert.That(entMan.HasComponent<WGAiRemoteLinkComponent>(linked), Is.False));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FactionAiCompanyTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var stationAi = entMan.System<SharedStationAiSystem>();
            foreach (var (coreProto, requisition, overwatch) in new[]
                     {
                         ("PlayerStationAiTSFMC", "VendingMachineTSFMCUplink", "TSFOverwatchComputer"),
                         ("PlayerStationAiPDV", "VendingMachinePDVUplink", "PDVOverwatchComputer"),
                     })
            {
                var core = entMan.SpawnEntity(coreProto, map.GridCoords);
                Assert.That(stationAi.TryGetHeld(new Entity<StationAiCoreComponent?>(core, null), out var brain));

                foreach (var machineProto in new[] { requisition, overwatch })
                {
                    var machine = entMan.SpawnEntity(machineProto, map.GridCoords);
                    var required = entMan.GetComponent<CompanyAccessReaderComponent>(machine).RequiredCompanies;
                    Assert.That(entMan.TryGetComponent<CompanyComponent>(brain, out var company)
                                && required.Contains(company.CompanyName),
                        $"{coreProto} doesn't pass the company reader on {machineProto}.");

                    entMan.DeleteEntity(machine);
                }

                entMan.DeleteEntity(core);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FactionBorgChassisCompanyTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            foreach (var (chassisProto, requisition) in new[]
                     {
                         ("BorgChassisSelectableTSF", "VendingMachineTSFMCUplink"),
                         ("BorgChassisSelectablePDV", "VendingMachinePDVUplink"),
                     })
            {
                var chassis = entMan.SpawnEntity(chassisProto, map.GridCoords);
                var machine = entMan.SpawnEntity(requisition, map.GridCoords);
                var required = entMan.GetComponent<CompanyAccessReaderComponent>(machine).RequiredCompanies;
                Assert.That(entMan.TryGetComponent<CompanyComponent>(chassis, out var company)
                            && required.Contains(company.CompanyName),
                    $"A built {chassisProto} doesn't pass the company reader on {requisition}.");

                entMan.DeleteEntity(machine);
                entMan.DeleteEntity(chassis);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AiOverwatchViewTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid eye = default;
        await server.WaitAssertion(() =>
        {
            var stationAi = entMan.System<SharedStationAiSystem>();
            var core = entMan.SpawnEntity("PlayerStationAiTSFMC", map.GridCoords);
            Assert.That(stationAi.TryGetHeld(new Entity<StationAiCoreComponent?>(core, null), out var brain));
            Assert.That(entMan.GetComponent<StationAiCoreComponent>(core).RemoteEntity, Is.Not.Null);
            eye = entMan.GetComponent<StationAiCoreComponent>(core).RemoteEntity!.Value;

            var console = entMan.SpawnEntity("TSFOverwatchComputer", map.GridCoords);
            var view = new BoundUserInterfaceMessageAttempt(brain, console, OverwatchUiKey.Key,
                new OverwatchViewCameraMessage(NetEntity.Invalid));
            entMan.EventBus.RaiseLocalEvent(console, view);
            Assert.That(view.Cancelled, Is.False, "An AI was refused an overwatch camera.");
            Assert.That(entMan.TryGetComponent<WGAiOverwatchEyeComponent>(eye, out var marker) && marker.Ai == brain,
                "The AI's remote eye wasn't marked while it watches.");

            // Stand-in for OverwatchSystem.OnViewCamera, which needs a worn camera on a faction member.
            var target = entMan.SpawnEntity(null, map.GridCoords);
            entMan.System<SharedEyeSystem>().SetTarget(brain, target);
            entMan.AddComponent<RatOverwatchWatchingComponent>(brain).Watching = target;

            var mover = entMan.GetComponent<InputMoverComponent>(eye);
            mover.HeldMoveButtons = MoveButtons.Up;
            var move = new MoveInputEvent((eye, mover), MoveButtons.None);
            entMan.EventBus.RaiseLocalEvent(eye, ref move);
            mover.HeldMoveButtons = MoveButtons.None;

            Assert.That(entMan.HasComponent<RatOverwatchWatchingComponent>(brain), Is.False,
                "Moving the AI's eye didn't stop the feed.");
            Assert.That(entMan.GetComponent<EyeComponent>(brain).Target, Is.EqualTo(eye),
                "The AI's view didn't return to its remote eye.");
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() => Assert.That(entMan.HasComponent<WGAiOverwatchEyeComponent>(eye), Is.False));

        await pair.CleanReturnAsync();
    }

    private static async Task Take(Pair.TestPair pair, EntityUid brain)
    {
        var session = pair.Player!;
        await pair.Server.WaitPost(() =>
        {
            var take = new TakeGhostRoleEvent(session);
            pair.Server.EntMan.EventBus.RaiseLocalEvent(brain, ref take);
            Assert.That(take.TookRole, "The AI ghost role couldn't be taken.");
        });
        await pair.RunTicksSync(5);
    }

    private static async Task Ghost(Pair.TestPair pair)
    {
        var session = pair.Player!;
        await pair.Server.WaitPost(() =>
        {
            var minds = pair.Server.EntMan.System<SharedMindSystem>();
            Assert.That(minds.TryGetMind(session, out var mindId, out var mind));
            Assert.That(pair.Server.EntMan.System<GhostSystem>().OnGhostAttempt(mindId, true, viaCommand: true, mind: mind));
        });
        await pair.RunTicksSync(5);
    }

    private static void AssertOpen(IEntityManager entMan, EntityUid brain, bool open)
    {
        var listed = entMan.TryGetComponent<GhostRoleComponent>(brain, out var role)
                     && entMan.HasComponent<GhostTakeoverAvailableComponent>(brain)
                     && !role.Taken
                     && entMan.System<GhostRoleSystem>().GetGhostRolesInfo(null).Any(r => r.Identifier == role.Identifier);
        Assert.That(listed, Is.EqualTo(open), open ? "The abandoned core's ghost role wasn't reopened." : "The core was reopened while its AI was in a borg.");
    }
}
