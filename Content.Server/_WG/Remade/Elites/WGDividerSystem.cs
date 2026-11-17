using System.Numerics;
using Content.Server._WG.Remade.Conversion;
using Content.Server._WG.Remade.Director;
using Content.Server._WG.Remade.Limbs;
using Content.Shared._WG.Remade.Limbs;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;

namespace Content.Server._WG.Remade.Elites;

/// <summary>Breaks a divider into crawling parts as its limbs come off, and all at once when it dies.</summary>
public sealed partial class WGDividerSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WGEliteSystem _elite = default!;
    [Dependency] private WGRemadeDirectorSystem _director = default!;
    [Dependency] private WGRemadeLimbsSystem _limbs = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGDividerComponent, WGRemadeLimbSeveredEvent>(OnSevered);
        SubscribeLocalEvent<WGDividerComponent, MobStateChangedEvent>(OnMobState);
    }

    private void OnSevered(Entity<WGDividerComponent> ent, ref WGRemadeLimbSeveredEvent args)
    {
        Release(ent, args.Limb);
    }

    /// <summary>On death the rest of it comes apart.</summary>
    private void OnMobState(Entity<WGDividerComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || !TryComp<WGRemadeLimbsComponent>(ent, out var limbs))
            return;

        _audio.PlayPvs(ent.Comp.SplitSound, ent);
        _popup.PopupEntity(Loc.GetString("wg-remade-elite-divides", ("mob", ent.Owner)), ent, PopupType.LargeCaution);
        foreach (var limb in limbs.Limbs.ToArray())
        {
            if (limb.Severable && !limbs.Severed.Contains(limb.Id))
                _limbs.TrySever(ent, limb.Id, null, limbs);
        }
    }

    /// <summary>Spawns the part for a limb that has come off and flings it from the body.</summary>
    private void Release(Entity<WGDividerComponent> ent, string limb)
    {
        if (!ent.Comp.Parts.TryGetValue(limb, out var proto))
            return;

        var angle = _random.NextAngle();
        var part = Spawn(proto, _transform.GetMoverCoordinates(ent).Offset(angle.ToVec() * 0.4f));
        ent.Comp.Released.Add(part);

        var source = CompOrNull<WGRemadeComponent>(ent)?.Source;
        _director.Enlist(part, source);
        _elite.Throw(part, angle.ToVec(), ent.Comp.Fling + _random.NextFloat(0f, 1f), 6f);
    }
}
