using System.Numerics;
using Content.Client._Scp.Shaders.Bloom;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Graphics;
using Robust.Shared.Map.Components;

namespace Content.Client._WG.WallmountFov;

/// <summary>
/// Hides <see cref="WGWallBackfaceVisualsComponent"/> sprites from an eye behind the wall they are drawn on. Hard FOV
/// shows wall tiles from both sides, so a lamp on a wall's far face would otherwise glow through it.
/// </summary>
public sealed partial class WGWallBackfaceSystem : EntitySystem
{
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private ILightManager _light = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Distance from a mount's tile centre to the wall face it hangs on.</summary>
    public const float FaceDistance = 0.5f;

    /// <summary>Sprites this system hid. It only ever shows these again, never one another system hid.</summary>
    private readonly HashSet<EntityUid> _hidden = new();

    private EntityQuery<MapComponent> _mapQuery;

    public override void Initialize()
    {
        base.Initialize();

        // Bloom reads IsHidden in the same frame.
        UpdatesBefore.Add(typeof(LightingOverlaySystem));

        _mapQuery = GetEntityQuery<MapComponent>();

        SubscribeLocalEvent<WGWallBackfaceVisualsComponent, ComponentShutdown>(OnShutdown);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        ShowAll();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var eye = _eye.CurrentEye;
        if (!DrawsHardFov(eye))
        {
            ShowAll();
            return;
        }

        var eyeMap = eye.Position.MapId;
        var eyePos = eye.Position.Position;

        var query = EntityQueryEnumerator<WGWallBackfaceVisualsComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var sprite, out var xform))
        {
            var hide = false;

            // A sprite that doesn't turn with its mount has no fixed wall to face.
            if (xform.MapID == eyeMap && !sprite.NoRotation && !sprite.SnapCardinals)
            {
                var (pos, rot) = _transform.GetWorldPositionRotation(xform);
                hide = IsBehindMount(pos, rot, sprite.Offset, eyePos);
            }

            if (hide)
            {
                if (sprite.Visible)
                {
                    _sprite.SetVisible((uid, sprite), false);
                    _hidden.Add(uid);
                }
            }
            else if (_hidden.Remove(uid))
            {
                _sprite.SetVisible((uid, sprite), true);
            }
        }
    }

    /// <summary>Whether this system is keeping a sprite hidden behind its wall.</summary>
    public bool IsHidden(EntityUid uid)
    {
        return _hidden.Contains(uid);
    }

    /// <summary>
    /// Whether an eye is behind the wall a sprite is drawn on: past the face half a tile out from the mount, in the
    /// direction of the sprite's offset turned by the mount's world rotation. A sprite offset no further than that
    /// face is drawn on the mount's own tile, which FOV already handles.
    /// </summary>
    public static bool IsBehindMount(Vector2 mountPos, Angle mountRotation, Vector2 spriteOffset, Vector2 eyePos)
    {
        var length = spriteOffset.Length();
        if (length <= FaceDistance)
            return false;

        var wallDir = mountRotation.RotateVec(spriteOffset) / length;
        return Vector2.Dot(eyePos - mountPos, wallDir) > FaceDistance;
    }

    /// <summary>Whether Clyde applies hard FOV to this eye's view, by the same test it uses.</summary>
    private bool DrawsHardFov(IEye eye)
    {
        if (!_light.Enabled || !_light.DrawHardFov || !eye.DrawLight || !eye.DrawFov)
            return false;

        return _map.TryGetMap(eye.Position.MapId, out var mapUid)
            && _mapQuery.TryComp(mapUid.Value, out var map)
            && map.LightingEnabled;
    }

    private void ShowAll()
    {
        if (_hidden.Count == 0)
            return;

        foreach (var uid in _hidden)
        {
            if (TryComp<SpriteComponent>(uid, out var sprite))
                _sprite.SetVisible((uid, sprite), true);
        }

        _hidden.Clear();
    }

    private void OnShutdown(Entity<WGWallBackfaceVisualsComponent> ent, ref ComponentShutdown args)
    {
        if (!_hidden.Remove(ent.Owner) || TerminatingOrDeleted(ent.Owner))
            return;

        if (TryComp<SpriteComponent>(ent.Owner, out var sprite))
            _sprite.SetVisible((ent.Owner, sprite), true);
    }
}
