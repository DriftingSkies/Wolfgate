using Content.Shared.Actions.Events;
using Content.Shared.Gravity;

namespace Content.Shared._WF.Modsuits.Modules;

/// <summary>Raised on the carrier to shorten a carry do-after.</summary>
[ByRefEvent]
public record struct ModifyCarryDurationEvent(EntityUid Carried, float Multiplier = 1f);

/// <summary>Raised on the cuffing user to shorten a cuff do-after.</summary>
[ByRefEvent]
public record struct ModifyCuffDurationEvent(EntityUid Target, float Duration);

/// <summary>Marks a wearer whose bulwark module blocks shoves.</summary>
[RegisterComponent]
public sealed partial class ModShoveBlockerActiveComponent : Component;

/// <summary>Wearer-side effects of modules that only modify the person inside the suit.</summary>
public sealed partial class ModWearerEffectsSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModQuickCarryComponent, ModifyCarryDurationEvent>(OnCarryDuration);
        SubscribeLocalEvent<ModQuickCuffComponent, ModifyCuffDurationEvent>(OnCuffDuration);
        SubscribeLocalEvent<ModShoveBlockerComponent, ModModuleActivatedEvent>(OnShoveBlockerActivated);
        SubscribeLocalEvent<ModShoveBlockerComponent, ModModuleDeactivatedEvent>(OnShoveBlockerDeactivated);
        SubscribeLocalEvent<ModShoveBlockerActiveComponent, DisarmAttemptEvent>(OnDisarmAttempt);
        SubscribeLocalEvent<ModAntiGravityComponent, ComponentStartup>(OnAntiGravityStartup);
        SubscribeLocalEvent<ModAntiGravityComponent, ComponentShutdown>(OnAntiGravityShutdown);
        SubscribeLocalEvent<ModAntiGravityComponent, IsWeightlessEvent>(OnIsWeightless);
    }

    [Dependency] private SharedGravitySystem _gravity = default!;
    private void OnCarryDuration(Entity<ModQuickCarryComponent> ent, ref ModifyCarryDurationEvent args)
    {
        args.Multiplier *= ent.Comp.Multiplier;
    }
    private void OnCuffDuration(Entity<ModQuickCuffComponent> ent, ref ModifyCuffDurationEvent args)
    {
        args.Duration *= ent.Comp.Multiplier;
    }
    private void OnShoveBlockerActivated(Entity<ModShoveBlockerComponent> ent, ref ModModuleActivatedEvent args)
    {
        if (args.Wearer is { } wearer)
            EnsureComp<ModShoveBlockerActiveComponent>(wearer);
    }
    private void OnShoveBlockerDeactivated(Entity<ModShoveBlockerComponent> ent, ref ModModuleDeactivatedEvent args)
    {
        if (args.Wearer is { } wearer)
            RemComp<ModShoveBlockerActiveComponent>(wearer);
    }
    private void OnDisarmAttempt(Entity<ModShoveBlockerActiveComponent> ent, ref DisarmAttemptEvent args)
    {
        if (args.DisarmerUid != ent.Owner)
            args.Cancel();
    }
    private void OnAntiGravityStartup(Entity<ModAntiGravityComponent> ent, ref ComponentStartup args)
    {
        RefreshWeightless(ent);
    }
    private void OnAntiGravityShutdown(Entity<ModAntiGravityComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent))
            RefreshWeightless(ent);
    }

    private void RefreshWeightless(EntityUid uid)
    {
        if (TryComp<GravityAffectedComponent>(uid, out var gravity))
            _gravity.RefreshWeightless((uid, gravity));
    }
    private void OnIsWeightless(Entity<ModAntiGravityComponent> ent, ref IsWeightlessEvent args)
    {
        if (ent.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        args.IsWeightless = true;
        args.Handled = true;
    }
}
