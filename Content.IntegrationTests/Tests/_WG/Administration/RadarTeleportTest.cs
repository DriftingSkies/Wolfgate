#nullable enable
using System.Numerics;
using System.Collections.Generic;
using System.Reflection;
using Content.Client._WG.Administration.UI.AdminRadar;
using Content.Client.Shuttles.UI;
using Content.Client.UserInterface.Controls;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._WG.Administration.Systems;
using Content.Server.Administration.Managers;
using Content.Server.Shuttles.Systems;
using Content.Shared._WG.Administration.AdminRadar;
using Content.Shared._WG.Administration.RadarTeleport;
using Content.Shared.Administration;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Content.Shared.UserInterface;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.Administration;

/// <summary>
/// The admin mass scanner: an admin ghost gets the resizable, far-zooming window, can see past IFF, is told where
/// the players are and can jump to one, and with the toggle on is moved to the clicked spot. Nobody else gets any of it.
/// </summary>
[TestOf(typeof(RadarTeleportSystem))]
public sealed class RadarTeleportTest : InteractionTest
{
    private const string AdminGhost = "AdminObserver";
    private const string Ghost = "MobObserver";
    private const string Toggle = "AdminTeleportToggle";

    /// <summary>
    /// The toggle is off at first and a click does nothing; with it on, a click moves the ghost to the clicked spot.
    /// </summary>
    [Test]
    public async Task AdminGhostTeleportsToClick()
    {
        // The toggle state outlives the window, and the pooled client outlives the test.
        await Client.WaitPost(() => CEntMan.System<Content.Client._WG.Administration.RadarTeleport.RadarTeleportSystem>().Enabled = false);
        await SetAdmin(true);
        var ghost = await BecomeGhost(AdminGhost);
        var window = await OpenScanner<AdminRadarWindow>(ghost);
        var radar = GetControlFromField<ShuttleNavControl>("RadarScreen", window);
        var toggle = FindControl(window, Toggle) as Button;
        Assert.That(toggle, Is.Not.Null, "An admin ghost should get the teleport toggle.");
        Assert.That(toggle!.Pressed, Is.False, "The toggle should start off.");

        var start = await Position(ghost);
        await ClickControl(radar);
        await RunTicks(5);
        Assert.That(await Position(ghost), Is.EqualTo(start), "A click with the toggle off shouldn't teleport.");

        await ClickControl(toggle);
        Assert.That(toggle.Pressed, "Clicking the toggle should turn it on.");

        MapCoordinates expected = default;
        await Client.WaitPost(() =>
            expected = CEntMan.System<SharedTransformSystem>().ToMapCoordinates(radar.GetMouseCoordinatesFromCenter()));
        Assert.That((expected.Position - start.Position).Length(), Is.GreaterThan(1f), "The clicked spot should be away from the ghost.");

        await ClickControl(radar);
        await RunTicks(5);
        var end = await Position(ghost);
        Assert.Multiple(() =>
        {
            Assert.That(end.MapId, Is.EqualTo(expected.MapId));
            Assert.That((end.Position - expected.Position).Length(), Is.LessThan(0.01f), "The ghost should be at the clicked spot.");
        });
    }

    /// <summary>
    /// A ghost who isn't an admin gets the plain scanner with no toggle, and hand-sent requests from it or from an
    /// admin in a body are ignored.
    /// </summary>
    [Test]
    public async Task OnlyAdminGhostsTeleport()
    {
        await SetAdmin(false);
        var body = SPlayer;
        var ghost = await BecomeGhost(Ghost);
        var window = await OpenScanner<RadarConsoleWindow>(ghost);
        Assert.That(FindControl(window, Toggle), Is.Null, "A ghost who isn't an admin shouldn't get the toggle.");

        var start = await Position(ghost);
        await SendRequest(start);
        Assert.That(await Position(ghost), Is.EqualTo(start), "A request from a ghost who isn't an admin should be ignored.");

        await SetAdmin(true);
        await Server.WaitPost(() =>
            SEntMan.System<SharedMindSystem>().TransferTo(ServerSession.ContentData()!.Mind!.Value, body));
        await RunTicks(5);
        Assert.That(ServerSession.AttachedEntity, Is.EqualTo(body), "The player should be back in the body.");

        start = await Position(body);
        await SendRequest(start);
        Assert.That(await Position(body), Is.EqualTo(start), "A request from an admin in a body should be ignored.");
    }

