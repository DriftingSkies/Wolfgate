using Content.Server.Shuttles.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.ShuttleRecords;
using Content.Shared.Access.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Player;

namespace Content.Server._NF.Shipyard.Systems;

/// <summary>
/// Moves a ship's deed between owners for the admin ship panel. Lives in the shipyard partial because the deed is
/// access-restricted to it. Unlike <see cref="TryAssignDeed"/> these touch one record each, so a ship that already
/// has an owner keeps its station, shuttle record, lock and access mode.
/// </summary>
public sealed partial class ShipyardSystem
{
    /// <summary>The ID cards and vouchers that hold a ship's deed, copies included.</summary>
    public List<EntityUid> DeedHolders(EntityUid shuttleUid)
    {
        var holders = new List<EntityUid>();
        var deeds = AllEntityQuery<ShuttleDeedComponent>();
        while (deeds.MoveNext(out var uid, out var deed))
        {
            if (uid != shuttleUid && deed.ShuttleUid == shuttleUid)
                holders.Add(uid);
        }

        return holders;
    }

    /// <summary>Takes a ship's deed off every card and voucher that holds it, and returns them.</summary>
    public List<EntityUid> RevokeDeeds(EntityUid shuttleUid)
    {
        var holders = DeedHolders(shuttleUid);
        foreach (var holder in holders)
            RemComp<ShuttleDeedComponent>(holder);

        ForgetVoucher(shuttleUid, null);
        return holders;
    }

    /// <summary>Takes a ship's deed off one card or voucher; false when it does not hold it.</summary>
    public bool RevokeDeed(EntityUid holder, EntityUid shuttleUid)
    {
        if (holder == shuttleUid || !TryComp<ShuttleDeedComponent>(holder, out var deed) || deed.ShuttleUid != shuttleUid)
            return false;

        RemComp<ShuttleDeedComponent>(holder);
        ForgetVoucher(shuttleUid, holder);
        return true;
    }

    /// <summary>
    /// Stops the voucher a ship was bought with from working its helm: the helm lock matches a voucher against the
    /// ship's own deed, not the voucher's. With a holder, only when that holder is the voucher. The ship stays
    /// marked as bought with a voucher.
    /// </summary>
    private void ForgetVoucher(EntityUid shuttleUid, EntityUid? holder)
    {
        if (!TryComp<ShuttleDeedComponent>(shuttleUid, out var deed) || deed.PurchaseVoucherUid == null)
            return;

        if (holder != null && deed.PurchaseVoucherUid != holder.Value.ToString())
            return;

        deed.PurchaseVoucherUid = null;
        Dirty(shuttleUid, deed);
    }

    /// <summary>
    /// Puts a ship's deed on an ID card that holds none, under the ship's current name. A ship that never had a deed
    /// gets its own copy and its consoles locked to it, as a purchase would.
    /// </summary>
    public bool GrantDeed(EntityUid shuttleUid, EntityUid idCard, string ownerName)
    {
        if (!HasComp<ShuttleComponent>(shuttleUid) || !HasComp<IdCardComponent>(idCard) || HasComp<ShuttleDeedComponent>(idCard))
            return false;

        var shipDeed = EnsureShipDeed(shuttleUid, ownerName);
        var deed = AddComp<ShuttleDeedComponent>(idCard);
        deed.ShuttleUid = shuttleUid;
        deed.ShuttleName = shipDeed.ShuttleName;
        deed.ShuttleNameSuffix = shipDeed.ShuttleNameSuffix;
        deed.ShuttleOwner = ownerName;
        deed.DeedHolder = idCard;

        // A shipyard reads this off the card at a sale: a voucher ship stays one, whoever holds it.
        deed.PurchasedWithVoucher = shipDeed.PurchasedWithVoucher;
        Dirty(idCard, deed);
        return true;
    }

    /// <summary>
    /// Writes the owner's name on the ship's own deed, on every card and voucher still holding it, and on its
    /// shuttle record, adding the record if the ship has none.
    /// </summary>
    public void SetShipOwnerName(EntityUid shuttleUid, string ownerName)
    {
        var shipDeed = EnsureShipDeed(shuttleUid, ownerName);
        shipDeed.ShuttleOwner = ownerName;
        Dirty(shuttleUid, shipDeed);

        foreach (var holder in DeedHolders(shuttleUid))
        {
            var deed = Comp<ShuttleDeedComponent>(holder);
            deed.ShuttleOwner = ownerName;
            Dirty(holder, deed);
        }

        var netShuttle = GetNetEntity(shuttleUid);
        if (_shuttleRecordsSystem.TryGetRecord(netShuttle, out var record))
        {
            record.OwnerName = ownerName;
            _shuttleRecordsSystem.TryUpdateRecord(record);
            return;
        }

        _shuttleRecordsSystem.AddRecord(new ShuttleRecord(
            name: shipDeed.ShuttleName ?? string.Empty,
            suffix: shipDeed.ShuttleNameSuffix ?? string.Empty,
            ownerName: ownerName,
            entityUid: netShuttle,
            purchasedWithVoucher: shipDeed.PurchasedWithVoucher,
            purchasePrice: 0));
    }

    /// <summary>Copies the owner's crew record onto the ship's station, when it has one.</summary>
    public void CopyOwnerRecord(EntityUid shuttleUid, EntityUid idCard, EntityUid? ownerEntity, ICommonSession owner)
    {
        if (_station.GetOwningStation(shuttleUid) is { } station)
            MoveOwnerRecord(station, idCard, ownerEntity, owner);
    }

    /// <summary>The ship's own deed; a ship without one gets it under its current name, and its consoles locked to it.</summary>
    private ShuttleDeedComponent EnsureShipDeed(EntityUid shuttleUid, string ownerName)
    {
        if (TryComp<ShuttleDeedComponent>(shuttleUid, out var existing))
            return existing;

        var deed = AddComp<ShuttleDeedComponent>(shuttleUid);
        AssignShuttleDeedProperties(deed, shuttleUid, Name(shuttleUid), ownerName, false);
        Dirty(shuttleUid, deed);

        var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var consoleUid, out _, out var xform))
        {
            if (xform.GridUid != shuttleUid)
                continue;

            var lockComp = EnsureComp<ShuttleConsoleLockComponent>(consoleUid);
            _shuttleConsoleLock.SetShuttleId(consoleUid, shuttleUid.ToString(), lockComp);
        }

        return deed;
    }
}
