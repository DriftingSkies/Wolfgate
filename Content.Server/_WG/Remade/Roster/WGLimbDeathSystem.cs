using Content.Server._WG.Remade.Elites;
using Content.Server._WG.Remade.Limbs;
using Content.Shared._WG.Remade.Limbs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;

namespace Content.Server._WG.Remade.Roster;

/// <summary>Kills a Remade that has lost every one of the limbs it can't live without.</summary>
public sealed partial class WGLimbDeathSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WGEliteSystem _elite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGDiesWithoutLimbsComponent, WGRemadeLimbSeveredEvent>(OnSevered);
    }

    private void OnSevered(Entity<WGDiesWithoutLimbsComponent> ent, ref WGRemadeLimbSeveredEvent args)
    {
        if (_mobState.IsDead(ent) || !TryComp<WGRemadeLimbsComponent>(ent, out var limbs))
            return;

        foreach (var id in ent.Comp.Limbs)
        {
            if (!limbs.Severed.Contains(id))
                return;
        }

        _popup.PopupEntity(Loc.GetString("wg-remade-limb-death", ("mob", ent.Owner)), ent, PopupType.MediumCaution);
        _elite.Fell(ent);
    }
}
