using System.Linq;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Shared._WF.Outposts;
using Content.Shared._WF.RoundSetup;
using Content.Shared.Roles;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WF.RoundSetup;

/// <summary>
/// Outpost crews: the host's saved outpost takes the place of a ship. Readying consents to a share of its load price;
/// the host's launch charges the shares and queues the outpost to drop on a world, and the crew waits in the lobby
/// until it lands, then wakes in its cryopods. If it can't come down, the shares are refunded and the crew starts Normal.
/// A landed outpost's crew is its crew for the round: later members wake in its pods, and its name, admission and
/// openings are saved with it (<see cref="WFOutpostCrewComponent"/>) and come back when it next lands or its owner wakes there.
/// </summary>
public sealed partial class WFRoundSetupSystem
{
    [Dependency] private WFOutpostSystem _outposts = default!;
    [Dependency] private WFSafeSpawnSystem _safeSpawn = default!;

    /// <summary>The job a settler takes instead when an Outpost crew falls back to a Normal start.</summary>
    private const string OutpostFallbackJob = "Contractor";

    /// <summary>Jobs an Outpost crew may take: the settler and the crash scenario's.</summary>
    private static readonly string[] OutpostJobs = WFCrashScenarioSystem.AllowedJobs.Prepend(WFOutpostLimits.SettlerJob).ToArray();

    /// <summary>Whether this server runs Outpost crews: outposts on, and planet worlds for them.</summary>
    private bool OutpostAvailable => _demoConfig.GetCVar(OutpostCVars.Enabled) && _crash.IsAvailable;

    /// <summary>What a member's purchase consent is for: the ship design, or the outpost save slot or preset.</summary>
    private static string? PurchaseKey(Lobby lobby) => lobby.Scenario == WFRoundScenario.Outpost
        ? lobby.OutpostSlot is { } slot ? $"outpost:{slot}" : lobby.OutpostPreset is { } preset ? $"preset:{preset}" : null
        : lobby.Vessel;

    /// <summary>A purchase crew before anything was bought: a ship not yet launched, or an outpost not yet sent down.</summary>
    private static bool IsBuying(Lobby lobby) =>
        WFRoundSetupPolicy.IsPurchase(lobby.Scenario) && !lobby.Started && !lobby.OutpostLanding;

    /// <summary>
    /// The host picks one of their saves or a preset, which sets the price; any pick asks everyone to consent again.
    /// </summary>
    private bool SelectOutpost(Lobby lobby, ICommonSession host, int slot, string? presetId, out string error)
    {
        error = "wf-round-setup-outpost-invalid";
        if (lobby.Scenario != WFRoundScenario.Outpost || !IsBuying(lobby))
            return false;
        if (presetId != null)
            return SelectPreset(lobby, presetId);
        if (_outposts.GetSlots(host.UserId) is not { } slots)
        {
            error = "wf-round-setup-outpost-reading";
            return false;
        }
        if (slot < 0 || slot >= slots.Length || slots[slot] is not { } save)
            return false;
        if (_outposts.IsInUse(save))
        {
            error = "wf-outpost-load-in-use";
            return false;
        }

        var price = WFOutpostSystem.LoadPrice(save.Appraisal);
        lobby.OutpostName = save.Name;
        if (lobby.OutpostSlot == slot && lobby.OutpostPrice == price)
        {
            UnreadyMembers(lobby);
            return true;
        }
        lobby.OutpostSlot = slot;
        lobby.OutpostPreset = null;
        lobby.OutpostPrice = price;
        ResetContributions(lobby);
        return true;
    }

    private bool SelectPreset(Lobby lobby, string presetId)
    {
        if (!_prototypes.TryIndex<WFOutpostPresetPrototype>(presetId, out var preset))
            return false;

        lobby.OutpostName = Loc.GetString(preset.Name);
        if (lobby.OutpostPreset == preset.ID && lobby.OutpostPrice == preset.Price)
        {
            UnreadyMembers(lobby);
            return true;
        }
        lobby.OutpostSlot = null;
        lobby.OutpostPreset = preset.ID;
        lobby.OutpostPrice = preset.Price;
        ResetContributions(lobby);
        return true;
    }

