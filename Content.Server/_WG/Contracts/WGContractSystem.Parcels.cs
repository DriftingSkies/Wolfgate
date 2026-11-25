using System.Linq;
using Content.Shared._WG.Contracts;
using Content.Shared.Interaction;
using Content.Shared.Lock;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Server.Storage.Components;
using Robust.Shared.Containers;

namespace Content.Server._WG.Contracts;

/// <summary>Supply and mining parcels: they take what the manifest still needs from goods they touch.</summary>
public sealed partial class WGContractSystem
{
    /// <summary>Where a parcel keeps the goods it has taken.</summary>
    public const string ParcelContainer = "wg-contract-parcel";

    private void InitializeParcels()
    {
        SubscribeLocalEvent<WGContractParcelComponent, InteractUsingEvent>(OnParcelInteractUsing);
        SubscribeLocalEvent<WGContractParcelComponent, AfterInteractEvent>(OnParcelAfterInteract);
    }

    /// <summary>Goods used on the parcel.</summary>
    private void OnParcelInteractUsing(Entity<WGContractParcelComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !GoodsSource(ent, args.Used))
            return;

        args.Handled = FillParcel(ent, args.Used, args.User);
    }

    /// <summary>The parcel used on goods, a pile or a box of them.</summary>
    private void OnParcelAfterInteract(Entity<WGContractParcelComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !GoodsSource(ent, target))
            return;

        args.Handled = FillParcel(ent, target, args.User);
    }

    /// <summary>Whether a parcel answers an entity: a stack, a storage or a manifest item. A table or labeler keeps its own interaction.</summary>
    private bool GoodsSource(EntityUid parcel, EntityUid uid)
    {
        if (HasComp<StackComponent>(uid) || HasComp<StorageComponent>(uid) || HasComp<EntityStorageComponent>(uid))
            return true;

        return TryComp<WGContractItemComponent>(parcel, out var item) && _contracts.TryGetValue(item.Contract, out var contract)
               && _proto.Index(contract.Prototype).Objective is WGContractSupply supply
               && contract.Goods.Any(line => Matches(supply.Goods[line.Line], uid) > 0);
    }

    /// <summary>Fills a parcel from a source and says what it took, or what it still needs. False if it isn't a live parcel.</summary>
    public bool FillParcel(EntityUid parcel, EntityUid source, EntityUid user)
    {
        if (!HasComp<WGContractParcelComponent>(parcel) || !TryComp<WGContractItemComponent>(parcel, out var item) || !_contracts.TryGetValue(item.Contract, out var contract)
            || contract.Status != WGContractStatus.Active || _proto.Index(contract.Prototype).Objective is not WGContractSupply supply)
            return false;

        var container = _containers.EnsureContainer<Container>(parcel, ParcelContainer);
        var have = Packed(contract, parcel);
        // A medkit or box the manifest wants goes in whole; else what it holds is gone through.
        var taken = Take(contract, supply, container, have, source);
        if (taken == 0)
        {
            foreach (var uid in Sources(source))
            {
                taken += Take(contract, supply, container, have, uid);
            }
        }

        var missing = Missing(contract, supply, have);
        _popup.PopupEntity(taken > 0
                ? Loc.GetString(missing.Count == 0 ? "wg-contract-parcel-full" : "wg-contract-parcel-took",
                    ("count", taken), ("missing", string.Join(", ", missing)))
                : Loc.GetString("wg-contract-parcel-nothing", ("missing", string.Join(", ", missing))),
            parcel, user);

        if (taken > 0 && missing.Count == 0 && OnStation(parcel, contract.Destination))
            Complete(contract);
        else if (taken > 0)
            _dirty = true;

        return true;
    }

    /// <summary>Everything an unlocked bag or box, or an open crate, holds.</summary>
    private IEnumerable<EntityUid> Sources(EntityUid source)
    {
        if (TryComp<LockComponent>(source, out var lockComp) && lockComp.Locked)
            return Array.Empty<EntityUid>();

        if (TryComp<StorageComponent>(source, out var storage))
            return storage.Container.ContainedEntities.ToList();

        if (TryComp<EntityStorageComponent>(source, out var crate) && crate.Open)
            return crate.Contents.ContainedEntities.ToList();

        return Array.Empty<EntityUid>();
    }

    /// <summary>Puts as much of one entity in the parcel as the manifest still needs, splitting a stack; returns the units taken.</summary>
    private int Take(WGContract contract, WGContractSupply supply, Container container, List<int> have, EntityUid uid)
    {
        if (TerminatingOrDeleted(uid) || HasComp<WGContractItemComponent>(uid) || HasComp<MobStateComponent>(uid)
            || HasComp<MindContainerComponent>(uid))
            return 0;

        for (var i = 0; i < contract.Goods.Count; i++)
        {
            if (Matches(supply.Goods[contract.Goods[i].Line], uid) is not (> 0 and var count))
                continue;

            // Counted for the first line it fits, as Packed counts it.
            var need = contract.Goods[i].Count - have[i];
            if (need <= 0)
                return 0;

            var taken = uid;
            if (count > need && TryComp<StackComponent>(uid, out var stack))
            {
                if (_stacks.Split(uid, need, Transform(uid).Coordinates, stack) is not { } split)
                    return 0;

                taken = split;
                count = need;
            }

            if (!_containers.Insert(taken, container))
            {
                if (taken != uid)
                    _stacks.TryAdd(taken, uid);
                return 0;
            }

            have[i] += count;
            return count;
        }

        return 0;
    }

    /// <summary>What a manifest still lacks, in words.</summary>
    private List<string> Missing(WGContract contract, WGContractSupply supply, List<int> have)
    {
        return contract.Goods
            .Select((line, i) => (line, have: have[i]))
            .Where(entry => entry.have < entry.line.Count)
            .Select(entry => Loc.GetString("wg-contract-crate-missing-line", ("name", GoodsName(supply.Goods[entry.line.Line])),
                ("count", entry.line.Count - entry.have)))
            .ToList();
    }

    /// <summary>What a parcel or crate holds.</summary>
    private IReadOnlyList<EntityUid> Contents(EntityUid holder)
    {
        if (TryComp<EntityStorageComponent>(holder, out var storage))
            return storage.Contents.ContainedEntities;

        return _containers.TryGetContainer(holder, ParcelContainer, out var container)
            ? container.ContainedEntities
            : Array.Empty<EntityUid>();
    }

    /// <summary>Hands a new parcel to whoever took the contract, if they have a free hand; else it stays where it was left.</summary>
    private void HandParcel(EntityUid parcel, EntityUid mob)
    {
        _hands.TryPickupAnyHand(mob, parcel, checkActionBlocker: false);
    }

    /// <summary>A parcel whose contract fell through gives its goods back where it is: into the bag it sits in, else the floor.</summary>
    private void SpillParcel(EntityUid parcel)
    {
        if (_containers.TryGetContainer(parcel, ParcelContainer, out var container))
            _containers.EmptyContainer(container, true);
    }
}
