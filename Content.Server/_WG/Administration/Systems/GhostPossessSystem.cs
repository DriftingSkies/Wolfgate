using System.Diagnostics.CodeAnalysis;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.Ghost;
using Content.Server.Mind;
using Content.Server.Mind.Commands;
using Content.Shared._WG.Administration.GhostShortcuts;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.DragDrop;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Player;

namespace Content.Server._WG.Administration.Systems;

/// <summary>
/// Puts a ghost's player into a body when an admin drops the ghost on it. A body that already has a player
/// needs the admin to confirm the replacement first.
/// </summary>
public sealed partial class GhostPossessSystem : SharedGhostPossessSystem
{
    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private GhostSystem _ghost = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GhostComponent, DragDropDraggedEvent>(OnGhostDragged);
        SubscribeNetworkEvent<GhostPossessRequestEvent>(OnPossessRequest);
    }

    private void OnGhostDragged(Entity<GhostComponent> ghost, ref DragDropDraggedEvent args)
    {
        if (args.Handled
            || !TryComp<ActorComponent>(args.User, out var actor)
            || !_adminManager.HasAdminFlag(actor.PlayerSession, AdminFlags.Fun))
            return;

        args.Handled = true;
        TryPossess(actor.PlayerSession, ghost, args.Target, replace: false);
    }

    private void OnPossessRequest(GhostPossessRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!_adminManager.HasAdminFlag(args.SenderSession, AdminFlags.Fun)
            || !TryGetEntity(ev.Ghost, out var ghost)
            || !TryGetEntity(ev.Body, out var body))
            return;

        TryPossess(args.SenderSession, ghost.Value, body.Value, ev.Replace, ev.Occupant);
    }

    /// <summary>
    /// Moves the ghost's player into the body. Without <paramref name="replace"/>, an occupied body only sends the admin a prompt.
    /// A replace only evicts the occupant the admin was shown (<paramref name="expectedOccupant"/>); anyone else gets a fresh prompt.
    /// </summary>
    public bool TryPossess(ICommonSession admin, EntityUid ghost, EntityUid body, bool replace, NetEntity expectedOccupant = default)
    {
        if (!HasComp<GhostComponent>(ghost) || !IsBodyTarget(body))
            return false;

        if (!TryComp<ActorComponent>(ghost, out var ghostActor))
        {
            _popup.PopupEntity(Loc.GetString("wg-ghost-possess-no-player"), ghost, admin, PopupType.MediumCaution);
            return false;
        }

        var player = ghostActor.PlayerSession;
        var (mindId, mind) = _mind.GetOrCreateMind(player.UserId);

        if (mind.CurrentEntity == body)
            return true;

        // Dropping a ghost onto its own body just sends it back.
        if (mind.OwnedEntity == body)
        {
            _mind.UnVisit(mindId, mind);
            Finish(admin, player, ghost, body, null);
            return true;
        }

        string? evicted = null;
        if (TryGetOccupant(body, out var occupantId, out var occupant, out var visiting))
        {
            if (!replace || GetNetEntity(occupantId) != expectedOccupant)
            {
                RaiseNetworkEvent(new GhostPossessOccupiedEvent(GetNetEntity(ghost), GetNetEntity(body), GetNetEntity(occupantId))
                {
                    BodyName = Name(body),
                    GhostPlayer = player.Name,
                    OccupantCharacter = occupant.CharacterName ?? Name(body),
                    OccupantPlayer = _players.TryGetSessionById(occupant.UserId, out var occupantSession)
                        ? occupantSession.Name
                        : Loc.GetString("wg-ghost-possess-disconnected"),
                }, admin);
                return false;
            }

            evicted = occupant.CharacterName ?? Name(body);

            // A visitor goes back to their own body; the body's own mind gets a non-returnable ghost.
            if (visiting)
                _mind.UnVisit(occupantId, occupant);
            else
                _ghost.OnGhostAttempt(occupantId, false, viaCommand: true, forced: true, mind: occupant);
        }

        if (HasComp<ActorComponent>(body))
        {
            _popup.PopupEntity(Loc.GetString("wg-ghost-possess-still-occupied"), body, admin, PopupType.MediumCaution);
            return false;
        }

        MakeSentientCommand.MakeSentient(body, EntityManager);
        _mind.TransferTo(mindId, body, ghostCheckOverride: true, mind: mind);
        Finish(admin, player, ghost, body, evicted);
        return true;
    }

    /// <summary>
    /// The mind currently in the body: a visiting one first, else the body's own.
    /// </summary>
    private bool TryGetOccupant(EntityUid body, out EntityUid mindId, [NotNullWhen(true)] out MindComponent? mind, out bool visiting)
    {
        if (TryComp<VisitingMindComponent>(body, out var visitor) && visitor.MindId is { } visitingId)
        {
            visiting = true;
            mindId = visitingId;
            return TryComp(mindId, out mind);
        }

        visiting = false;
        return _mind.TryGetMind(body, out mindId, out mind);
    }

    private void Finish(ICommonSession admin, ICommonSession player, EntityUid ghost, EntityUid body, string? evicted)
    {
        _adminLogger.Add(LogType.Mind, LogImpact.High,
            $"{admin.Name} moved {player.Name} from {ToPrettyString(ghost):ghost} into {ToPrettyString(body):body}{(evicted != null ? $", ghosting {evicted}" : "")}");
        _popup.PopupEntity(Loc.GetString("wg-ghost-possess-done", ("player", player.Name), ("body", Name(body))), body, admin);

        if (player != admin)
            _chat.DispatchServerMessage(player, Loc.GetString("wg-ghost-possess-notice", ("body", Name(body))));
    }
}
