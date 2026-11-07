using Content.Server.Body.Components;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WF.MeridianTraits.Hemophage;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.MeridianTraits.Hemophage;

/// <summary>
/// Spreads a hemophage's tumor into the organs around it, and lets a corrupted organ wither in a body that has no
/// tumor to feed it.
/// </summary>
public sealed partial class WFTumorCorruptionSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private OrganHealthSystem _organHealth = default!;
    [Dependency] private SharedBodySystem _body = default!;

    private static readonly ResPath OrganRsi = new("/Textures/_WF/MeridianTraits/hemophage_organs.rsi");
    /// <summary>The organ slot a hemophage's tongue sits in.</summary>
    public const string TongueSlot = "tongue";
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFTumorCorruptedComponent, OrganAddedToBodyEvent>(OnAddedToBody);
        SubscribeLocalEvent<WFTumorCorruptedComponent, OrganRemovedFromBodyEvent>(OnRemovedFromBody);
    }

    private void OnAddedToBody(Entity<WFTumorCorruptedComponent> ent, ref OrganAddedToBodyEvent args)
    {
        if (ent.Comp.Corrupted && IsTongue(ent))
            EnsureComp<WFHemophageDrainerComponent>(args.Body);
    }

    private void OnRemovedFromBody(Entity<WFTumorCorruptedComponent> ent, ref OrganRemovedFromBodyEvent args)
    {
        if (IsTongue(ent) && !TerminatingOrDeleted(args.OldBody))
            RemCompDeferred<WFHemophageDrainerComponent>(args.OldBody);
    }

    private bool IsTongue(EntityUid organ) => CompOrNull<OrganComponent>(organ)?.SlotId == TongueSlot;

    /// <summary>Starts corrupting an organ, or finishes it at once.</summary>
    public void Corrupt(EntityUid organ, bool instant)
    {
        var comp = EnsureComp<WFTumorCorruptedComponent>(organ);
        if (comp.Corrupted)
            return;

        if (instant)
            Finish((organ, comp));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextUpdate)
            return;

        _nextUpdate = now + UpdateInterval;
        Spread(now);

        var query = EntityQueryEnumerator<WFTumorCorruptedComponent, OrganComponent>();
        while (query.MoveNext(out var uid, out var corrupted, out var organ))
        {
            var hosted = organ.Body is { } body && HasComp<WFHemophageComponent>(body);
            if (!corrupted.Corrupted)
            {
                if (!hosted)
                    RemCompDeferred<WFTumorCorruptedComponent>(uid);
                else if (now >= corrupted.CorruptAt)
                    Finish((uid, corrupted));

                continue;
            }

            if (hosted || organ.Body == null || !TryComp<WolfmedOrganComponent>(uid, out var health) || health.Health <= FixedPoint2.Zero)
                continue;

            var loss = health.MaxHealth * corrupted.DecayPerSecond * (float) UpdateInterval.TotalSeconds;
            _organHealth.ChangeHealth((uid, health), -loss);
        }
    }

    /// <summary>Organs put into a hemophage's tumor-bearing body start to turn after a while.</summary>
    private void Spread(TimeSpan now)
    {
        var query = EntityQueryEnumerator<WFHemophageTumorComponent, OrganComponent>();
        while (query.MoveNext(out var tumorUid, out var tumor, out var tumorOrgan))
        {
            if (tumorOrgan.Body is not { } body || !TryComp<BodyComponent>(body, out var bodyComp))
                continue;

            foreach (var organ in _body.GetBodyOrgans(body, bodyComp))
            {
                if (organ.Id == tumorUid || !tumor.CorruptSlots.Contains(organ.Component.SlotId)
                    || HasComp<WFTumorCorruptedComponent>(organ.Id))
                    continue;

                var corrupted = AddComp<WFTumorCorruptedComponent>(organ.Id);
                corrupted.CorruptAt = now + tumor.CorruptionTime;
            }
        }
    }

    private void Finish(Entity<WFTumorCorruptedComponent> ent)
    {
        ent.Comp.Corrupted = true;
        ent.Comp.Sprite = HasComp<StomachComponent>(ent) ? new SpriteSpecifier.Rsi(OrganRsi, "stomach")
            : HasComp<LiverComponent>(ent) ? new SpriteSpecifier.Rsi(OrganRsi, "liver")
            : IsTongue(ent) ? new SpriteSpecifier.Rsi(OrganRsi, "tongue")
            : null;
        Dirty(ent);

        // A corrupted tongue drinks blood.
        if (IsTongue(ent) && CompOrNull<OrganComponent>(ent)?.Body is { } body)
            EnsureComp<WFHemophageDrainerComponent>(body);

        var key = HasComp<LungComponent>(ent) ? "wf-tumor-atrophied-organ" : "wf-tumor-corrupted-organ";
        _metaData.SetEntityName(ent, Loc.GetString(key, ("organ", Name(ent))));
        _metaData.SetEntityDescription(ent, Loc.GetString("wf-tumor-corrupted-organ-desc"));
    }
}
