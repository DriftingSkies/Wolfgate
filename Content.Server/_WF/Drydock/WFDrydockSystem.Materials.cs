using System.Globalization;
using System.Linq;
using Content.Shared._WF.Drydock;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;

namespace Content.Server._WF.Drydock;

public sealed partial class WFDrydockSystem
{
    [Dependency] private SharedOreSiloSystem _oreSilo = default!;

    /// <summary>
    /// Sets each item's material bill for a console that pays in materials, then pays what
    /// refit units it can with parts from the cranes' parts silos.
    /// </summary>
    private void AssignMaterials(WFDrydockMaterialPaymentComponent pay, List<WFDrydockWorkItem> items, List<EntityUid> cranes)
    {
        foreach (var item in items)
        {
            if (item.Kind == WFDrydockWorkKind.Upgrade)
            {
                item.Materials = RefitBill(pay, item);
                continue;
            }

            if (item.Kind == WFDrydockWorkKind.Tile)
            {
                item.Materials = pay.BreakTileYield.ToDictionary(t => t.Key.Id, t => (int) Math.Ceiling(t.Value * pay.StructureRepairFactor));
                continue;
            }

            if (item.Prototype is { } protoId && _prototype.TryIndex(protoId, out var proto) && ConstructionMaterials(proto) is { Count: > 0 } construction)
            {
                var share = IsWall(proto) ? pay.StructureYield * pay.StructureRepairFactor : pay.RepairShare;
                item.Materials = ShareBill(construction, share * item.Share);
                continue;
            }

            item.Materials = ItemBill(pay, item.Cost - item.RefitCost);
        }

        // A rebuild restoring a refit pays the refit's materials too.
        foreach (var item in items)
        {
            if (item.Kind != WFDrydockWorkKind.Upgrade && item.RefitMaterials != null && item.Materials != null)
                AddBill(item.Materials, item.RefitMaterials);
        }

        AssignParts(cranes, items);
    }

    /// <summary>A refit item's bill: its tiers' own materials, or the rates on its cost when they list none.</summary>
    private static Dictionary<string, int> RefitBill(WFDrydockMaterialPaymentComponent pay, WFDrydockWorkItem item)
    {
        return item.RefitMaterials is { Count: > 0 } refit
            ? new Dictionary<string, int>(refit)
            : ItemBill(pay, item.Cost);
    }

    /// <summary>A share of a set of construction materials, rounded up.</summary>
    private static Dictionary<string, int> ShareBill(Dictionary<string, int> construction, float share)
    {
        var bill = new Dictionary<string, int>();
        foreach (var (id, amount) in construction)
        {
            var units = (int) Math.Ceiling(amount * share);
            if (units > 0)
                bill[id] = units;
        }

        return bill;
    }

    /// <summary>A material bill for a cost: the cost times each rate.</summary>
    private static Dictionary<string, int> ItemBill(WFDrydockMaterialPaymentComponent pay, int cost)
    {
        var bill = new Dictionary<string, int>();
        foreach (var (id, rate) in pay.Rates)
        {
            var amount = (int) Math.Ceiling(cost * rate);
            if (amount > 0)
                bill[id] = amount;
        }

        return bill;
    }

    /// <summary>Adds each material of one bill into another.</summary>
    private static void AddBill(Dictionary<string, int> bill, Dictionary<string, int> add)
    {
        foreach (var (id, amount) in add)
        {
            bill[id] = bill.GetValueOrDefault(id) + amount;
        }
    }

    /// <summary>Sums the material bills of work items.</summary>
    private static Dictionary<string, int> SumBills(IEnumerable<WFDrydockWorkItem> items)
    {
        var bill = new Dictionary<string, int>();
        foreach (var item in items)
        {
            if (item.Materials != null)
                AddBill(bill, item.Materials);
        }

        return bill;
    }

    /// <summary>The cranes a console's next job would use: its working berth cranes, or none without a berth.</summary>
    private List<EntityUid> WorkingCranes(EntityUid console, WFDrydockConsoleComponent comp)
    {
        return TryGetBerth(console, comp, out var berth) ? berth.Cranes : new List<EntityUid>();
    }

    /// <summary>The cranes a job drives, skipping empty slots.</summary>
    private static List<EntityUid> JobCranes(WFDrydockJobComponent job)
    {
        return job.Slots.Where(s => s.Crane != null).Select(s => s.Crane!.Value).ToList();
    }

    /// <summary>The silo a crane draws from, if it is linked to one that can transmit to it.</summary>
    private EntityUid? CraneSilo(EntityUid crane)
    {
        if (CompOrNull<OreSiloClientComponent>(crane)?.Silo is not { } silo || !TryComp<OreSiloComponent>(silo, out var siloComp))
            return null;

        return _oreSilo.CanTransmitMaterials((silo, siloComp, Transform(silo)), crane) ? silo : null;
    }

    /// <summary>The distinct silos that can transmit to at least one of the cranes, in crane order.</summary>
    private List<EntityUid> SourceSilos(List<EntityUid> cranes)
    {
        var silos = new List<EntityUid>();
        foreach (var crane in cranes)
        {
            if (CraneSilo(crane) is { } silo && !silos.Contains(silo))
                silos.Add(silo);
        }

        return silos;
    }

    /// <summary>An entity's own stock, ignoring any linked silo.</summary>
    private Dictionary<string, int> LocalStored(EntityUid uid)
    {
        var stored = new Dictionary<string, int>();
        if (!TryComp<MaterialStorageComponent>(uid, out var storage))
            return stored;

        foreach (var (id, amount) in _materials.GetStoredMaterials((uid, storage), localOnly: true))
        {
            stored[id] = amount;
        }

        return stored;
    }

