using Content.Server._NF.Station.Components;
using Content.Server._WG.Encounters.Components;
using Content.Server.Stack;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.Drydock;
using Content.Shared.Stacks;
using Content.Shared.Station.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Drydock;

/// <summary>
/// Chop-shop consoles: a break pays a share of the ship's value in thalers, printed by the console tile by tile, and
/// breaking lawful property posts a warrant on the breaker.
/// </summary>
public sealed partial class WGDrydockSystem
{
    [Dependency] private WGShipbreakerWarrantSystem _warrants = default!;
    [Dependency] private StackSystem _stacks = default!;

    /// <summary>Stack the chop shop prints its pay in.</summary>
    private static readonly ProtoId<StackPrototype> Thalers = "Credit";

    /// <summary>
    /// What a ship is worth to a chop shop: its shipyard price, from its own record or its vessel station's, else what
    /// the grid's fittings appraise at. Loose cargo is left out, since it drifts off when the hull goes. A ship bought at
    /// a bargain is worth that less its discount, as at a shipyard, so breaking it can't pay more than it cost.
    /// </summary>
    public int ChopValue(EntityUid ship)
    {
        ProtoId<VesselPrototype>? id = null;
        if (TryComp<VesselComponent>(ship, out var vessel))
            id = vessel.VesselId;
        else if (TryComp<StationMemberComponent>(ship, out var member) &&
                 TryComp<ExtraShuttleInformationComponent>(member.Station, out var info))
            id = info.Vessel;

        if (id != null && _prototype.TryIndex(id.Value, out var proto) && proto.Price > 0)
            return _shipServices.ResaleValue(ship, proto.Price);

        var appraisal = (int) Math.Min(_pricing.AppraiseGrid(ship, uid => Transform(uid).ParentUid != ship || Transform(uid).Anchored), int.MaxValue);
        return _shipServices.ResaleValue(ship, appraisal);
    }

    /// <summary>What a chop shop still pays for breaking the rest of a ship, less what earlier breaks of it printed.</summary>
    public int ChopPayout(WGDrydockChopShopComponent chop, EntityUid ship)
    {
        if (TryComp<WGHulkComponent>(ship, out var hulk) && hulk.ChopTotal is { } total)
            return Math.Max(0, total - hulk.ChopPaid);

        return (int) (ChopValue(ship) * (double) chop.ValueShare);
    }

    /// <summary>Fixes a chop-shop break's payout when it starts, to be printed as its tiles come off.</summary>
    private void SetUpChopPay(WGDrydockChopShopComponent chop, WGDrydockJobComponent job, List<WGDrydockWorkItem> items)
    {
        var hulk = EnsureComp<WGHulkComponent>(job.Ship);
        hulk.ChopTotal ??= (int) (ChopValue(job.Ship) * (double) chop.ValueShare);
        job.CashTotal = ChopPayout(chop, job.Ship);
        job.CashWeight = 0;
        foreach (var item in items)
        {
            job.CashWeight += item.WorkTime;
        }
    }

    /// <summary>
    /// Prints a broken tile's share of the payout by the console, weighted by what was stripped off it; the last tile
    /// of the hull prints whatever is left.
    /// </summary>
    private void PrintChopCash(EntityUid console, WGDrydockJobComponent job, WGDrydockWorkItem item)
    {
        job.CashWeightDone += item.WorkTime;
        var due = job.HullGone || job.CashWeight <= 0
            ? job.CashTotal
            : (int) (job.CashTotal * Math.Min(1.0, job.CashWeightDone / job.CashWeight));
        var amount = due - job.CashPaid;
        if (amount <= 0)
            return;

        job.CashPaid += amount;
        if (TryComp<WGHulkComponent>(job.Ship, out var hulk))
            hulk.ChopPaid += amount;

        var drop = DropSpot(console);

        // New bills go on the stack already at the console, until someone takes it.
        if (job.CashStack is { } stack && !TerminatingOrDeleted(stack) && !_container.IsEntityInContainer(stack) &&
            TryComp<StackComponent>(stack, out var stackComp) &&
            _transform.InRange(Transform(stack).Coordinates, drop, 1f))
        {
            _stacks.SetCount(stack, stackComp.Count + amount, stackComp);
            return;
        }

        job.CashStack = _stacks.Spawn(amount, Thalers, drop);
    }

    /// <summary>Hull tiles a grid has.</summary>
    private int TileCount(EntityUid ship)
    {
        if (!TryComp<MapGridComponent>(ship, out var grid))
            return 0;

        var count = 0;
        var tiles = _map.GetAllTiles(ship, grid);
        while (tiles.MoveNext(out _))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Whether breaking a ship is a crime: it carries a deed in someone else's name, or it belongs to a live encounter
    /// of a lawful faction. Derelicts, outlaws' ships and ships freed from their encounter are anyone's.
    /// </summary>
    public bool IsLawfulProperty(WGDrydockChopShopComponent chop, EntityUid ship, EntityUid? breaker)
    {
        if (TryComp<ShuttleDeedComponent>(ship, out var deed))
        {
            if (breaker == null)
                return true;

            // Registered ships go by the owner's player, since character names aren't unique.
            if (TryComp<ShipOwnershipComponent>(ship, out var ownership))
                return CompOrNull<ActorComponent>(breaker.Value)?.PlayerSession.UserId != ownership.OwnerUserId;

            return deed.ShuttleOwner != Name(breaker.Value);
        }

        if (!TryComp<WGEncounterGridComponent>(ship, out var member) ||
            !TryComp<WGEncounterComponent>(member.Encounter, out var encounter) ||
            !_prototype.TryIndex(encounter.Prototype, out var prototype))
            return false;

        if (encounter.Ships.TryGetValue(member.Key, out var state) && state.Derelict)
            return false;

        var entry = prototype.Ships.Find(s => s.Key == member.Key);
        return entry != null && !chop.OutlawFactions.Contains(entry.Faction);
    }

    /// <summary>Posts a warrant on whoever started breaking lawful property at a chop shop.</summary>
    private void PostBreakWarrant(EntityUid console, WGDrydockChopShopComponent chop, EntityUid ship, EntityUid breaker)
    {
        if (chop.WarrantReward <= 0)
            return;

        var shipName = Name(ship);
        _warrants.Issue(breaker, shipName, chop.WarrantReward,
            Loc.GetString("drydock-chop-warrant-description", ("name", Name(breaker)), ("ship", shipName)),
            Loc.GetString("drydock-chop-warrant-author"));

        Log.Info($"{ToPrettyString(breaker)} began breaking lawful ship {ToPrettyString(ship)} at {ToPrettyString(console)}.");
        _popup.PopupEntity(Loc.GetString("drydock-chop-warrant-posted"), breaker, breaker, Shared.Popups.PopupType.LargeCaution);
    }
}
