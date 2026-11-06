using Content.Shared._WF.Fauna;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Robust.Shared.Audio.Systems;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Soaks damage into <see cref="FaunaBreakableShellComponent"/> shells and breaks them when they are spent.
/// </summary>
public sealed partial class FaunaBreakableShellSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaBreakableShellComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<FaunaBreakableShellComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<FaunaBreakableShellComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMapInit(Entity<FaunaBreakableShellComponent> ent, ref MapInitEvent args)
    {
        UpdateStage(ent);
    }

    private void OnDamageModify(Entity<FaunaBreakableShellComponent> ent, ref DamageModifyEvent args)
    {
        if (ent.Comp.Broken || _mobState.IsDead(ent))
            return;

        var total = 0f;
        foreach (var value in args.Damage.DamageDict.Values)
        {
            if (value > 0)
                total += value.Float();
        }

        if (total <= 0)
            return;

        var projectile = HasComp<ProjectileComponent>(args.Tool);
        var factor = projectile ? ent.Comp.ProjectileFactor : 1f;
        var wasCracked = ent.Comp.Absorbed >= ent.Comp.MaxHealth / 2;
        ent.Comp.Absorbed += total * factor;
        args.Damage = new DamageSpecifier();

        if (projectile && args.Tool is { } tool)
        {
            _popup.PopupEntity(Loc.GetString("fauna-breakable-shell-absorb", ("projectile", tool), ("user", ent.Owner)),
                ent);
        }

        if (!wasCracked && ent.Comp.Absorbed >= ent.Comp.MaxHealth / 2 && ent.Comp.Absorbed < ent.Comp.MaxHealth)
            _popup.PopupEntity(Loc.GetString("fauna-breakable-shell-crack", ("user", ent.Owner)), ent);

        if (ent.Comp.Absorbed >= ent.Comp.MaxHealth)
        {
            Break(ent);
            _audio.PlayPvs(ent.Comp.BreakSound, ent);
            _popup.PopupEntity(Loc.GetString("fauna-breakable-shell-break", ("user", ent.Owner)), ent,
                PopupType.MediumCaution);
        }

        UpdateStage(ent);
    }

    private void OnMobStateChanged(Entity<FaunaBreakableShellComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead && !ent.Comp.Broken)
            Break(ent);

        UpdateStage(ent);
    }

    private void Break(Entity<FaunaBreakableShellComponent> ent)
    {
        ent.Comp.Broken = true;
        var coords = Transform(ent).Coordinates;
        foreach (var loot in ent.Comp.Loot)
        {
            Spawn(loot, coords);
        }
    }

    private void UpdateStage(Entity<FaunaBreakableShellComponent> ent)
    {
        var stage = _mobState.IsDead(ent) ? FaunaBreakableShellStage.Dead
            : ent.Comp.Broken ? FaunaBreakableShellStage.Broken
            : ent.Comp.Absorbed >= ent.Comp.MaxHealth / 2 ? FaunaBreakableShellStage.Cracked
            : FaunaBreakableShellStage.Whole;
        _appearance.SetData(ent, FaunaBreakableShellVisuals.Stage, stage);
    }
}
