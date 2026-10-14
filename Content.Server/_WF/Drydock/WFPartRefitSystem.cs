using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server._NF.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.Drydock;
using Content.Shared.Construction.Components;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Drydock;

/// <summary>
/// Part rating refits (<see cref="WFPartRatingRefit"/>): a category of ship machines whose stock parts are swapped for
/// higher-rated ones. Each part is a unit.
/// </summary>
public sealed class WFPartRefitSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ConstructionSystem _construction = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    /// <summary>Replacement part prototype for a (part type, rating) pair.</summary>
    private readonly Dictionary<(string Part, int Rating), EntProtoId> _replacements = new();

    /// <summary>Part pairs already reported as missing a replacement, so each is logged once.</summary>
    private readonly HashSet<(string Part, int Rating)> _warned = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        SubscribeLocalEvent<WFRefitSurveyEvent<WFPartRatingRefit>>(OnSurvey);
        SubscribeLocalEvent<WFRefitProspectEvent<WFPartRatingRefit>>(OnProspect);
        SubscribeLocalEvent<WFRefitApplyEvent<WFPartRatingRefit>>(OnApply);
        RebuildReplacements();
    }

    /// <summary>Counts each covered machine's parts at the tier, and lists the machines with parts below it.</summary>
    private void OnSurvey(WFRefitSurveyEvent<WFPartRatingRefit> args)
    {
        var survey = args.Survey;
        var rating = args.Effect.Ratings[survey.Tier - 1];
        var machines = new List<EntityUid>();
        GetMachines(survey.Ship, args.Effect, machines);
        foreach (var uid in machines)
        {
            var machine = Comp<MachineComponent>(uid);
            WFRefitTarget? target = null;
            foreach (var part in machine.PartContainer.ContainedEntities)
            {
                if (!TryComp<MachinePartComponent>(part, out var partComp))
                    continue;

                if (partComp.Rating >= rating)
                {
                    survey.UnitsAtTier += PartCount(part);
                    continue;
                }

                if (!_replacements.ContainsKey((partComp.PartType.Id, rating)))
                    continue;

                target ??= new WFRefitTarget(uid, Transform(uid).LocalPosition, survey.Tier);
                target.Lacking[TierOf(args.Effect, partComp.Rating)] += PartCount(part);
            }

            if (target != null)
                survey.Below.Add(target);
        }
    }

    /// <summary>Counts a covered machine prototype's stock parts, from its board, by the tier they reach.</summary>
    private void OnProspect(WFRefitProspectEvent<WFPartRatingRefit> args)
    {
        var prospect = args.Prospect;
        if (!_prototype.TryIndex(prospect.Prototype, out var proto) || !Covers(proto, args.Effect) ||
            !proto.TryGetComponent<MachineComponent>(out var machine, Factory) || machine.Board is not { } boardId ||
            !_prototype.TryIndex<EntityPrototype>(boardId.Id, out var boardProto) ||
            !boardProto.TryGetComponent<MachineBoardComponent>(out var board, Factory))
        {
            return;
        }

        var rating = args.Effect.Ratings[prospect.Lacking.Length - 1];
        foreach (var (partType, amount) in board.Requirements)
        {
            var stock = machine.PartOverrides.TryGetValue(partType, out var overridden)
                ? overridden
                : _prototype.Index<MachinePartPrototype>(partType).StockPartPrototype;
            if (!_prototype.TryIndex(stock, out var stockProto) ||
                !stockProto.TryGetComponent<MachinePartComponent>(out var part, Factory) ||
                part.Rating >= rating || !_replacements.ContainsKey((partType.Id, rating)))
            {
                continue;
            }

            prospect.Lacking[TierOf(args.Effect, part.Rating)] += amount;
        }
    }

    private void OnApply(WFRefitApplyEvent<WFPartRatingRefit> args)
    {
        if (IsCovered(args.Target, args.Effect))
            ApplyRating(args.Target, args.Effect.Ratings[args.Tier - 1]);
    }

    /// <summary>Collects the covered machines anchored on a ship.</summary>
    private void GetMachines(EntityUid ship, WFPartRatingRefit effect, List<EntityUid> result)
    {
        if (!TryComp<MapGridComponent>(ship, out var grid))
            return;

        var machines = new HashSet<Entity<MachineComponent>>();
        _lookup.GetLocalEntitiesIntersecting(ship, grid.LocalAABB, machines);
        foreach (var ent in machines)
        {
            var xform = Transform(ent);
            if (xform.ParentUid == ship && xform.Anchored && IsCovered(ent.Owner, effect))
                result.Add(ent.Owner);
        }
    }

    /// <summary>The tier a machine's lowest-rated part reaches on a refit line; 0 is stock.</summary>
    public int GetTier(EntityUid machine, WFShipRefitPrototype refit)
    {
        if (refit.Effect is not WFPartRatingRefit effect || !TryComp<MachineComponent>(machine, out var comp))
            return 0;

        var rating = int.MaxValue;
        foreach (var part in comp.PartContainer.ContainedEntities)
        {
            if (TryComp<MachinePartComponent>(part, out var partComp))
                rating = Math.Min(rating, partComp.Rating);
        }

        return rating == int.MaxValue ? 0 : TierOf(effect, rating);
    }

    /// <summary>Swaps a machine's parts rated below a rating for parts of that rating.</summary>
    private void ApplyRating(EntityUid machine, int rating)
    {
        if (!TryComp<MachineComponent>(machine, out var comp))
            return;

        var coords = Transform(machine).Coordinates;
        var changed = false;
        foreach (var part in new List<EntityUid>(comp.PartContainer.ContainedEntities))
        {
            if (!TryComp<MachinePartComponent>(part, out var partComp) || partComp.Rating >= rating)
                continue;

            var key = (partComp.PartType.Id, rating);
            if (!_replacements.TryGetValue(key, out var replacement))
            {
                if (_warned.Add(key))
                    Log.Warning($"No stock machine part of type {key.Item1} at rating {key.Item2} for drydock refit.");
                continue;
            }

            var count = PartCount(part);
            _container.Remove(part, comp.PartContainer);
            QueueDel(part);

            for (var i = 0; i < count; i++)
            {
                _container.Insert(Spawn(replacement, coords), comp.PartContainer);
            }

            changed = true;
        }

        if (changed)
            _construction.RefreshParts(machine, comp);
    }

    /// <summary>The tier a part rating reaches on a refit line: how many leading tiers it meets.</summary>
    private static int TierOf(WFPartRatingRefit effect, int rating)
    {
        var tier = 0;
        while (tier < effect.Ratings.Count && effect.Ratings[tier] <= rating)
        {
            tier++;
        }

        return tier;
    }

    /// <summary>Whether an entity is a machine with parts of the category a refit covers.</summary>
    private bool IsCovered(EntityUid uid, WFPartRatingRefit effect)
    {
        if (!TryComp<MachineComponent>(uid, out var machine) || !HasMachineParts(machine))
            return false;

        return effect.Target switch
        {
            WFPartRefitTarget.LinearThruster => TryComp<ThrusterComponent>(uid, out var linear) && linear.Type == ThrusterType.Linear,
            WFPartRefitTarget.Gyroscope => TryComp<ThrusterComponent>(uid, out var angular) && angular.Type == ThrusterType.Angular,
            WFPartRefitTarget.Power => HasComp<UpgradeBatteryComponent>(uid)
                                       || HasComp<UpgradePowerSupplierComponent>(uid)
                                       || HasComp<UpgradePowerSupplyRampingComponent>(uid),
            _ => false,
        };
    }

    /// <summary>Whether a prototype is a machine of the category a refit covers.</summary>
    private bool Covers(EntityPrototype proto, WFPartRatingRefit effect)
    {
        return effect.Target switch
        {
            WFPartRefitTarget.LinearThruster => proto.TryGetComponent<ThrusterComponent>(out var linear, Factory) && linear.Type == ThrusterType.Linear,
            WFPartRefitTarget.Gyroscope => proto.TryGetComponent<ThrusterComponent>(out var angular, Factory) && angular.Type == ThrusterType.Angular,
            WFPartRefitTarget.Power => proto.TryGetComponent<UpgradeBatteryComponent>(out _, Factory)
                                       || proto.TryGetComponent<UpgradePowerSupplierComponent>(out _, Factory)
                                       || proto.TryGetComponent<UpgradePowerSupplyRampingComponent>(out _, Factory),
            _ => false,
        };
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<EntityPrototype>())
            RebuildReplacements();
    }

    /// <summary>Maps each (part type, rating) to a stock part prototype, preferring "StockPart" IDs for determinism.</summary>
    private void RebuildReplacements()
    {
        _replacements.Clear();
        var machinePartName = Factory.GetComponentName<MachinePartComponent>();

        foreach (var proto in _prototype.EnumeratePrototypes<EntityPrototype>())
        {
            if (proto.Abstract || !proto.Components.TryGetValue(machinePartName, out var entry))
                continue;

            var part = (MachinePartComponent) entry.Component;
            var key = (part.PartType.Id, part.Rating);
            if (!_replacements.TryGetValue(key, out var existing) || PrefersOver(proto.ID, existing.Id))
                _replacements[key] = proto.ID;
        }
    }

    private static bool PrefersOver(string candidate, string existing)
    {
        var candidateStock = candidate.EndsWith("StockPart", StringComparison.Ordinal);
        var existingStock = existing.EndsWith("StockPart", StringComparison.Ordinal);
        if (candidateStock != existingStock)
            return candidateStock;

        return string.CompareOrdinal(candidate, existing) < 0;
    }

    /// <summary>Parts in a part entity: its stack count, or 1 when it doesn't stack.</summary>
    private int PartCount(EntityUid part)
    {
        return TryComp<StackComponent>(part, out var stack) ? stack.Count : 1;
    }

    private bool HasMachineParts(MachineComponent machine)
    {
        foreach (var part in machine.PartContainer.ContainedEntities)
        {
            if (HasComp<MachinePartComponent>(part))
                return true;
        }

        return false;
    }
}
