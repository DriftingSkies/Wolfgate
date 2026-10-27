#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client.Administration.Systems;
using Content.Client.Administration.UI.CustomControls;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Bwoink;
using Content.Shared.Administration;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._WF.AHelpResolve;

/// <summary>Clicking players in the ahelp list selects only that player and shows their chat.</summary>
[TestFixture]
public sealed class AHelpChannelSelectTest
{
    private const int FakePlayers = 3;

    [Test]
    public async Task ClickingAPlayerSelectsOnlyThem()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var ui = client.ResolveDependency<IUserInterfaceManager>();
        AdminAHelpUIHandler Helper() => (AdminAHelpUIHandler) ui.GetUIController<AHelpUIController>().UIHelper!;

        await client.WaitPost(() =>
        {
            var controller = ui.GetUIController<AHelpUIController>();
            controller.EnsureUIHelper();
            controller.ToggleWindow();
        });
        await pair.RunTicksSync(10);

        await client.WaitPost(() =>
        {
            var me = client.System<AdminSystem>().PlayerList.First();
            var players = new List<PlayerInfo> { me };
            for (var i = 0; i < FakePlayers; i++)
            {
                players.Add(me with { SessionId = new NetUserId(Guid.NewGuid()), Username = $"fake{i}" });
            }

            foreach (var player in players)
            {
                Helper().Receive(new SharedBwoinkSystem.BwoinkTextMessage(player.SessionId, player.SessionId, "help"));
            }

            Helper().Control!.ChannelSelector.PopulateList(players);
        });
        await pair.RunTicksSync(10);

        for (var n = 0; n <= FakePlayers; n++)
        {
            ListContainerButton? row = null;
            await client.WaitPost(() => row = Rows(Helper()).ElementAt(n));
            await Click(pair, row!);
            await pair.RunTicksSync(5);

            await client.WaitAssertion(() =>
            {
                var control = Helper().Control!;
                var player = ((PlayerListData) row!.Data).Info;
                Assert.That(Helper().TryGetChannel(player.SessionId, out var panel));
                Assert.Multiple(() =>
                {
                    Assert.That(Rows(Helper()).Count(b => b.Pressed), Is.EqualTo(1), $"click {n}: only the clicked row should stay selected");
                    Assert.That(control.BwoinkArea.Children.Where(c => c.Visible), Is.EqualTo(new[] { panel }), $"click {n}: only the clicked player's chat should show");
                });
            });
        }

        await client.WaitPost(() => Helper().Close());
        await pair.CleanReturnAsync();
    }

    private static IEnumerable<ListContainerButton> Rows(AdminAHelpUIHandler helper)
    {
        return helper.Control!.ChannelSelector.PlayerListContainer.Children.OfType<ListContainerButton>();
    }

    private static async Task Click(Pair.TestPair pair, Control control)
    {
        var coords = new ScreenCoordinates(control.GlobalPixelPosition + control.PixelSize / 2, control.Window?.Id ?? default);
        var relativePos = coords.Position / control.UIScale - control.GlobalPosition;
        var relativePixelPos = coords.Position - control.GlobalPixelPosition;
        foreach (var state in new[] { BoundKeyState.Down, BoundKeyState.Up })
        {
            await pair.Client.DoGuiEvent(control, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state, coords, default, relativePos, relativePixelPos));
            await pair.RunTicksSync(1);
        }
    }
}