    private static void ClearOutpost(Lobby lobby)
    {
        lobby.OutpostSlot = null;
        lobby.OutpostPreset = null;
        lobby.OutpostName = string.Empty;
        lobby.OutpostPrice = 0;
    }

    /// <summary>
    /// Where a member of an Outpost crew spawns: a free cryopod of the landed outpost, else safe floor on it. Before it
    /// lands, the host's launch sends it down; until then everyone waits, and <paramref name="error"/> says so.
    /// </summary>
    private bool TryOutpostSpawn(Lobby lobby, ICommonSession session, out EntityUid station, out EntityCoordinates? coordinates,
        out string error)
    {
        station = EntityUid.Invalid;
        coordinates = null;
        if (lobby.OutpostGrid is { } grid)
        {
            error = "wf-round-setup-outpost-gone";
            if (TerminatingOrDeleted(grid))
                return false;
            if (_outposts.TryGetFreePod(grid, out var pod))
                coordinates = Transform(pod).Coordinates;
            else if (_safeSpawn.TryFindSafeFloor(grid, out var floor))
                coordinates = floor;
            else
            {
                error = "wf-round-setup-outpost-no-room";
                return false;
            }
            station = _outposts.EnsureSettlerStation();
            return true;
        }

        // A started crew whose outpost was sold has none to send down again.
        if (lobby.Started)
        {
            error = "wf-round-setup-outpost-gone";
            return false;
        }

        if (!lobby.OutpostLanding && !StartOutpost(lobby, session, out error))
            return false;
        lobby.OutpostWaiting.Add(session.UserId);
        error = "wf-round-setup-outpost-landing";
        return false;
    }

    /// <summary>The host's launch: checks consent, the save and every share, picks a world, charges and queues the drop.</summary>
    private bool StartOutpost(Lobby lobby, ICommonSession host, out string error)
    {
        error = "wf-round-setup-error-ship-consent";
        if (lobby.Host != host.UserId || !AllPaymentsApproved(lobby))
            return false;
        if (lobby.OutpostPreset is { } presetId)
        {
            if (!_prototypes.TryIndex<WFOutpostPresetPrototype>(presetId, out var preset) || preset.Price != lobby.OutpostPrice)
            {
                error = "wf-round-setup-payment-changed";
                return false;
            }
            if (!_outposts.CanDropPreset(host.UserId, presetId, out error) || !CanAffordCrew(lobby, out error))
                return false;
        }
        else if (lobby.OutpostSlot is not { } slot)
        {
            error = "wf-round-setup-outpost-choice";
            return false;
        }
        else if (_outposts.GetSlots(host.UserId) is not { } slots || slots[slot] is not { } save ||
            WFOutpostSystem.LoadPrice(save.Appraisal) != lobby.OutpostPrice)
        {
            error = "wf-round-setup-payment-changed";
            return false;
        }
        else if (!_outposts.CanDrop(host.UserId, slot, out error) || !CanAffordCrew(lobby, out error))
            return false;
        if (!_crash.TryChooseGround(out var ground))
        {
            error = "wf-round-setup-outpost-no-world";
            return false;
        }
        if (!ChargeCrew(lobby, host, out error))
            return false;

        var lobbyId = lobby.Id;
        Action<EntityUid?, string> dropped = (grid, reason) => OnOutpostDropped(lobbyId, grid, reason);
        var queued = lobby.OutpostPreset is { } queuedPreset
            ? _outposts.TryQueuePresetDrop(host.UserId, CharacterName(host.UserId), queuedPreset, ground, dropped, out error)
            : _outposts.TryQueueDrop(host.UserId, CharacterName(host.UserId), lobby.OutpostSlot!.Value, ground, dropped, out error);
        if (!queued)
        {
            _ships.RefundShares(lobbyId);
            return false;
        }
        lobby.OutpostLanding = true;
        return true;
    }