    /// <summary>
    /// The admin scanner's display follows its window and maps clicks at that size, the zoom keeps its own limit when
    /// the console's range changes, and the maximize toggle fills the game window and gives the size back.
    /// </summary>
    [Test]
    public async Task AdminScannerFillsWindowAndZoomsOut()
    {
        var size = new Vector2(1000f, 600f);
        await SetAdmin(true);
        var ghost = await BecomeGhost(AdminGhost);
        var window = await OpenScanner<AdminRadarWindow>(ghost);
        var radar = GetControlFromField<AdminRadarControl>("RadarScreen", window);

        await Client.WaitPost(() => window.SetSize = size);
        await RunTicks(5);
        Assert.Multiple(() =>
        {
            Assert.That(radar.Width, Is.GreaterThan(900f), "The display should fill the window's width.");
            Assert.That(radar.Height, Is.InRange(450f, 600f), "The display should fill the window's height.");
        });

        // A click maps from the display's own centre, with the range spanning its longer side.
        var start = await Position(ghost);
        MapCoordinates clicked = default;
        Vector2 expected = default;
        await Client.WaitPost(() =>
        {
            clicked = CEntMan.System<SharedTransformSystem>().ToMapCoordinates(radar.GetMouseCoordinatesFromCenter());
            var mouse = Client.ResolveDependency<IUserInterfaceManager>().MousePositionScaled.Position;
            var pixel = (mouse - radar.GlobalPosition) * radar.UIScale - (Vector2) radar.PixelSize / 2f;
            var scale = MathF.Floor(MathF.Max(radar.PixelWidth, radar.PixelHeight) / 2f) / radar.WorldRange;
            expected = start.Position + new Vector2(pixel.X, -pixel.Y) / scale;
        });
        Assert.That((clicked.Position - expected).Length(), Is.LessThan(0.01f), "A click should map through the resized display.");

        var range = typeof(MapGridControl).GetField("ActualRadarRange", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await Client.WaitPost(() => radar.AddRadarRange(float.MaxValue));
        Assert.That(range.GetValue(radar), Is.EqualTo(AdminRadarControl.AdminMaxRange), "The admin scanner should zoom out to its own limit.");

        await Server.WaitPost(() =>
            SEntMan.System<RadarConsoleSystem>().SetRange(ghost, 1000f, SEntMan.GetComponent<RadarConsoleComponent>(ghost)));
        await RunTicks(10);
        Assert.That(range.GetValue(radar), Is.EqualTo(AdminRadarControl.AdminMaxRange), "A console state update shouldn't pull the zoom back in.");

        var maximize = GetControlFromField<Button>("MaximizeToggle", window);
        await ClickControl(maximize);
        await RunTicks(5);
        Assert.Multiple(() =>
        {
            Assert.That(window.Position, Is.EqualTo(Vector2.Zero), "A maximized scanner should sit at the corner.");
            Assert.That(window.Size, Is.EqualTo(window.Parent!.Size), "A maximized scanner should fill the game window.");
        });

        await ClickControl(maximize);
        await RunTicks(5);
        Assert.That(window.Size, Is.EqualTo(size), "Turning maximize off should give the old size back.");
    }

    /// <summary>
    /// Only an admin is told where the players are, bodies are told from ghosts, and a row in the searchable jump
    /// list warps the ghost to its player.
    /// </summary>
    [Test]
    public async Task AdminScannerListsAndJumpsToPlayers()
    {
        var radar = CEntMan.System<Content.Client._WG.Administration.AdminRadar.AdminRadarSystem>();
        var body = SEntMan.GetNetEntity(SPlayer);

        await SetAdmin(false);
        var before = radar.Players;
        await RequestPlayers(radar);
        Assert.That(radar.Players, Is.SameAs(before), "A player who isn't an admin shouldn't be sent the list.");

        await SetAdmin(true);
        await RequestPlayers(radar);
        Assert.That(radar.Players, Has.Count.EqualTo(1), "The list should hold the one connected player.");
        Assert.Multiple(() =>
        {
            Assert.That(radar.Players[0].Entity, Is.EqualTo(body));
            Assert.That(radar.Players[0].Username, Is.EqualTo(ServerSession.Name));
            Assert.That(radar.Players[0].Ghost, Is.False, "A body shouldn't be listed as a ghost.");
        });

        var ghost = await BecomeGhost(AdminGhost);
        await RequestPlayers(radar);
        Assert.That(radar.Players, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(radar.Players[0].Entity, Is.EqualTo(SEntMan.GetNetEntity(ghost)));
            Assert.That(radar.Players[0].Ghost, Is.True, "A ghost should be listed as one.");
        });

        // The old body stands in for another player, with the ghost well away from it.
        await Server.WaitPost(() => SEntMan.System<SharedTransformSystem>()
            .SetCoordinates(ghost, SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(8f, 0f))));
        await RunTicks(5);
        var target = await Position(SPlayer);
        Assert.That(((await Position(ghost)).Position - target.Position).Length(), Is.GreaterThan(5f));

