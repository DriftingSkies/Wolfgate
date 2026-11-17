using System.Numerics;
using Content.Client.Lobby;
using Content.Shared._WG.RoundSetup;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.RoundSetup;

public sealed partial class WGRoundSetupWindow
{
    /// <summary>Gives a crew its leader's visible identity and existing profession icon.</summary>
    private BoxContainer CrewIdentity(WGRoundSetupLobby lobby)
    {
        var identity = Column(4);
        identity.AddChild(PlainDescription(LobbyTitle(lobby)));
        if (lobby.Scenario == WGRoundScenario.FactionShip)
            identity.AddChild(Description("faction-" + lobby.Faction.ToString().ToLowerInvariant()));
        identity.AddChild(PlainDescription(Text("crew-leader", ("name", lobby.Portrait?.Name ?? lobby.HostName))));
        identity.AddChild(Row(WGRoundSetupCrewRow.JobIcon(lobby.HostJob, lobby.HostRankId),
            PlainDescription(lobby.HostTitle.Length > 0 ? lobby.HostTitle : JobName(lobby.HostJob))));
        return Row(new LeaderPortrait(lobby.Portrait, lobby.HostJob), identity);
    }

    /// <summary>Owns a preview dummy only while its portrait is attached to the visible UI.</summary>
    private sealed class LeaderPortrait : SpriteView
    {
        private readonly WGCrewPortrait? _portrait;
        private readonly string? _job;
        private EntityUid? _dummy;

        public LeaderPortrait(WGCrewPortrait? portrait, string? job)
        {
            _portrait = portrait;
            _job = job;
            SetSize = new Vector2(80);
            Scale = new Vector2(2.5f);
            Stretch = StretchMode.Fill;
            OverrideDirection = Direction.South;
            MouseFilter = MouseFilterMode.Ignore;
        }

        protected override void EnteredTree()
        {
            base.EnteredTree();
            if (_portrait == null)
                return;
            var profile = HumanoidCharacterProfile.DefaultWithSpecies(_portrait.Species)
                .WithName(_portrait.Name).WithSex(_portrait.Sex).WithCharacterAppearance(_portrait.Appearance);
            var prototypes = IoCManager.Resolve<Robust.Shared.Prototypes.IPrototypeManager>();
            prototypes.TryIndex<JobPrototype>(_job ?? "Contractor", out var job);
            _dummy = UserInterfaceManager.GetUIController<LobbyUIController>().LoadProfileEntity(profile, job, job != null);
            SetEntity(_dummy);
        }

        protected override void ExitedTree()
        {
            SetEntity((EntityUid?) null);
            if (_dummy != null)
                IoCManager.Resolve<IEntityManager>().DeleteEntity(_dummy);
            _dummy = null;
            base.ExitedTree();
        }
    }
}
