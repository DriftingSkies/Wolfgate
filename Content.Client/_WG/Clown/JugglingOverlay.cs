using System;
using System.Numerics;
using Content.Shared._WG.Clown;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._WG.Clown;

/// <summary>Draws each juggling clown's items arcing above their head.</summary>
public sealed class JugglingOverlay : Overlay
{
    private readonly IEntityManager _entities;
    private SharedTransformSystem _transform => _entities.System<SharedTransformSystem>();
    private readonly IGameTiming _timing;
    private readonly SpriteSystem _sprites;

    /// <summary>Drawn below the lighting pass so the items darken with the room.</summary>
    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    private readonly EntityQuery<MetaDataComponent> _metaQuery;
    private readonly EntityQuery<SpriteComponent> _spriteQuery;

    private const float Cycle = 1.4f;
    private const float ItemScale = 0.65f;

    /// <summary>Takes the managers and systems the draw needs.</summary>
    public JugglingOverlay(IEntityManager entities, IGameTiming timing, SpriteSystem sprites)
    {
        _entities = entities;
        _timing = timing;
        _sprites = sprites;
        _metaQuery = entities.GetEntityQuery<MetaDataComponent>();
        _spriteQuery = entities.GetEntityQuery<SpriteComponent>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var curTime = (float)_timing.CurTime.TotalSeconds;
        var eyeRot = args.Viewport.Eye?.Rotation ?? Angle.Zero;
        var screenUp = -eyeRot;

        var enumerator = _entities.EntityQueryEnumerator<JugglingActiveComponent, TransformComponent, MetaDataComponent>();
        while (enumerator.MoveNext(out _, out var active, out var xform, out var clownMeta))
        {
            if (xform.MapID != args.MapId || active.JuggledItems.Count == 0)
                continue;

            // A clown inside a locker or vehicle can't be seen, so neither can the items.
            if ((clownMeta.Flags & MetaDataFlags.InContainer) != 0)
                continue;

            var worldPos = _transform.GetWorldPosition(xform);
            var startTime = (float)active.StartTime.TotalSeconds;
            var elapsed = curTime - startTime;
            var n = active.JuggledItems.Count;

            for (var i = 0; i < n; i++)
            {
                var phase = i * (Cycle / n);
                var itemPos = worldPos + screenUp.RotateVec(ComputeItemOffset(elapsed, phase, Cycle));

                if (!_entities.TryGetEntity(active.JuggledItems[i], out var itemEnt))
                    continue;

                var spinDir = (i % 2 == 0) ? 1.0 : -1.0;
                var spin = new Angle(elapsed * 1.5 * spinDir + i * 2.3);

                if (_spriteQuery.TryGetComponent(itemEnt.Value, out var sprite))
                {
                    var scale = sprite.Scale;
                    _sprites.SetScale((itemEnt.Value, sprite), scale * ItemScale);
                    _sprites.RenderSprite((itemEnt.Value, sprite), handle, eyeRot, screenUp + spin, itemPos);
                    _sprites.SetScale((itemEnt.Value, sprite), scale);
                    continue;
                }

                if (!_metaQuery.TryGetComponent(itemEnt.Value, out var meta) || meta.EntityPrototype == null)
                    continue;

                var texture = _sprites.Frame0(meta.EntityPrototype);
                var halfSize = texture.Size / (2f * EyeManager.PixelsPerMeter) * ItemScale;

                var box = Box2.CenteredAround(itemPos, halfSize * 2);
                handle.DrawTextureRect(texture, new Box2Rotated(box, spin, itemPos));
            }
        }
    }

    /// <summary>Each item rises in a tall arc from one hand to the other, then makes a quick low pass back.</summary>
    private static Vector2 ComputeItemOffset(float elapsed, float phase, float cycle)
    {
        var tNorm = ((elapsed + phase) % cycle) / cycle;
        if (tNorm < 0f) tNorm += 1f;

        const float left = -0.35f;
        const float right = 0.35f;

        float x, y;
        if (tNorm < 0.55f)
        {
            var u = tNorm / 0.55f;
            x = MathHelper.Lerp(right, left, u);
            y = 0.9f * MathF.Sin(MathF.PI * u);
        }
        else
        {
            var u = (tNorm - 0.55f) / 0.45f;
            x = MathHelper.Lerp(left, right, u);
            y = 0.18f * MathF.Sin(MathF.PI * u);
        }

        return new Vector2(x, y);
    }
}

/// <summary>Toggles the walk state when juggling starts and stops, so the walk icon and speed follow.</summary>
public sealed partial class JugglingVisualsSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SpriteSystem _sprites = default!;
    [Dependency] private SharedMoverController _mover = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new JugglingOverlay(EntityManager, _timing, _sprites));

        SubscribeLocalEvent<JugglingActiveComponent, ComponentStartup>(OnActiveStartup);
        SubscribeLocalEvent<JugglingActiveComponent, ComponentShutdown>(OnActiveShutdown);

        CommandBinds.Builder
            .BindBefore(EngineKeyFunctions.Walk, new JuggleWalkBlocker(), typeof(SharedMoverController))
            .Register<JugglingVisualsSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<JugglingOverlay>();
        CommandBinds.Unregister<JugglingVisualsSystem>();
    }

    private void OnActiveStartup(Entity<JugglingActiveComponent> ent, ref ComponentStartup args)
        => DeferWalk(ent.Owner, true);

    private void OnActiveShutdown(Entity<JugglingActiveComponent> ent, ref ComponentShutdown args)
        => DeferWalk(ent.Owner, false);

    /// <summary>Applies the walk state once juggling has fully started or stopped.</summary>
    private void DeferWalk(EntityUid owner, bool walking)
        => Timer.Spawn(0, () => ApplyWalk(owner, walking));

    private void ApplyWalk(EntityUid uid, bool walking)
    {
        if (uid != _player.LocalEntity || Deleted(uid))
            return;
        if (!TryComp<InputMoverComponent>(uid, out var mover))
            return;

        _mover.SetSprinting((uid, mover), 0, walking);
    }
}
