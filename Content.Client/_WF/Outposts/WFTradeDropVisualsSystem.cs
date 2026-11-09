using System.Numerics;
using Content.Shared._WF.Outposts;
using Robust.Client.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._WF.Outposts;

/// <summary>Draws a trade delivery sinking onto its drop tile under a swaying canopy, over a shadow that grows as it nears.</summary>
public sealed partial class WFTradeDropVisualsSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private static readonly SpriteSpecifier Canopy =
        new SpriteSpecifier.Rsi(new ResPath("_WF/Planets/Effects/parachute_canopy.rsi"), "canopy");

    private static readonly SpriteSpecifier Shadow =
        new SpriteSpecifier.Rsi(new ResPath("_WF/Outposts/Effects/drop_shadow.rsi"), "shadow");

    /// <summary>How far the load swings side to side under the canopy, in tiles, and how fast.</summary>
    private const float SwayAmplitude = 0.12f;
    private const float SwaySpeed = 1.7f;

    private readonly List<Entity<WFTradeDropComponent>> _undressed = new();

    /// <summary>Dresses a descent as its product, under a canopy and over a shadow.</summary>
    private void Dress(Entity<WFTradeDropComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite) || ent.Comp.Product is not { } product || !_proto.HasIndex(product))
            return;

        // Look the same as the product by borrowing its sprite from a throwaway client copy.
        var copy = Spawn(product, MapCoordinates.Nullspace);
        if (TryComp<SpriteComponent>(copy, out var source))
            _sprite.CopySprite((copy, source), (ent, sprite));
        Del(copy);

        _sprite.SetDrawDepth((ent, sprite), (int) DrawDepth.Effects);
        sprite.NoRotation = true;

        var top = _sprite.GetLocalBounds((ent, sprite)).Height / 2f;
        var canopy = _sprite.AddLayer((ent, sprite), Canopy);
        _sprite.LayerMapSet((ent, sprite), DropKey.Canopy, canopy);
        _sprite.LayerSetOffset((ent, sprite), canopy, new Vector2(0f, top + 0.5f));

        var shadow = _sprite.AddLayer((ent, sprite), Shadow, 0);
        _sprite.LayerMapSet((ent, sprite), DropKey.Shadow, shadow);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var now = _timing.CurTime;
        _undressed.Clear();
        var query = EntityQueryEnumerator<WFTradeDropComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var drop, out var sprite))
        {
            if (!_sprite.LayerMapTryGet((uid, sprite), DropKey.Shadow, out var shadow, false))
            {
                _undressed.Add((uid, drop));
                continue;
            }

            var left = 1f - Math.Clamp((float) ((now - drop.Start) / drop.Duration), 0f, 1f);
            var height = drop.Height * left;
            var sway = MathF.Sin((float) now.TotalSeconds * SwaySpeed) * SwayAmplitude * left;

            _sprite.SetOffset((uid, sprite), new Vector2(sway, height));

            // The shadow stays on the ground, sharpening and filling out as the load comes down.
            _sprite.LayerSetOffset((uid, sprite), shadow, new Vector2(-sway, -height));
            _sprite.LayerSetScale((uid, sprite), shadow, new Vector2(1f - 0.6f * left));
            _sprite.LayerSetColor((uid, sprite), shadow, Color.Black.WithAlpha(0.45f - 0.3f * left));
        }

        // Outside the query: borrowing a sprite spawns an entity.
        foreach (var ent in _undressed)
        {
            Dress(ent);
        }
    }

    private enum DropKey
    {
        Canopy,
        Shadow,
    }
}