        var window = await OpenScanner<AdminRadarWindow>(ghost);
        var panel = GetControlFromField<Control>("JumpPanel", window);
        var list = GetControlFromField<BoxContainer>("JumpList", window);
        var search = GetControlFromField<LineEdit>("JumpSearch", window);
        await ClickControl(GetControlFromField<Button>("JumpToggle", window));
        await RunTicks(5);
        Assert.Multiple(() =>
        {
            Assert.That(panel.Visible, "The toggle should open the jump list.");
            Assert.That(list.ChildCount, Is.Zero, "The admin's own ghost shouldn't be in the jump list.");
        });

        // A forced request restarts the once-a-second refresh, so the hand-made list isn't replaced mid-test.
        await RequestPlayers(radar);
        await Server.WaitPost(() => SEntMan.EntityNetManager!.SendSystemNetworkMessage(
            new AdminRadarPlayersEvent(new List<AdminRadarPlayer> { new(body, "Jump Target", "someone", PlayerCoords, false) }),
            ServerSession.Channel));
        await RunTicks(3);
        Assert.That(list.ChildCount, Is.EqualTo(1), "A listed player should get a row.");

        await Client.WaitPost(() => search.SetText("nobody", true));
        Assert.That(list.ChildCount, Is.Zero, "A search that matches nobody should leave no rows.");
        await Client.WaitPost(() => search.SetText("SOME", true));
        Assert.That(list.ChildCount, Is.EqualTo(1), "The search should match usernames, whatever the case.");

