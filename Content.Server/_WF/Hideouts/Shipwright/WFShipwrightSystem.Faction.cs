using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Content.Server._WF.Drydock;
using Content.Server._WF.Outposts.Components;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Drydock;
using Content.Shared._WF.Hideouts.Shipwright;
using Content.Shared.Database;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Hideouts.Shipwright;

/// <summary>
/// The faction hangar's refit console: the same cranes, for a stock shipyard ship. Guns go on the hardpoints it came with,
/// as long as the ship's firepower stays within what its stock hardpoints hold at their size, and it takes strongboxes.
/// It lays no hardpoints and fits no other systems.
/// </summary>
public sealed partial class WFShipwrightSystem
{
    /// <summary>The systems a faction ship may have fitted.</summary>
    private static readonly HashSet<string> FactionSystems = new() { "WFSystemStrongbox" };

    private static readonly Regex ProtoBlock = new(@"^- proto: (\S+)\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Each vessel's stock weapon points, read once from its grid.</summary>
    private readonly Dictionary<string, int> _stockPoints = new();

    /// <summary>The weapon points a vessel's stock hardpoints hold, each filled at its size.</summary>
    public int StockPoints(ProtoId<VesselPrototype> vesselId)
    {
        if (_stockPoints.TryGetValue(vesselId, out var cached))
            return cached;

        var points = 0;
        if (_proto.TryIndex(vesselId, out var vessel))
        {
            try
            {
                var text = _resources.ContentFileReadAllText(vessel.ShuttlePath);
                var name = _factory.GetComponentName<HardpointComponent>();
                var matches = ProtoBlock.Matches(text);
                for (var i = 0; i < matches.Count; i++)
                {
                    var id = matches[i].Groups[1].Value;
                    if (!_proto.TryIndex<EntityPrototype>(id, out var proto) || !proto.TryGetComponent<HardpointComponent>(name, out var mount))
                        continue;

                    var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
                    var block = text.AsSpan(matches[i].Index, end - matches[i].Index);
                    var count = Regex.Count(block.ToString(), @"^  - uid:", RegexOptions.Multiline);
                    points += count * WeaponPoints(mount.CompatibleSizes);
                }
            }
            catch (Exception e)
            {
                Log.Error($"Failed to read the stock layout of {vesselId}: {e}");
            }
        }

        _stockPoints[vesselId] = points;
        return points;
    }

    /// <summary>A faction ship's weapon budget: what its stock hardpoints hold.</summary>
    public int FactionBudget(EntityUid hull)
    {
        return TryComp<VesselComponent>(hull, out var vessel) ? StockPoints(vessel.VesselId) : 0;
    }

    /// <summary>What a faction ship's guns and mounted systems take of its weapon budget, fitted or not.</summary>
    public int FactionSpent(EntityUid hull)
    {
        var spent = 0;
        foreach (var (_, hardpoint) in Hardpoints(hull))
        {
            if (hardpoint.anchoring is { } gun && !TerminatingOrDeleted(gun))
                spent += PointsOf(gun);
        }

        return spent;
    }

    private WFShipwrightHullView FactionHullView(EntityUid hull, Entity<WFOutpostComponent> outpost)
    {
        var mounts = new List<WFShipwrightMountView>();
        foreach (var (hardpoint, comp) in Hardpoints(hull))
        {
            var gun = comp.anchoring is { } anchored && !TerminatingOrDeleted(anchored) ? MetaData(anchored).EntityPrototype?.ID : null;
            var xform = Transform(hardpoint);
            var tile = new Vector2i((int) MathF.Floor(xform.LocalPosition.X), (int) MathF.Floor(xform.LocalPosition.Y));
            mounts.Add(new WFShipwrightMountView(GetNetEntity(hardpoint), tile, MetaData(hardpoint).EntityPrototype?.ID ?? string.Empty,
                comp.CompatibleSizes.ToString(), (int) comp.CompatibleTypes, true, true, gun, true));
        }

        var systems = new Dictionary<string, int>();
        if (_proto.TryIndex<WFShipwrightSystemPrototype>("WFSystemStrongbox", out var strongbox))
            systems[strongbox.ID] = FittedOf(hull, strongbox, any: true).Count;

        var registered = TryComp<WFHideoutShipComponent>(hull, out var owned);
        var budget = FactionBudget(hull);
        var spent = FactionSpent(hull);
        return new WFShipwrightHullView(Name(hull), null, registered, registered && owned!.Owner == outpost.Comp.Owner,
            Survey(hull, outpost).Tiles, budget, spent, mounts, systems, new Dictionary<string, int>(), 0, 0, spent <= budget,
            GetNetEntity(hull), mounts.Count, new List<Vector2i>());
    }

    /// <summary>
    /// Refits the owner's faction ship on the hangar: guns on its own hardpoints and strongboxes, within its weapon budget.
    /// <paramref name="reason"/> is a loc id when it fails.
    /// </summary>
    public bool TryFactionRefit(EntityUid user, Entity<WFShipwrightConsoleComponent> console, Dictionary<NetEntity, string> mounts,
        Dictionary<string, int> systems, out string reason)
    {
        if (Blocked(console, out var dock) is { } blocked)
        {
            reason = blocked;
            return false;
        }

        TryOutpost(console, out var outpost);
        TryGetBay(console, out var pad);
        if (!_outposts.IsOwnerOf(user, outpost.Comp))
        {
            reason = "wf-hideout-build-not-owner";
            return false;
        }

        if (pad.Comp.Parked is not { } hull || TerminatingOrDeleted(hull) || !TryComp<WFHideoutShipComponent>(hull, out var owned)
            || owned.Owner != outpost.Comp.Owner || HasComp<WFShipwrightShipComponent>(hull))
        {
            reason = "wf-shipwright-faction-not-registered";
            return false;
        }

        var rules = Rules;
        var guns = Guns().ToDictionary(gun => gun.Id);
        var items = new List<WFDrydockWorkItem>();
        var spent = FactionSpent(hull);
        var price = 0;
        foreach (var (net, gunId) in mounts)
        {
            if (!TryGetEntity(net, out var hardpointUid) || Transform(hardpointUid.Value).ParentUid != hull
                || !TryComp<HardpointComponent>(hardpointUid, out var hardpoint))
            {
                reason = "wf-shipwright-refit-invalid";
                return false;
            }

            var xform = Transform(hardpointUid.Value);
            if (hardpoint.anchoring is { } old && !TerminatingOrDeleted(old))
            {
                if (MetaData(old).EntityPrototype?.ID == gunId)
                    continue;

                spent -= PointsOf(old);
                price += rules.Labour;
                items.Add(new WFDrydockWorkItem { Kind = WFDrydockWorkKind.Remove, Target = old, LocalPos = xform.LocalPosition });
            }

            if (gunId.Length > 0 && !TryAddGun(items, guns, gunId, (int) hardpoint.CompatibleSizes, (int) hardpoint.CompatibleTypes,
                    xform.LocalPosition, xform.LocalRotation, 1f, ref spent, ref price))
            {
                reason = "wf-shipwright-refit-wont-fit";
                return false;
            }
        }

        var reserved = new HashSet<Vector2i>();
        foreach (var (systemId, count) in systems)
        {
            if (!FactionSystems.Contains(systemId) || !_proto.TryIndex<WFShipwrightSystemPrototype>(systemId, out var system)
                || count < 0 || system.Limit > 0 && count > system.Limit)
            {
                reason = "wf-shipwright-faction-system";
                return false;
            }

            var fitted = FittedOf(hull, system, any: true);
            for (var i = count; i < fitted.Count; i++)
            {
                price += rules.Labour;
                items.Add(new WFDrydockWorkItem { Kind = WFDrydockWorkKind.Remove, Target = fitted[i], LocalPos = PositionOn(hull, fitted[i]) });
            }

            for (var i = fitted.Count; i < count; i++)
            {
                if (!TryFindFreeTile(hull, reserved, out var tile))
                {
                    reason = "wf-shipwright-refit-no-room";
                    return false;
                }

                reserved.Add(tile);
                price += system.Price;
                items.Add(new WFDrydockWorkItem { Kind = WFDrydockWorkKind.Install, Prototype = system.Install, LocalPos = (Vector2) tile + new Vector2(0.5f) });
            }
        }

        if (items.Count == 0)
        {
            reason = "wf-shipwright-refit-nothing";
            return false;
        }

        if (spent > FactionBudget(hull))
        {
            reason = "wf-shipwright-faction-over-stock";
            return false;
        }

        if (!_bank.TryBankWithdraw(user, price))
        {
            reason = "wf-hideout-cannot-afford";
            return false;
        }

        Spread(items, price);
        console.Comp.Job = WFShipwrightJob.Refit;
        console.Comp.Orderer = user;
        _drydock.StartJob(console, dock!, hull, user, items, price);
        _adminLog.Add(LogType.WFOutpost, LogImpact.Low, $"{ToPrettyString(user):user} ordered a {items.Count}-item faction refit of {ToPrettyString(hull):grid} for {price}");
        reason = string.Empty;
        return true;
    }
}