    /// <summary>The drop's answer: the crew spawns at its outpost, or is refunded and starts Normal.</summary>
    private void OnOutpostDropped(int lobbyId, EntityUid? grid, string reason)
    {
        if (!_lobbies.TryGetValue(lobbyId, out var lobby) || !lobby.OutpostLanding)
        {
            // The crew broke up while it came down; a landed outpost still belongs to its owner.
            if (grid != null)
                _ships.KeepShares(lobbyId);
            else
                _ships.RefundShares(lobbyId);
            return;
        }

        lobby.OutpostLanding = false;
        if (grid is { } outpost)
        {
            _ships.KeepShares(lobbyId);
            lobby.OutpostGrid = outpost;
            RestoreOutpostCrew(lobby, outpost);
        }
        else
        {
            _ships.RefundShares(lobbyId);
            FallBackToNormal(lobby, reason);
        }

        var users = lobby.Members
            .Where(pair => !pair.Value.Spawned && (lobby.OutpostWaiting.Contains(pair.Key) || pair.Value.Ready))
            .Select(pair => pair.Key)
            .OrderByDescending(user => user == lobby.Host)
            .ToList();
        lobby.OutpostWaiting.Clear();
        foreach (var user in users)
        {
            if (_players.TryGetSessionById(user, out var session) && !TrySpawn(lobby, session, true, out var error))
                SendState(session, error);
        }
        Broadcast();
    }

    /// <summary>Turns an Outpost crew whose outpost can't come down into a Normal one, telling them why.</summary>
    private void FallBackToNormal(Lobby lobby, string reason)
    {
        Log.Info($"Outpost crew {lobby.Id} falls back to a Normal start: {reason}");
        lobby.Scenario = WFRoundScenario.Normal;
        ClearOutpost(lobby);
        foreach (var role in lobby.Roles.Values)
        {
            role.Station = null;
            if (role.Job == WFOutpostLimits.SettlerJob)
                role.Job = OutpostFallbackJob;
        }
        foreach (var member in lobby.Members.Values.Where(member => !member.Spawned))
        {
            member.Station = null;
            member.Contribution = 0;
            if (member.Job == WFOutpostLimits.SettlerJob)
                member.Job = OutpostFallbackJob;
        }
        NotifyCrew(lobby, Loc.GetString("wf-round-setup-outpost-fallback", ("reason", Loc.GetString(reason))));
    }

    /// <summary>
    /// Round start for an Outpost crew: the host's launch sends the outpost down and the crew leaves the spawn pool to
    /// wait for it. If it can't go, the crew stays in the pool as a Normal one.
    /// </summary>
    private void StartOutpostAtRoundStart(Lobby lobby, ICommonSession[] candidates, List<ICommonSession> pool)
    {
        var host = candidates.First(player => player.UserId == lobby.Host);
        if (!TrySpawn(lobby, host, false, out var error) && !lobby.OutpostLanding)
        {
            FallBackToNormal(lobby, error);
            return;
        }

        foreach (var player in candidates)
        {
            pool.Remove(player);
            lobby.OutpostWaiting.Add(player.UserId);
            SendState(player, "wf-round-setup-outpost-landing");
        }
    }

    /// <summary>The started crew an outpost belongs to, if any.</summary>
    private Lobby? OutpostCrew(EntityUid outpost) =>
        _lobbies.Values.FirstOrDefault(lobby => lobby.Started && lobby.OutpostGrid == outpost);

