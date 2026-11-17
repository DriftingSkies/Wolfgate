using System.Linq;
using Content.Server._WG.ShipAccess;
using Content.Server.StationRecords.Systems;
using Content.Shared._WG.ShipAccess;
using Content.Shared.StationRecords;
using Robust.Shared.Player;

namespace Content.Server._WG.RoundSetup;

public sealed partial class WGRoundSetupSystem
{
    [Dependency] private WGShipAccessServerSystem _shipAccess = default!;
    [Dependency] private WGShipAccessSystem _shipCredentials = default!;

    private void InitializeShipAccess()
    {
        SubscribeLocalEvent<WGShipAccessComponent, WGShipCrewLinkEvent>(OnLinkCrew);
    }

    /// <summary>Only a spawned member can link a ship to their crew.</summary>
    private void OnLinkCrew(Entity<WGShipAccessComponent> ship, ref WGShipCrewLinkEvent args)
    {
        if (!TryComp<ActorComponent>(args.Actor, out var actor) || FindLobby(actor.PlayerSession.UserId) is not { } lobby ||
            !lobby.Members[actor.PlayerSession.UserId].Spawned)
            return;
        SyncCrewShip(ship, lobby);
    }

    /// <summary>Retains the issued card through disconnects, without trusting cards picked up later.</summary>
    private void CaptureCrewCard(Member member, ICommonSession session)
    {
        member.Body = session.AttachedEntity;
        if (session.AttachedEntity is { } body && _shipCredentials.TryGetWornCard(body, out var card) &&
            _shipCredentials.TryGetKey(card, out var key))
            member.AccessKey = key;
    }

    /// <summary>Immediately revokes departed members and periodically updates crew names.</summary>
    private void SyncCrewShips()
    {
        var ships = EntityQueryEnumerator<WGShipAccessComponent>();
        while (ships.MoveNext(out var uid, out var access))
        {
            if (access.CrewId is not { } id || TerminatingOrDeleted(uid))
                continue;
            _lobbies.TryGetValue(id, out var lobby);
            SyncCrewShip((uid, access), lobby);
        }
    }

    /// <summary>
    /// Picks up the card of a member who joined without one worn, while they are still in the body they joined with
    /// and the card's crew record carries their name, so someone else's ID put on later isn't taken.
    /// </summary>
    private void RetryCrewCards(Lobby lobby)
    {
        foreach (var (user, member) in lobby.Members)
        {
            if (!member.Spawned || member.AccessKey != null || member.Body is not { } body ||
                !_players.TryGetSessionById(user, out var session) || session.AttachedEntity != body ||
                !_shipCredentials.TryGetWornCard(body, out var card) || !_shipCredentials.TryGetKey(card, out var key) ||
                !EntityManager.System<StationRecordsSystem>().TryGetRecord<GeneralStationRecord>(_shipCredentials.ToRecordKey(key), out var record) ||
                // A cyborg's record is under its chassis name, not its player's character.
                record.Name != member.Name && record.Name != Name(body))
                continue;
            member.AccessKey = key;
        }
    }

    private void SyncCrewShip(Entity<WGShipAccessComponent> ship, Lobby? lobby)
    {
        if (lobby != null)
            RetryCrewCards(lobby);
        var keys = lobby?.Members.Values.Where(member => member.Spawned && member.AccessKey != null)
            .Select(member => member.AccessKey!.Value) ?? Enumerable.Empty<WGShipAccessKey>();
        var name = lobby == null ? null : string.IsNullOrWhiteSpace(lobby.Title) ? lobby.Members[lobby.Host].Name : lobby.Title;
        _shipAccess.SetCrew(ship, lobby?.Id, name, keys);
    }
}
