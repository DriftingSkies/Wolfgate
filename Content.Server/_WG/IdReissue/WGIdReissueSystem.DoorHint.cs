using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Access.Components;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Server._WG.IdReissue;

/// <summary>Tells a ship's owner who is turned away at their own door without their ID where to get a new one.</summary>
public sealed partial class WGIdReissueSystem
{
    private static readonly TimeSpan HintCooldown = TimeSpan.FromSeconds(30);

    /// <summary>When each player may next be hinted at, so bumping a door doesn't repeat it.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _hinted = new();

    private void InitializeDoorHint()
    {
        SubscribeLocalEvent<AccessReaderComponent, BeforeDoorOpenedEvent>(OnBeforeDoorOpened);
    }

    private void OnBeforeDoorOpened(Entity<AccessReaderComponent> ent, ref BeforeDoorOpenedEvent args)
    {
        // Not gated on the event being cancelled: a bolted, unpowered or otherwise blocked door shuts the owner out too.
        if (args.User is not { } user || !HasComp<DoorComponent>(ent))
            return;

        if (_hinted.TryGetValue(user, out var next) && _timing.CurTime < next)
            return;

        if (!NeedsHint(ent, user))
            return;

        _hinted[user] = _timing.CurTime + HintCooldown;
        _popup.PopupEntity(Loc.GetString("wg-id-reissue-door-hint"), ent, user, PopupType.Medium);
    }

    /// <summary>
    /// Whether the ship this door is on is registered to the user under this character's name, the door would turn
    /// them away and they carry no valid card of their own. Runs on every door opening, so the cheap checks go first.
    /// </summary>
    public bool NeedsHint(Entity<AccessReaderComponent> door, EntityUid user)
    {
        if (!TryComp<ActorComponent>(user, out var actor)
            || Transform(door).GridUid is not { } grid
            || !TryComp<ShipOwnershipComponent>(grid, out var ownership)
            || ownership.OwnerUserId != actor.PlayerSession.UserId
            || !TryComp<ShuttleDeedComponent>(grid, out var deed)
            || deed.ShuttleOwner?.Trim() != Name(user).Trim())
        {
            return false;
        }

        var items = _accessReader.FindPotentialAccessItems(user);
        var tags = _accessReader.FindAccessTags(user, items);
        _accessReader.FindStationRecordKeys(user, out var keys, items);
        if (_accessReader.IsAllowed(tags, keys, door, door.Comp))
            return false;

        return !CarriesOwnCard(user, CardsOf(actor.PlayerSession.UserId, Name(user)));
    }
}
