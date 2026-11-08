using System.Numerics;
using Content.Shared._WF.Fauna;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._WF.Fauna;

/// <summary>
/// Plays the client look of fauna charges and blinks: the wind-up lean, the charge afterimage and the blink fade.
/// </summary>
public sealed partial class FaunaChargeLookSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly TimeSpan LeanTime = TimeSpan.FromSeconds(0.3);
    private static readonly TimeSpan ReturnTime = TimeSpan.FromSeconds(0.15);
    private static readonly TimeSpan AfterimageTime = TimeSpan.FromSeconds(0.3);
    private static readonly TimeSpan FadeTime = TimeSpan.FromSeconds(0.2);

    private readonly List<(EntityUid Ghost, TimeSpan Start, Color Color, bool Grow)> _ghosts = new();
    private readonly Dictionary<EntityUid, TimeSpan> _fadeIns = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaChargeLookComponent, AppearanceChangeEvent>(OnAppearanceChange);
        SubscribeNetworkEvent<FaunaBlinkFadeEvent>(OnBlinkFade);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        foreach (var (ghost, _, _, _) in _ghosts)
        {
            QueueDel(ghost);
        }

        _ghosts.Clear();
        _fadeIns.Clear();
    }

    private void OnAppearanceChange(Entity<FaunaChargeLookComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (ent.Comp.Lean > 0 &&
            _appearance.TryGetData<Vector2>(ent, FaunaChargeVisuals.Windup, out var away, args.Component))
        {
            var lean = EnsureComp<FaunaChargeLeanComponent>(ent);
            var leaning = away != Vector2.Zero;
            if (leaning != lean.Leaning)
            {
                if (leaning && !lean.Active)
                    lean.BaseOffset = args.Sprite.Offset;

                lean.Active = true;
                lean.Leaning = leaning;
                lean.Direction = leaning ? away : lean.Direction;
                lean.Start = _timing.CurTime;
            }
        }

        if (ent.Comp.Afterimage is { } colour &&
            _appearance.TryGetData<bool>(ent, FaunaChargeVisuals.Charging, out var charging, args.Component) &&
            charging)
            SpawnGhost(ent, Transform(ent).Coordinates, colour, true);
    }

    private void OnBlinkFade(FaunaBlinkFadeEvent ev)
    {
        var mob = GetEntity(ev.Mob);
        if (!Exists(mob) || !TryComp<SpriteComponent>(mob, out var sprite))
            return;

        SpawnGhost(mob, GetCoordinates(ev.From), sprite.Color, false);
        _fadeIns[mob] = _timing.CurTime;
        _sprite.SetColor((mob, sprite), sprite.Color.WithAlpha(0f));
    }

    /// <summary>
    /// Leaves a fading copy of the mob's sprite. A charge ghost also grows to twice its size.
    /// </summary>
    private void SpawnGhost(EntityUid mob, EntityCoordinates coords, Color colour, bool grow)
    {
        if (!TryComp<SpriteComponent>(mob, out var source))
            return;

        var ghost = Spawn(null, coords);
        _transform.SetWorldRotation(ghost, _transform.GetWorldRotation(mob));
        var sprite = AddComp<SpriteComponent>(ghost);
        _sprite.CopySprite((mob, source), (ghost, sprite));
        _sprite.SetColor((ghost, sprite), colour.WithAlpha(grow ? 0.6f : 1f));
        _ghosts.Add((ghost, _timing.CurTime, colour, grow));
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var now = _timing.CurTime;
        UpdateGhosts(now);
        UpdateFadeIns(now);

        var query = EntityQueryEnumerator<FaunaChargeLeanComponent, FaunaChargeLookComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var lean, out var look, out var sprite))
        {
            if (!lean.Active)
                continue;

            float amount;
            if (lean.Leaning)
            {
                amount = Ease((float) ((now - lean.Start) / LeanTime));
            }
            else
            {
                var back = (float) ((now - lean.Start) / ReturnTime);
                if (back >= 1f)
                {
                    lean.Active = false;
                    _sprite.SetOffset((uid, sprite), lean.BaseOffset);
                    RemCompDeferred<FaunaChargeLeanComponent>(uid);
                    continue;
                }

                amount = 1f - Ease(back);
            }

            var world = lean.Direction * look.Lean * amount;
            _sprite.SetOffset((uid, sprite), lean.BaseOffset + ToLocal(uid, sprite, world));
        }
    }

    private void UpdateGhosts(TimeSpan now)
    {
        for (var i = _ghosts.Count - 1; i >= 0; i--)
        {
            var (ghost, start, colour, grow) = _ghosts[i];
            var progress = (float) ((now - start) / (grow ? AfterimageTime : FadeTime));
            if (progress >= 1f || !TryComp<SpriteComponent>(ghost, out var sprite))
            {
                QueueDel(ghost);
                _ghosts.RemoveAt(i);
                continue;
            }

            Entity<SpriteComponent?> ent = (ghost, sprite);
            _sprite.SetColor(ent, colour.WithAlpha((grow ? 0.6f : 1f) * (1f - progress)));
            if (grow)
                _sprite.SetScale(ent, Vector2.One * (1f + progress));
        }
    }

    private void UpdateFadeIns(TimeSpan now)
    {
        foreach (var (mob, start) in _fadeIns)
        {
            var progress = Math.Clamp((float) ((now - start) / FadeTime), 0f, 1f);
            if (TryComp<SpriteComponent>(mob, out var sprite))
                _sprite.SetColor((mob, sprite), sprite.Color.WithAlpha(progress));

            if (progress >= 1f || sprite == null)
                _fadeIns.Remove(mob);
        }
    }

    /// <summary>
    /// Turns a world-space offset into the sprite's own space, which rotates with the entity unless it is no-rot.
    /// </summary>
    private Vector2 ToLocal(EntityUid uid, SpriteComponent sprite, Vector2 world)
    {
        if (sprite.NoRotation)
            return _eye.CurrentEye.Rotation.RotateVec(world);

        var rotation = _transform.GetWorldRotation(uid);
        if (sprite.SnapCardinals)
            rotation -= rotation.GetCardinalDir().ToAngle();

        return (-rotation).RotateVec(world);
    }

    private static float Ease(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return 1f - (1f - t) * (1f - t);
    }
}
