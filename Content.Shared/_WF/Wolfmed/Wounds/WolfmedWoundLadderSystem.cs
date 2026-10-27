using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.FixedPoint;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// Wound ladders: a part holds one wound of each kind, which gets worse instead of more wounds piling up beside it.
/// Called from <see cref="WoundSystem"/> before it creates or merges a wound; server only, as wound creation is.
/// </summary>
// Severity moves between rungs as a share of each rung's maximum: a slash at 150 of 200 that becomes an arterial bleed
// (max 120) carries 90. A worse rung arriving takes over at the larger of its own and the carried severity; a lesser one
// adds wolfmed.wound_ladder_lower_share of its rescaled severity. A full wound that is pushed further opens the next
// rung at wolfmed.wound_ladder_entry of its maximum plus the excess.
public sealed class WolfmedWoundLadderSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private AmputationSystem _amputation = default!;
    [Dependency] private WolfmedEmbeddedObjectSystem _embedded = default!;

    private readonly Dictionary<ProtoId<WoundPrototype>, (WolfmedWoundLadderPrototype Ladder, int Tier)> _rungs = new();

    /// <summary>Set while this system creates a rung itself, so that creation is not folded again.</summary>
    private bool _creating;

    /// <summary>The part the current hit landed on, and the ladders it has already made a wound on.</summary>
    private EntityUid? _hitPart;

    private readonly HashSet<WolfmedWoundLadderPrototype> _hitLadders = new();

    /// <summary>
    /// Called as one hit's wounds are made on a part. Wounds of a ladder made by the same hit are one injury: the
    /// lesser ones add only their share and a worse one does not add the lesser one's severity on top.
    /// </summary>
    public void BeginHit(EntityUid part)
    {
        _hitPart = part;
        _hitLadders.Clear();
    }

    /// <summary>Closes the hit <see cref="BeginHit"/> opened; wounds made after it count as a new injury.</summary>
    public void EndHit()
    {
        _hitPart = null;
        _hitLadders.Clear();
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        Rebuild();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<WolfmedWoundLadderPrototype>())
            Rebuild();
    }

    private void Rebuild()
    {
        _rungs.Clear();
        foreach (var ladder in _prototypes.EnumeratePrototypes<WolfmedWoundLadderPrototype>())
        {
            for (var i = 0; i < ladder.Tiers.Count; i++)
                _rungs[ladder.Tiers[i]] = (ladder, i);
        }
    }

    /// <summary>The ladder and rung of a wound prototype, if it is on one.</summary>
    public bool TryGetRung(ProtoId<WoundPrototype> wound, out WolfmedWoundLadderPrototype ladder, out int tier)
    {
        if (_rungs.TryGetValue(wound, out var rung))
        {
            (ladder, tier) = rung;
            return true;
        }

        ladder = default!;
        tier = -1;
        return false;
    }

    /// <summary>
    /// Folds a new wound into the part's wound of the same kind. True when it did, with the wound that now carries
    /// it; false when the part has none of that kind yet and the wound is created as usual.
    /// </summary>
    public bool TryFold(Entity<WoundableComponent> part, WoundPrototype incoming, FixedPoint2 severity, bool reopen,
        out EntityUid? result)
    {
        result = null;
        if (_creating || !_config.GetCVar(WolfmedCVars.WoundLadders) ||
            !TryGetRung(incoming.ID, out var ladder, out var tier))
            return false;

        Entity<WoundComponent>? existing = null;
        var existingTier = -1;
        foreach (var wound in _wounds.GetWounds(part.AsNullable()))
        {
            if (wound.Comp.State is WoundState.Healed or WoundState.Scarred ||
                !TryGetRung(wound.Comp.Prototype, out var other, out var otherTier) || other != ladder ||
                otherTier <= existingTier)
                continue;

            existing = wound;
            existingTier = otherTier;
        }

        var sameHit = _hitPart == part.Owner && !_hitLadders.Add(ladder);
        if (existing is not { } held)
            return false;

        if (reopen && held.Comp.State != WoundState.Open)
            _wounds.SetWoundState(held.AsNullable(), WoundState.Open);

        // A later hit adds in full on either side. Within one hit, which often makes several wounds of a kind at once
        // (a cut, its tendon, its artery), a worse rung takes over at whichever is more severe and a lesser one adds
        // only its share.
        if (tier > existingTier)
        {
            var carried = Rescale(held.Comp.Severity, held.Comp.Prototype, incoming.ID);
            result = Replace(part, held, ladder, tier, sameHit ? FixedPoint2.Max(severity, carried) : severity + carried);
        }
        else
        {
            var add = tier == existingTier ? severity : Rescale(severity, incoming.ID, held.Comp.Prototype);
            if (sameHit && tier < existingTier)
                add *= Math.Clamp(_config.GetCVar(WolfmedCVars.WoundLadderLowerShare), 0f, 1f);
            result = Grow(part, held, ladder, existingTier, add);
        }

        return true;
    }

    /// <summary>Adds severity to a wound; what its maximum cannot hold climbs to the next rung.</summary>
    private EntityUid? Grow(Entity<WoundableComponent> part, Entity<WoundComponent> wound,
        WolfmedWoundLadderPrototype ladder, int tier, FixedPoint2 add)
    {
        var maximum = _prototypes.Index(wound.Comp.Prototype).MaximumSeverity;
        var room = maximum - wound.Comp.Severity;
        if (add <= room)
        {
            _wounds.ChangeSeverity(wound.AsNullable(), add);
            return wound;
        }

        if (room > FixedPoint2.Zero)
            _wounds.ChangeSeverity(wound.AsNullable(), room);

        var excess = add - FixedPoint2.Max(room, FixedPoint2.Zero);
        var next = NextRung(part, ladder, tier);
        if (next < 0)
        {
            if (ladder.Dismembers)
                _amputation.MarkSeverable(part);
            return wound;
        }

        var nextId = ladder.Tiers[next];
        var entry = _prototypes.Index(nextId).MaximumSeverity * Math.Clamp(_config.GetCVar(WolfmedCVars.WoundLadderEntry), 0f, 1f);
        return Replace(part, wound, ladder, next, entry + Rescale(excess, wound.Comp.Prototype, nextId));
    }

    /// <summary>Swaps a wound for another rung of its ladder at the given severity, climbing on if it overflows.</summary>
    private EntityUid? Replace(Entity<WoundableComponent> part, Entity<WoundComponent> old,
        WolfmedWoundLadderPrototype ladder, int tier, FixedPoint2 severity)
    {
        var id = ladder.Tiers[tier];
        var maximum = _prototypes.Index(id).MaximumSeverity;
        var start = FixedPoint2.Min(severity, maximum);

        // Whatever is still lodged in the old wound stays in the part.
        var lodged = CompOrNull<WolfmedEmbeddedObjectComponent>(old) is { Count: > 0 } held
            ? (held.Item, held.Count, held.MaxCount)
            : ((EntProtoId Item, int Count, int MaxCount)?) null;

        var replacing = new WolfmedWoundLadderReplacingEvent(part.Owner, old.Owner);
        RaiseLocalEvent(ref replacing);

        _wounds.RemoveWound(old.AsNullable());
        EntityUid? created;
        _creating = true;
        try
        {
            created = _wounds.CreateOrMergeWound(part.AsNullable(), id, start);
        }
        finally
        {
            _creating = false;
        }

        if (created is not { } wound || !TryComp(wound, out WoundComponent? comp))
            return created;

        if (lodged is { } objects)
            _embedded.Add(wound, objects.Item, objects.Count, objects.MaxCount);

        return severity > maximum ? Grow(part, (wound, comp), ladder, tier, severity - maximum) : wound;
    }

    /// <summary>The next rung above <paramref name="tier"/> this part can carry, or -1.</summary>
    private int NextRung(Entity<WoundableComponent> part, WolfmedWoundLadderPrototype ladder, int tier)
    {
        for (var next = tier + 1; next < ladder.Tiers.Count; next++)
        {
            if (_wounds.CanCreateWound(part.AsNullable(), ladder.Tiers[next]))
                return next;
        }

        return -1;
    }

    /// <summary>A severity on one wound's scale expressed on another's, by share of each one's maximum.</summary>
    private FixedPoint2 Rescale(FixedPoint2 severity, ProtoId<WoundPrototype> from, ProtoId<WoundPrototype> to)
    {
        var fromMax = _prototypes.Index(from).MaximumSeverity;
        return fromMax <= FixedPoint2.Zero ? severity : severity * _prototypes.Index(to).MaximumSeverity.Float() / fromMax.Float();
    }
}