    /// <summary>
    /// Materials a console's cranes can pay with: each crane's own stock plus each distinct linked silo once,
    /// so several cranes on one silo don't count it twice.
    /// </summary>
    private Dictionary<string, int> Availability(List<EntityUid> cranes)
    {
        var total = new Dictionary<string, int>();
        foreach (var crane in cranes)
            AddBill(total, LocalStored(crane));

        foreach (var silo in SourceSilos(cranes))
            AddBill(total, LocalStored(silo));

        return total;
    }

    /// <summary>Adds (positive) or takes (negative) materials from one entity's own storage, ignoring linked silos.</summary>
    private bool TryChangeLocal(EntityUid uid, Dictionary<string, int> change)
    {
        if (!TryComp<MaterialStorageComponent>(uid, out var storage))
            return false;

        return _materials.TryChangeMaterialAmount((uid, storage), change, localOnly: true);
    }

    /// <summary>
    /// The items a stock of materials pays for, in work order. Once a tile is left out so are the parts, which might
    /// stand on it.
    /// </summary>
    private static List<WFDrydockWorkItem> Affordable(List<WFDrydockWorkItem> items, Dictionary<string, int> stock)
    {
        var left = new Dictionary<string, int>(stock);
        var tileLeftOut = false;
        var affordable = new List<WFDrydockWorkItem>();
        foreach (var item in items.OrderBy(Rank))
        {
            var bill = item.Materials ?? new Dictionary<string, int>();
            if ((tileLeftOut && item.Kind == WFDrydockWorkKind.Entity) || bill.Any(m => left.GetValueOrDefault(m.Key) < m.Value))
            {
                tileLeftOut |= item.Kind == WFDrydockWorkKind.Tile;
                continue;
            }

            foreach (var (id, amount) in bill)
                left[id] = left.GetValueOrDefault(id) - amount;

            affordable.Add(item);
        }

        return affordable;
    }

    /// <summary>
    /// Takes the items' bill from the console's silos, then its cranes, and their parts from the parts silos, all or
    /// nothing; denies with the shortfall.
    /// </summary>
    private bool TryPayMaterials(EntityUid console, WFDrydockConsoleComponent comp, EntityUid actor, List<WFDrydockWorkItem> items)
    {
        var bill = SumBills(items);
        var parts = SumParts(items);
        var cranes = WorkingCranes(console, comp);
        var silos = SourceSilos(cranes);

        // Each source is visited once per material, so no stock is counted or taken twice.
        var draws = new Dictionary<EntityUid, Dictionary<string, int>>();
        var missing = new Dictionary<string, int>();
        foreach (var (id, amount) in bill)
        {
            var left = amount;
            foreach (var source in silos.Concat(cranes))
            {
                if (left <= 0)
                    break;

                var take = Math.Min(left, _materials.GetMaterialAmount(source, id, localOnly: true));
                if (take <= 0)
                    continue;

                if (!draws.TryGetValue(source, out var draw))
                    draws[source] = draw = new Dictionary<string, int>();

                draw[id] = take;
                left -= take;
            }

            if (left > 0)
                missing[id] = left;
        }

        var missingParts = MissingParts(cranes, parts);
        if (missing.Count > 0 || missingParts.Count > 0)
            return Deny(console, comp, actor, Loc.GetString("drydock-console-insufficient-materials", ("missing", FormatBill(missing, missingParts))));

        var applied = new List<(EntityUid Uid, Dictionary<string, int> Draw)>();
        foreach (var (source, draw) in draws)
        {
            if (!TryChangeLocal(source, draw.ToDictionary(p => p.Key, p => -p.Value)))
            {
                foreach (var (uid, undo) in applied)
                    TryChangeLocal(uid, undo);

                return Deny(console, comp, actor, Loc.GetString("drydock-console-transaction-failed"));
            }

            applied.Add((source, draw));
        }

        TakeParts(console, cranes, parts);
        return true;
    }

    /// <summary>
    /// Returns the items' bill to the first crane with storage, or to its linked silo, and their parts to a parts silo;
    /// gives both.
    /// </summary>
    private (Dictionary<string, int> Materials, Dictionary<string, int> Parts) ReturnMaterials(EntityUid console,
        List<EntityUid> cranes, List<WFDrydockWorkItem> items)
    {
        var bill = SumBills(items);
        var parts = SumParts(items);
        ReturnParts(cranes, parts);
        if (bill.Count == 0)
            return (bill, parts);

        var crane = cranes.FirstOrDefault(c => HasComp<MaterialStorageComponent>(c));
        if (!crane.Valid || !TryChangeLocal(CraneSilo(crane) ?? crane, bill))
            Log.Warning($"Could not return drydock materials to {ToPrettyString(console)}; its cranes or silo are gone or full.");

        return (bill, parts);
    }

    /// <summary>Lists a material bill as localized names and sheet amounts, comma separated.</summary>
    private string FormatMaterials(Dictionary<string, int> bill)
    {
        var entries = new List<string>();
        foreach (var (id, amount) in bill)
        {
            var proto = _prototype.Index<MaterialPrototype>(id);
            var sheets = amount / (float) _materials.GetSheetVolume(proto);
            entries.Add(Loc.GetString("drydock-console-material-amount",
                ("name", Loc.GetString(proto.Name)),
                ("sheets", sheets.ToString("0.##", CultureInfo.InvariantCulture))));
        }

        return string.Join(", ", entries);
    }
}
