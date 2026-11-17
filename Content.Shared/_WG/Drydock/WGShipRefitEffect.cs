using System.Numerics;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Drydock;

/// <summary>
/// What a ship refit does. Each kind is a subclass of <see cref="WGShipRefitEffect{T}"/>, and a server system implements
/// it by handling that kind's <see cref="WGRefitSurveyEvent{T}"/>, <see cref="WGRefitProspectEvent{T}"/> and
/// <see cref="WGRefitApplyEvent{T}"/>.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class WGShipRefitEffect
{
    /// <summary>Fills a survey of a ship against a tier.</summary>
    public abstract void Survey(IEntityManager entMan, WGRefitSurvey survey);

    /// <summary>Fills a prospect for an entity a repair will rebuild.</summary>
    public abstract void Prospect(IEntityManager entMan, WGRefitProspect prospect);

    /// <summary>Brings one target up to a tier, if the refit covers it.</summary>
    public abstract void Apply(IEntityManager entMan, EntityUid target, int tier);
}

/// <summary>
/// Raises each request as an event typed by the refit kind, so a server system handles it.
/// </summary>
public abstract partial class WGShipRefitEffect<T> : WGShipRefitEffect where T : WGShipRefitEffect<T>
{
    /// <inheritdoc/>
    public override void Survey(IEntityManager entMan, WGRefitSurvey survey)
    {
        entMan.EventBus.RaiseEvent(EventSource.Local, new WGRefitSurveyEvent<T>((T) this, survey));
    }

    /// <inheritdoc/>
    public override void Prospect(IEntityManager entMan, WGRefitProspect prospect)
    {
        entMan.EventBus.RaiseEvent(EventSource.Local, new WGRefitProspectEvent<T>((T) this, prospect));
    }

    /// <inheritdoc/>
    public override void Apply(IEntityManager entMan, EntityUid target, int tier)
    {
        entMan.EventBus.RaiseEvent(EventSource.Local, new WGRefitApplyEvent<T>((T) this, target, tier));
    }
}

/// <summary>
/// A refit's survey of a ship against a tier: how many of its units already reach the tier, and the targets below it.
/// </summary>
public sealed class WGRefitSurvey(EntityUid ship, int tier)
{
    /// <summary>Ship surveyed.</summary>
    public readonly EntityUid Ship = ship;

    /// <summary>Tier checked, from 1.</summary>
    public readonly int Tier = tier;

    /// <summary>Units at or above the tier.</summary>
    public int UnitsAtTier;

    /// <summary>Targets with units below the tier.</summary>
    public readonly List<WGRefitTarget> Below = new();
}

/// <summary>
/// One target with units below a tier, and where it sits on the ship.
/// </summary>
public sealed class WGRefitTarget(EntityUid entity, Vector2 localPos, int tier)
{
    /// <summary>The entity the units belong to.</summary>
    public readonly EntityUid Entity = entity;

    /// <summary>Its position on the ship, for crane work.</summary>
    public readonly Vector2 LocalPos = localPos;

    /// <summary>Its units below the tier, by the tier they reach: index 0 counts units at stock.</summary>
    public readonly int[] Lacking = new int[tier];
}

/// <summary>
/// A refit's look at an entity a repair will rebuild stock: its units below a tier, or none if the refit doesn't
/// cover it.
/// </summary>
public sealed class WGRefitProspect(EntProtoId prototype, int tier)
{
    /// <summary>Prototype to be rebuilt.</summary>
    public readonly EntProtoId Prototype = prototype;

    /// <summary>Its units below the tier, by the tier they reach, as in <see cref="WGRefitTarget.Lacking"/>.</summary>
    public readonly int[] Lacking = new int[tier];
}

/// <summary>Raised to survey a ship for a refit of kind <typeparamref name="T"/>.</summary>
public sealed class WGRefitSurveyEvent<T>(T effect, WGRefitSurvey survey)
{
    public readonly T Effect = effect;
    public readonly WGRefitSurvey Survey = survey;
}

/// <summary>Raised to prospect a rebuilt entity for a refit of kind <typeparamref name="T"/>.</summary>
public sealed class WGRefitProspectEvent<T>(T effect, WGRefitProspect prospect)
{
    public readonly T Effect = effect;
    public readonly WGRefitProspect Prospect = prospect;
}

/// <summary>Raised to bring a target up to a tier of a refit of kind <typeparamref name="T"/>.</summary>
public sealed class WGRefitApplyEvent<T>(T effect, EntityUid target, int tier)
{
    public readonly T Effect = effect;
    public readonly EntityUid Target = target;
    public readonly int Tier = tier;
}
