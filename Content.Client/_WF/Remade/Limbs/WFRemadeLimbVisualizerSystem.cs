using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.Client._WF.Remade.Limbs;

/// <summary>Remembers the sprite states a limbed mob was built with, so they come back after a lying state replaces them.</summary>
[RegisterComponent]
public sealed partial class WFRemadeLimbVisualsComponent : Component
{
    public Dictionary<string, string> Alive = new();
}

/// <summary>Hides the layers of severed limbs, and draws a dead mob as its remaining lying limbs or, failing that, its dead state.</summary>
public sealed class WFRemadeLimbVisualizerSystem : EntitySystem
{
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFRemadeLimbsComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<WFRemadeLimbsComponent, AppearanceChangeEvent>(OnAppearance);
        SubscribeLocalEvent<WFSeveredLimbComponent, AfterAutoHandleStateEvent>(OnSeveredState);
        SubscribeLocalEvent<WFSeveredLimbComponent, ComponentStartup>(OnSeveredStartup);
    }

    private void OnState(Entity<WFRemadeLimbsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        Refresh(ent, _mobState.IsDead(ent));
    }

    private void OnAppearance(Entity<WFRemadeLimbsComponent> ent, ref AppearanceChangeEvent args)
    {
        var dead = args.AppearanceData.TryGetValue(MobStateVisuals.State, out var state)
            ? state is MobState.Dead
            : _mobState.IsDead(ent);
        Refresh(ent, dead);
    }

    private void Refresh(Entity<WFRemadeLimbsComponent> ent, bool dead)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var visuals = EnsureComp<WFRemadeLimbVisualsComponent>(ent);
        var comp = ent.Comp;
        var entity = new Entity<SpriteComponent?>(ent.Owner, sprite);

        // First sight of the sprite: the states in the prototype are the living ones.
        if (visuals.Alive.Count == 0)
        {
            foreach (var limb in comp.Limbs)
            {
                foreach (var key in limb.LayerKeys)
                {
                    if (_sprite.LayerMapTryGet(entity, key, out var index, false))
                        visuals.Alive[key] = _sprite.LayerGetRsiState(entity, index).Name ?? string.Empty;
                }
            }
        }

        var anyLying = false;
        if (dead)
        {
            foreach (var limb in comp.Limbs)
            {
                if (limb.Lying != null && !comp.Severed.Contains(limb.Id))
                    anyLying = true;
            }
        }

        foreach (var limb in comp.Limbs)
        {
            var severed = comp.Severed.Contains(limb.Id);
            // A standing mob shows a limb it is growing back with the limb's regrowth art, on the limb's first layer.
            var growing = severed && !dead && limb.Regen != null && comp.Growing.Contains(limb.Id);
            var first = true;
            foreach (var key in limb.LayerKeys)
            {
                var lying = dead && first && limb.Lying != null;
                var visible = growing ? first : !severed && (!dead || (anyLying && lying));
                var grows = growing && first;
                first = false;

                if (!_sprite.LayerMapTryGet(entity, key, out _, false))
                    continue;

                _sprite.LayerSetVisible(entity, key, visible);
                if (!visible)
                    continue;

                var state = grows ? limb.Regen : lying ? limb.Lying : visuals.Alive.GetValueOrDefault(key);
                if (!string.IsNullOrEmpty(state))
                    _sprite.LayerSetRsiState(entity, key, state);
            }
        }

        if (_sprite.LayerMapTryGet(entity, comp.DeadLayer, out _, false))
            _sprite.LayerSetVisible(entity, comp.DeadLayer, dead && !anyLying);
    }

    private void OnSeveredStartup(Entity<WFSeveredLimbComponent> ent, ref ComponentStartup args)
    {
        DrawSevered(ent);
    }

    private void OnSeveredState(Entity<WFSeveredLimbComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        DrawSevered(ent);
    }

    private void DrawSevered(Entity<WFSeveredLimbComponent> ent)
    {
        if (string.IsNullOrEmpty(ent.Comp.State) || !TryComp<SpriteComponent>(ent, out var sprite))
            return;

        _sprite.LayerSetRsi((ent.Owner, sprite), 0, ent.Comp.Rsi, ent.Comp.State);
    }
}
