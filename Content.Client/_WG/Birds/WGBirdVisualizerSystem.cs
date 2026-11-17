using System.Numerics;
using Content.Shared._WG.Birds;
using Content.Shared.Mobs;
using Robust.Client.GameObjects;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._WG.Birds;

/// <summary>Swaps a bird between its ground, flight and dead sprites, lifts it over its shadow and draws it above walls in flight.</summary>
public sealed class WGBirdVisualizerSystem : VisualizerSystem<WGBirdComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, WGBirdComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite is not { } sprite)
            return;

        AppearanceSystem.TryGetData<bool>(uid, WGBirdVisuals.Flying, out var flying, args.Component);
        var dead = AppearanceSystem.TryGetData<MobState>(uid, MobStateVisuals.State, out var state, args.Component) &&
                   state != MobState.Alive;
        flying &= !dead;

        Entity<SpriteComponent?> ent = (uid, sprite);
        if (SpriteSystem.LayerMapTryGet(ent, WGBirdLayers.Body, out var body, false))
        {
            SpriteSystem.LayerSetRsiState(ent, body, dead ? component.DeadState : flying ? component.FlyState : component.GroundState);
            SpriteSystem.LayerSetOffset(ent, body, flying ? new Vector2(0f, component.FlightLift) : Vector2.Zero);
        }

        if (SpriteSystem.LayerMapTryGet(ent, WGBirdLayers.Shadow, out var shadow, false))
            SpriteSystem.LayerSetVisible(ent, shadow, flying);

        SpriteSystem.SetDrawDepth(ent, (int) (dead ? DrawDepth.DeadMobs : flying ? DrawDepth.Overdoors : DrawDepth.SmallMobs));
    }
}
