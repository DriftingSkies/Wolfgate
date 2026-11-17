using System.Linq;
using Content.Client.UserInterface.Controls;
using Content.Shared._WG.RoundSetup;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WG.RoundSetup;

public sealed partial class WGRoundSetupWindow
{
    /// <summary>Picks the roster label for a member, using their condition once they have spawned.</summary>
    private static string MemberStatusKey(bool spawned, bool ready, WGCrewMemberStatus status) => !spawned
        ? ready ? "member-ready" : "member-not-ready"
        : status switch
        {
            WGCrewMemberStatus.Critical => "member-critical",
            WGCrewMemberStatus.Dead => "member-dead",
            WGCrewMemberStatus.Missing => "member-missing",
            _ => "member-alive",
        };

    private static string ShipConditionKey(WGCrewShipCondition condition) => condition switch
    {
        WGCrewShipCondition.Damaged => "ship-condition-damaged",
        WGCrewShipCondition.Wrecked => "ship-condition-wrecked",
        WGCrewShipCondition.Gone => "ship-condition-gone",
        _ => "ship-condition-intact",
    };

    private static Color ShipConditionColor(WGCrewShipCondition condition) => condition switch
    {
        WGCrewShipCondition.Damaged => Color.Gold,
        WGCrewShipCondition.Wrecked or WGCrewShipCondition.Gone => Color.OrangeRed,
        _ => Color.LightGreen,
    };

    private static bool NeedsJoinWarning(WGRoundSetupLobby lobby) =>
        WGCrewStatusPolicy.NeedsJoinWarning(lobby.Started, lobby.Roster.Select(mate => mate.Status).ToArray(), lobby.ShipCondition);

    /// <summary>Summarises a launched crew's living members and ship condition.</summary>
    private static Control CrewHealthSummary(WGRoundSetupLobby lobby)
    {
        var statuses = lobby.Roster.Select(mate => mate.Status).ToArray();
        var living = WGCrewStatusPolicy.Living(statuses);
        var deployed = WGCrewStatusPolicy.Deployed(statuses);
        var content = Column(2);
        var crew = new RichTextLabel { HorizontalExpand = true };
        crew.SetMessage(FormattedMessage.FromUnformatted(Text("crew-alive", ("living", living), ("deployed", deployed))),
            defaultColor: deployed > 0 && living == 0 ? Color.OrangeRed : Color.LightGreen);
        content.AddChild(crew);
        if (lobby.ShipCondition != WGCrewShipCondition.None)
        {
            var ship = new RichTextLabel { HorizontalExpand = true };
            ship.SetMessage(FormattedMessage.FromUnformatted(Text(ShipConditionKey(lobby.ShipCondition))),
                defaultColor: ShipConditionColor(lobby.ShipCondition));
            content.AddChild(ship);
        }
        return content;
    }

    /// <summary>Explains why joining a crew that has lost its people or ship needs a second press.</summary>
    private static Control JoinWarning()
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(FormattedMessage.FromUnformatted(Text("join-warning")), defaultColor: Color.OrangeRed);
        return label;
    }

    /// <summary>A join action that asks for confirmation when the crew is dead or its ship is lost.</summary>
    private static Button JoinButton(WGRoundSetupLobby lobby, string key, Action pressed, bool disabled, string? disabledReason)
    {
        if (disabled || !NeedsJoinWarning(lobby))
            return Button(key, pressed, disabled, disabledReason);
        var button = new ConfirmButton
        {
            Text = Text(key), ConfirmationText = Text("join-warning-confirm"), MinHeight = 40, HorizontalExpand = true,
            ToolTip = Text("join-warning"), ResetTime = TimeSpan.FromSeconds(5),
        };
        button.Label.ClipText = false;
        button.AddStyleClass("ButtonColorRed");
        button.OnPressed += _ => pressed();
        return button;
    }
}