    /// <summary>Writes an outpost's crew onto it, so its next save keeps the crew's name, admission and openings.</summary>
    public void SyncOutpostCrew(EntityUid outpost)
    {
        if (OutpostCrew(outpost) is not { } lobby)
            return;

        var crew = EnsureComp<WFOutpostCrewComponent>(outpost);
        crew.Title = lobby.Title;
        crew.Description = lobby.Description;
        crew.Access = lobby.Access;
        crew.AllowLateJoin = lobby.AllowLateJoin;
        crew.Roles.Clear();
        foreach (var role in lobby.Roles.Values)
        {
            crew.Roles.Add(new WFOutpostCrewRole { Name = role.Name, Job = role.Job, Capacity = role.Capacity });
        }
    }

    /// <summary>Unlinks an outpost from its crew, as when it is sold; later members of that crew can't spawn on it.</summary>
    public void ReleaseOutpost(EntityUid outpost)
    {
        foreach (var lobby in _lobbies.Values.Where(lobby => lobby.OutpostGrid == outpost))
        {
            lobby.OutpostGrid = null;
        }

        Broadcast();
    }

    /// <summary>
    /// Takes back the crew an outpost was saved with: its openings, and its name, description and admission where the
    /// host left the crew's own unset. Anything no longer valid is skipped.
    /// </summary>
    private void RestoreOutpostCrew(Lobby lobby, EntityUid outpost)
    {
        if (!TryComp<WFOutpostCrewComponent>(outpost, out var crew))
            return;

        if (lobby.Title.Length == 0 && WFRoundSetupPolicy.TryNormalizeTitle(crew.Title, out var title))
            lobby.Title = title;
        if (lobby.Description.Length == 0 && WFRoundSetupPolicy.TryNormalizeDescription(crew.Description, out var description))
            lobby.Description = description;
        if (lobby.Access == WFLobbyAccess.Requests && lobby.AllowLateJoin && Enum.IsDefined(crew.Access) &&
            (!WFRoundSetupPolicy.IsCompanyAccess(crew.Access) || lobby.CompanyId != null))
        {
            lobby.Access = crew.Access;
            lobby.AllowLateJoin = crew.AllowLateJoin;
        }

        var jobs = ScenarioJobs(WFRoundScenario.Outpost, WFCrewFaction.None);
        foreach (var role in crew.Roles)
        {
            if (lobby.Roles.Count >= WFRoundSetupPolicy.MaximumRoles)
                break;
            if (!jobs.Contains(role.Job) || !_prototypes.HasIndex<JobPrototype>(role.Job) ||
                role.Capacity is < 1 or > WFRoundSetupPolicy.MaximumMembers ||
                !WFRoundSetupPolicy.TryNormalizeRoleName(role.Name, out var name) ||
                lobby.Roles.Values.Any(existing => existing.Name == name && existing.Job == role.Job))
                continue;

            var id = ++lobby.NextRoleId;
            lobby.Roles.Add(id, new LobbyRole { Id = id, Name = name, Job = role.Job, Capacity = role.Capacity });
        }
    }

    /// <summary>
    /// Puts an outpost's owner who just woke in one of its pods into its crew: the crew it already has, or a new started
    /// one with the crew it was saved with, so others can join in Crew Setup and wake in its pods too.
    /// </summary>
    public void JoinOutpostCrew(ICommonSession session, EntityUid outpost, string name)
    {
        if (FindLobby(session.UserId) != null)
            return;

        var lobby = OutpostCrew(outpost);
        if (lobby == null)
        {
            if (_lobbies.Count >= 64)
                return;

            lobby = new Lobby
            {
                Id = ++_nextId, Host = session.UserId, Scenario = WFRoundScenario.Outpost, Code = NewCode(), Started = true,
                OutpostGrid = outpost, OutpostName = name,
            };
            _lobbies.Add(lobby.Id, lobby);
            RestoreOutpostCrew(lobby, outpost);
        }

        var member = new Member { Name = session.Name, Job = WFOutpostLimits.SettlerJob };
        RegisterMember(lobby, session, member);
        member.Spawned = true;
        CaptureCrewCard(member, session);
        _ships.LinkCrewAccess(new[] { outpost }, session);
        SyncCrewShips();
        Broadcast();
    }
}