        await ClickControl(list.GetChild(0));
        await RunTicks(5);
        var end = await Position(ghost);
        Assert.Multiple(() =>
        {
            Assert.That((end.Position - target.Position).Length(), Is.LessThan(0.5f), "The ghost should be at the picked player.");
            Assert.That(panel.Visible, Is.False, "Picking a player should close the jump list.");
        });
    }

    /// <summary>
    /// Revealing lifts a hidden grid's IFF so it can be drawn and named, and restoring hides it again.
    /// </summary>
    [Test]
    public async Task TrueIffRevealsHiddenGrid()
    {
        await Server.WaitPost(() =>
            SEntMan.System<ShuttleSystem>().AddIFFFlag(MapData.Grid, IFFFlags.Hide | IFFFlags.HideLabel));
        await RunTicks(5);

        var grid = CEntMan.GetEntity(SEntMan.GetNetEntity(MapData.Grid.Owner));
        var shuttle = CEntMan.System<Content.Client.Shuttles.Systems.ShuttleSystem>();
        var hidden = new List<(IFFComponent, IFFFlags)>();
        await Client.WaitAssertion(() =>
        {
            Assert.That(shuttle.CanDraw(grid), Is.False, "The grid should start hidden.");
            Assert.That(shuttle.GetIFFLabel(grid), Is.Null, "A hidden grid should have no label.");

            shuttle.WgRevealIff(hidden);
            Assert.That(shuttle.CanDraw(grid), "A revealed grid should be drawn.");
            Assert.That(shuttle.GetIFFLabel(grid), Is.Not.Null, "A revealed grid should have its label.");

            shuttle.WgRestoreIff(hidden);
            Assert.That(shuttle.CanDraw(grid), Is.False, "Restoring should hide the grid again.");
            Assert.That(hidden, Is.Empty);
        });
    }

    /// <summary>
    /// Asks the server for the player list and waits for the answer.
    /// </summary>
    private async Task RequestPlayers(Content.Client._WG.Administration.AdminRadar.AdminRadarSystem radar)
    {
        await Client.WaitPost(() => radar.RequestPlayers(true));
        await RunTicks(5);
    }

    /// <summary>
    /// Sends a teleport request by hand for a spot 10 m away.
    /// </summary>
    private async Task SendRequest(MapCoordinates from)
    {
        await Client.WaitPost(() =>
            CEntMan.EntityNetManager!.SendSystemNetworkMessage(new RadarTeleportRequestEvent(from.Offset(new Vector2(10f, 10f)))));
        await RunTicks(5);
    }

    private new async Task<MapCoordinates> Position(EntityUid uid)
    {
        MapCoordinates position = default;
        await Server.WaitPost(() => position = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(uid));
        return position;
    }

    /// <summary>
    /// Opens the ghost's mass scanner and returns its window, which has to be of the given type.
    /// </summary>
    private async Task<T> OpenScanner<T>(EntityUid ghost) where T : BaseWindow
    {
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<IntrinsicUISystem>().InteractUI(ghost, RadarConsoleUiKey.Key), "The ghost's mass scanner should open."));
        await RunTicks(10);
        return GetWindow<T>();
    }

    private static Control? FindControl(Control parent, string name)
    {
        foreach (var child in parent.Children)
        {
            if (child.Name == name)
                return child;

            if (FindControl(child, name) is { } found)
                return found;
        }

        return null;
    }

    /// <summary>
    /// Makes the player an active admin or not, and waits for the client to hear about it.
    /// </summary>
    private async Task SetAdmin(bool admin)
    {
        var admins = Server.ResolveDependency<IAdminManager>();
        var clientAdmins = Client.ResolveDependency<Content.Client.Administration.Managers.IClientAdminManager>();
        await Server.WaitPost(() =>
        {
            if (!admin)
                admins.DeAdmin(ServerSession);
            else if (admins.IsAdmin(ServerSession, includeDeAdmin: true))
                admins.ReAdmin(ServerSession);
            else
                admins.PromoteHost(ServerSession);
        });

        for (var i = 0; i < 60 && clientAdmins.HasFlag(AdminFlags.Admin) != admin; i++)
        {
            await RunTicks(1);
        }

        Assert.Multiple(() =>
        {
            Assert.That(admins.HasAdminFlag(ServerSession, AdminFlags.Admin), Is.EqualTo(admin));
            Assert.That(clientAdmins.HasFlag(AdminFlags.Admin), Is.EqualTo(admin));
        });
    }

    /// <summary>
    /// Moves the player, with a new mind, into a fresh ghost.
    /// </summary>
    private async Task<EntityUid> BecomeGhost(string prototype)
    {
        EntityUid ghost = default;
        await Server.WaitPost(() =>
        {
            var minds = SEntMan.System<SharedMindSystem>();
            minds.WipeMind(ServerSession.ContentData()?.Mind);
            ghost = SEntMan.SpawnEntity(prototype, SEntMan.GetCoordinates(PlayerCoords));
            minds.TransferTo(minds.CreateMind(ServerSession.UserId).Owner, ghost);
        });

        await RunTicks(5);
        Assert.That(ServerSession.AttachedEntity, Is.EqualTo(ghost), "The player should be the ghost.");
        return ghost;
    }
}
