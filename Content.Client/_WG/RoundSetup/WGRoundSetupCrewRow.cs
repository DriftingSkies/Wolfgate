using System.Linq;
using System.Numerics;
using Content.Shared._WG.RoundSetup;
using Content.Shared.Roles;
using Content.Shared.StatusIcon;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WG.RoundSetup;

/// <summary>A single-line crew entry shared by the lobby and crew browser.</summary>
public sealed class WGRoundSetupCrewRow : Button
{
    /// <summary>Opens details without selecting a role or joining the crew.</summary>
    public WGRoundSetupCrewRow(WGRoundSetupLobby lobby, bool invited, Action open)
    {
        Name = "RoundSetupCrewRow" + lobby.Id;
        HorizontalExpand = true;
        MinHeight = 34;
        var title = string.IsNullOrEmpty(lobby.Title)
            ? Loc.GetString("wg-round-setup-row-untitled", ("host", lobby.Portrait?.Name ?? lobby.HostName)) : lobby.Title;
        var status = Loc.GetString("wg-round-setup-" + (invited ? "row-invited" : lobby.Started ? "status-started" : "status-waiting"));
        ToolTip = Loc.GetString("wg-round-setup-row-tooltip", ("title", title), ("host", lobby.Portrait?.Name ?? lobby.HostName),
            ("count", lobby.MemberCount), ("open", Openings(lobby)), ("status", status));
        var statuses = lobby.Roster.Select(mate => mate.Status).ToArray();
        var living = WGCrewStatusPolicy.Living(statuses);
        var deployed = WGCrewStatusPolicy.Deployed(statuses);
        var warning = WGCrewStatusPolicy.NeedsJoinWarning(lobby.Started, statuses, lobby.ShipCondition);
        if (lobby.Recruiting)
            ToolTip += "\n" + Loc.GetString("wg-round-setup-row-recruiting-tooltip");
        if (lobby.Started)
            ToolTip += "\n" + Loc.GetString("wg-round-setup-row-alive-tooltip", ("living", living), ("deployed", deployed),
                ("ship", Loc.GetString("wg-round-setup-ship-condition-short-" + lobby.ShipCondition.ToString().ToLowerInvariant())));
        Label.Visible = false;
        var row = new BoxContainer { SeparationOverride = 8, HorizontalExpand = true, MouseFilter = MouseFilterMode.Ignore };
        row.AddChild(JobIcon(lobby.HostJob ?? (WGRoundSetupPolicy.IsShip(lobby.Scenario) ? "Pilot" : "Contractor"), lobby.HostRankId));
        if (lobby.CompanyTag.Length > 0)
            row.AddChild(new Label { Text = $"[{lobby.CompanyTag}]", FontColorOverride = lobby.CompanyColour, MouseFilter = MouseFilterMode.Ignore,
                ToolTip = lobby.CompanyName });
        row.AddChild(new Label { Text = title, ClipText = true, HorizontalExpand = true, MouseFilter = MouseFilterMode.Ignore });
        if (lobby.Recruiting)
        {
            row.AddChild(new Label
            {
                Text = Loc.GetString("wg-round-setup-row-recruiting"),
                FontColorOverride = WGRecruitingGlow.Accent,
                MouseFilter = MouseFilterMode.Ignore,
            });
        }
        var difficulty = WGRoundSetupPolicy.Difficulty(lobby.Scenario);
        row.AddChild(new Label
        {
            Text = WGScenarioDifficultyLabel.Name(difficulty),
            FontColorOverride = WGScenarioDifficultyLabel.Color(difficulty),
            MouseFilter = MouseFilterMode.Ignore,
        });
        row.AddChild(new Label
        {
            Text = lobby.Started && deployed > 0
                ? Loc.GetString("wg-round-setup-row-alive", ("living", living), ("deployed", deployed))
                : Loc.GetString("wg-round-setup-row-count", ("count", lobby.MemberCount)),
            FontColorOverride = invited ? Color.Gold : warning ? Color.OrangeRed : lobby.Started ? Color.LightGreen : Color.LightGray,
            MouseFilter = MouseFilterMode.Ignore,
        });
        AddChild(row);
        if (lobby.Recruiting)
            AddChild(new WGRecruitingGlow());
        OnPressed += _ => open();
    }

    /// <summary>A job icon that becomes its company rank's icon as soon as the client has it.</summary>
    private sealed class RankedJobIcon(int rankId) : TextureRect
    {
        private bool _waiting = true;

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);
            if (!_waiting || Companies.WGCompanyRankIcons.Get(rankId) is not { } rank)
                return;
            Texture = rank;
            _waiting = false;
        }
    }

    /// <summary>Uses the job's existing status sprite for roles and crew identities, or the company rank's icon if it has one.</summary>
    public static TextureRect JobIcon(string? job, int rankId = 0)
    {
        var icon = rankId != 0 ? new RankedJobIcon(rankId) : new TextureRect();
        icon.SetSize = new Vector2(24);
        icon.Stretch = TextureRect.StretchMode.Scale;
        icon.VerticalAlignment = VAlignment.Center;
        icon.MouseFilter = MouseFilterMode.Ignore;
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        if (prototypes.TryIndex<JobPrototype>(job ?? "Contractor", out var prototype) && prototypes.TryIndex<JobIconPrototype>(prototype.Icon, out var status))
            icon.Texture = IoCManager.Resolve<IEntityManager>().System<SpriteSystem>().Frame0(status.Icon);
        return icon;
    }

    private static int Openings(WGRoundSetupLobby lobby) => lobby.Roles.Sum(role => Math.Max(0, role.Capacity - role.Occupied));

    /// <summary>Avoids rebuilding rows for unrelated readiness and admission changes.</summary>
    public static bool SameListing(WGRoundSetupLobby first, WGRoundSetupLobby second) =>
        first.Id == second.Id && first.Scenario == second.Scenario && first.Title == second.Title && first.HostName == second.HostName && first.Portrait?.Name == second.Portrait?.Name && first.HostJob == second.HostJob && first.HostRankId == second.HostRankId &&
        first.MemberCount == second.MemberCount && first.Started == second.Started && Openings(first) == Openings(second) && first.Recruiting == second.Recruiting && first.CompanyTag == second.CompanyTag && first.CompanyColour == second.CompanyColour &&
        first.ShipCondition == second.ShipCondition && first.Roster.Select(mate => mate.Status).SequenceEqual(second.Roster.Select(mate => mate.Status));
}
