using System.Numerics;
using Content.Shared._WG.AdminObserver;
using Content.Shared.StatusIcon.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WG.AdminObserver;

/// <summary>
/// Draws every staff marker visual but the nameplate: the sigil as a status icon, the hologram and aura outline as
/// passes over the sprite, the aura light as a light of this client's own, and the ring, shield and glyph through
/// <see cref="WGStaffMarkerOverlay"/>. Nothing is kept on the sprite's layers, so swapping the avatar's sprite
/// costs the marker nothing.
/// </summary>
public sealed partial class WGStaffMarkerVisualsSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedPointLightSystem _lights = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public const string HologramPass = "wg-staff-hologram";
    public const string OutlinePass = "wg-staff-outline";

    private static readonly ProtoId<ShaderPrototype> HologramShader = "Hologram";
    private static readonly ProtoId<ShaderPrototype> OutlineShader = "ShieldingOutline";
    private static readonly ProtoId<ShaderPrototype> SkinShader = "PersonalShieldSkin";
    private static readonly EntProtoId AuraLight = "WGStaffMarkerAuraLight";

    /// <summary>How much darker the hologram's second band is than its first.</summary>
    private const float HologramBand = 0.57f;

    private const float HologramScroll = 0.125f;

    /// <summary>The outline's width in screen pixels at a render scale of one.</summary>
    private const float OutlineWidth = 1.5f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGStaffMarkerComponent, GetStatusIconsEvent>(OnGetStatusIcons);
        SubscribeLocalEvent<WGStaffMarkerComponent, ComponentShutdown>(OnMarkerShutdown);
        SubscribeLocalEvent<WGStaffMarkerVisualsComponent, ComponentShutdown>(OnVisualsShutdown);
        SubscribeLocalEvent<WGStaffMarkerVisualsComponent, BeforePostShaderRenderEvent>(OnBeforeRender);
        _overlays.AddOverlay(new WGStaffMarkerOverlay(EntityManager, _prototypes, _timing, _sprite, _transform));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<WGStaffMarkerOverlay>();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<WGStaffMarkerComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var marker, out var sprite))
        {
            var visuals = EnsureComp<WGStaffMarkerVisualsComponent>(uid);
            UpdateHologram(uid, marker, sprite, visuals);
            UpdateAura(uid, marker, sprite, visuals);
            UpdateSkin(marker, visuals);
        }
    }

    /// <summary>The look a marker's visual takes, or null if the visual is off or the look is unknown here.</summary>
    public WGStaffMarkerVariantPrototype? Variant(WGStaffMarkerComponent marker, WGMarkerVisuals visual)
    {
        if ((marker.Visuals & visual) == 0)
            return null;

        var id = visual switch
        {
            WGMarkerVisuals.Ring => marker.Ring,
            WGMarkerVisuals.Glyph => marker.Glyph,
            WGMarkerVisuals.Shield => marker.Shield,
            WGMarkerVisuals.Aura => marker.Aura,
            _ => string.Empty,
        };

        return _prototypes.TryIndex<WGStaffMarkerVariantPrototype>(id, out var variant) ? variant : null;
    }

    private void OnGetStatusIcons(Entity<WGStaffMarkerComponent> ent, ref GetStatusIconsEvent args)
    {
        if ((ent.Comp.Visuals & WGMarkerVisuals.Sigil) == 0)
            return;

        if (_prototypes.TryIndex(ent.Comp.Host ? ent.Comp.HostSigil : ent.Comp.AdminSigil, out var icon))
            args.StatusIcons.Add(icon);
    }

    private void OnMarkerShutdown(Entity<WGStaffMarkerComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent))
            RemComp<WGStaffMarkerVisualsComponent>(ent);
    }

    private void OnVisualsShutdown(Entity<WGStaffMarkerVisualsComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(ent, out var sprite))
        {
            _sprite.RemovePostShader((ent, sprite), HologramPass);
            _sprite.RemovePostShader((ent, sprite), OutlinePass);
        }

        ent.Comp.Hologram?.Dispose();
        ent.Comp.Outline?.Dispose();
        ent.Comp.Skin?.Dispose();
        ent.Comp.Hologram = null;
        ent.Comp.Outline = null;
        ent.Comp.Skin = null;

        if (ent.Comp.Light is { } light && !TerminatingOrDeleted(light))
            QueueDel(light);

        ent.Comp.Light = null;
    }

    private void OnBeforeRender(Entity<WGStaffMarkerVisualsComponent> ent, ref BeforePostShaderRenderEvent args)
    {
        // The pass works in the viewport's pixels, so a sharper viewport needs a wider outline to look the same.
        if (args.Id == OutlinePass)
            args.Shader.SetParameter("outline_width", OutlineWidth * MathF.Max(args.Viewport.RenderScale.X, 1f));
    }

    private void UpdateHologram(EntityUid uid, WGStaffMarkerComponent marker, SpriteComponent sprite, WGStaffMarkerVisualsComponent visuals)
    {
        if ((marker.Visuals & WGMarkerVisuals.Hologram) == 0)
        {
            Drop((uid, sprite), HologramPass, ref visuals.Hologram);
            return;
        }

        var height = 0f;
        foreach (var layer in sprite.AllLayers)
        {
            height = MathF.Max(height, layer.PixelSize.Y);
        }

        if (height <= 0f)
            return;

        var shader = visuals.Hologram ??= _prototypes.Index(HologramShader).InstanceUnique();
        var color = marker.Color;
        shader.SetParameter("color1", new Vector3(color.R, color.G, color.B));
        shader.SetParameter("color2", new Vector3(color.R, color.G, color.B) * HologramBand);
        shader.SetParameter("alpha", 0.9f);
        shader.SetParameter("intensity", 2f);
        shader.SetParameter("texHeight", height);
        // Whole bands only, so the scroll does not jump when the clock wraps.
        shader.SetParameter("t", (float) (_timing.RealTime.TotalSeconds % 64d) * HologramScroll);

        if (!HasPass(sprite, HologramPass))
            _sprite.SetPostShader((uid, sprite), new SpriteComponent.PostShaderArgs(HologramPass, shader));
    }

    private void UpdateAura(EntityUid uid, WGStaffMarkerComponent marker, SpriteComponent sprite, WGStaffMarkerVisualsComponent visuals)
    {
        var kind = Variant(marker, WGMarkerVisuals.Aura)?.Kind;

        if (kind == WGMarkerVariantKind.Shader)
        {
            var shader = visuals.Outline ??= _prototypes.Index(OutlineShader).InstanceUnique();
            shader.SetParameter("outline_color", marker.Color.WithAlpha(0.85f));
            shader.SetParameter("outline_fullbright", true);

            if (!HasPass(sprite, OutlinePass))
            {
                _sprite.SetPostShader((uid, sprite), new SpriteComponent.PostShaderArgs(OutlinePass, shader)
                {
                    RaiseShaderEvent = true,
                    After = new[] { HologramPass },
                });
            }
        }
        else
        {
            Drop((uid, sprite), OutlinePass, ref visuals.Outline);
        }

        if (kind == WGMarkerVariantKind.Light)
        {
            if (visuals.Light is not { } light || TerminatingOrDeleted(light))
            {
                light = SpawnAttachedTo(AuraLight, new EntityCoordinates(uid, Vector2.Zero));
                visuals.Light = light;
            }

            _lights.SetColor(light, marker.Color);
        }
        else if (visuals.Light is { } light)
        {
            if (!TerminatingOrDeleted(light))
                QueueDel(light);

            visuals.Light = null;
        }
    }

    private void UpdateSkin(WGStaffMarkerComponent marker, WGStaffMarkerVisualsComponent visuals)
    {
        if (Variant(marker, WGMarkerVisuals.Shield)?.Kind != WGMarkerVariantKind.Shader)
        {
            visuals.Skin?.Dispose();
            visuals.Skin = null;
            return;
        }

        if (visuals.Skin != null)
        {
            visuals.Skin.SetParameter("skin_color", marker.Color.WithAlpha(0.95f));
            return;
        }

        // The personal shield's own look, fully formed and never struck.
        var shader = visuals.Skin = _prototypes.Index(SkinShader).InstanceUnique();
        shader.SetParameter("progress", 1f);
        shader.SetParameter("skin_color", marker.Color.WithAlpha(0.95f));
        shader.SetParameter("brightness", 1f);
        shader.SetParameter("pixel_grid", 96f);
        shader.SetParameter("hex_density", 4f);
        shader.SetParameter("form_origin", Vector2.Zero);
        shader.SetParameter("fill_level", 0.08f);
        shader.SetParameter("line_level", 0.5f);
        shader.SetParameter("rim_level", 0.75f);
        shader.SetParameter("core_fade", 0.85f);
        shader.SetParameter("shard_scale", 5f);
        shader.SetParameter("alpha_bands", 6f);
        shader.SetParameter("breath_depth", 0.08f);
        shader.SetParameter("damage_flare", 0f);
    }

    private void Drop(Entity<SpriteComponent> sprite, string pass, ref ShaderInstance? shader)
    {
        if (shader == null)
            return;

        _sprite.RemovePostShader((sprite.Owner, sprite.Comp), pass);
        shader.Dispose();
        shader = null;
    }

    private bool HasPass(SpriteComponent sprite, string pass)
    {
        foreach (var entry in _sprite.GetPostShaders(sprite))
        {
            if (entry.Id == pass)
                return true;
        }

        return false;
    }
}
