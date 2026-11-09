using System.Numerics;
using Content.Client._White.Overlays;
using Content.Client.Construction;
using Content.Client.Interactable.Components;
using Content.Client.Interaction;
using Content.Client.Machines.Components;
using Content.Client.Outline;
using Content.Shared._WF.Photography;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;

namespace Content.Client._WF.Photography;

/// <summary>
/// Takes UI out of the world for one photo render: highlights, previews, HUDs, vision effects and cloaked entities.
/// </summary>
public sealed partial class PhotoCaptureFilterSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private DragDropSystem _dragDrop = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPointLightSystem _light = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private TargetOutlineSystem _targetOutline = default!;

    private const OverlaySpace ScreenSpaces = OverlaySpace.ScreenSpace | OverlaySpace.ScreenSpaceBelowWorld;

    /// <summary>Overlays that draw the world itself. Every other world overlay is left out of photos.</summary>
    private static readonly HashSet<Type> WorldOverlays = new()
    {
        typeof(Content.Client.Atmos.Overlays.GasTileOverlay),
        typeof(Content.Client.Decals.Overlays.DecalOverlay),
        typeof(Content.Client.Explosion.ExplosionOverlay),
        typeof(Content.Client.Light.AfterLightTargetOverlay),
        typeof(Content.Client.Light.AmbientOcclusionOverlay),
        typeof(Content.Client.Light.BeforeLightTargetOverlay),
        typeof(Content.Client.Light.LightBlurOverlay),
        typeof(Content.Client.Light.RoofOverlay),
        typeof(Content.Client.Light.SunShadowOverlay),
        typeof(Content.Client.Light.TileEmissionOverlay),
        typeof(Content.Client.Overlays.StencilOverlay),
        typeof(Content.Client.Parallax.ParallaxOverlay),
        typeof(Content.Client.Physics.JointVisualsOverlay),
        typeof(Content.Client.Radiation.Overlays.RadiationPulseOverlay),
        typeof(Content.Client.Shuttles.FtlArrivalOverlay),
        typeof(Content.Client.Singularity.SingularityOverlay),
        typeof(Content.Client.Weapons.Misc.TetherGunOverlay),
        typeof(Content.Client._CE.ZLevels.Core.Overlays.CEZLevelBlurOverlay),
        typeof(Content.Client._CE.ZLevels.Core.Overlays.CEZLevelShadowOverlay),
        typeof(Content.Client._Crescent.ShipShields.ShipShieldOverlay),
        typeof(Content.Client._Emberfall.Weapons.Ranged.Overlays.TracerOverlay),
        typeof(Content.Client._FarHorizons.StarSystem.AsteroidBeltOverlay),
        typeof(Content.Client._FarHorizons.StarSystem.PlanetOverlay),
        typeof(Content.Client._FarHorizons.StarSystem.StarLightOverlay),
        typeof(Content.Client._FarHorizons.StarSystem.StarOverlay),
        typeof(Content.Client._Goobstation.Weapons.LaserPointer.LaserPointerOverlay),
        typeof(Content.Client._Goobstation.Wizard.Trail.TrailOverlay),
        typeof(Content.Client._Mono.BlackFlash.BlackFlashImpactOverlay),
        typeof(Content.Client._Mono.BlackFlash.BlackFlashOverlay),
        typeof(Content.Client._Mono.PersonalShield.PersonalShieldOverlay),
        typeof(Content.Client._Mono.Water.WaterOverlay),
        typeof(Content.Client._NF.Emp.Overlays.EmpBlastOverlay),
        typeof(Content.Client._Scp.Shaders.Bloom.ConeLightingOverlay),
        typeof(Content.Client._Scp.Shaders.Bloom.PointLightingOverlay),
        typeof(Content.Client._WF.AsteroidMining.WFAsteroidCrackOverlay),
        typeof(Content.Client._WF.AsteroidMining.WFAsteroidFarOverlay),
        typeof(Content.Client._WF.AsteroidMining.WFMiningBeamOverlay),
        typeof(Content.Client._WF.AsteroidMining.WFOreMagnetOverlay),
        typeof(Content.Client._WF.BitmaskWalls.WallFrillOverlay),
        typeof(Content.Client._WF.Clown.BalloonStringOverlay),
        typeof(Content.Client._WF.Clown.JugglingOverlay),
        typeof(Content.Client._WF.Explosion.ExplosionShockwaveOverlay),
        typeof(Content.Client._WF.FtlEffects.FtlDepartureOverlay),
        typeof(Content.Client._WF.FtlEffects.FtlMotionBackgroundOverlay),
        typeof(Content.Client._WF.FtlEffects.FtlMotionOverlay),
        typeof(Content.Client._WF.HeatHaze.HeatHazeOverlay),
        typeof(Content.Client._WF.Planets.WFPlanetApproachOverlay),
        typeof(Content.Client._WF.RMCCore.Explosion.RMCExplosionShockWaveOverlay),
        typeof(Content.Client._WF.RMCCore.Tether.RMCTetherOverlay),
        typeof(Content.Client._WF.ShipShields.WFShipShieldOverlay),
        typeof(Content.Client._WF.Shuttles.UI.ShuttleLowLightOverlay),
        typeof(Content.Client._WF.Tether.RopeOverlay),
        typeof(Content.Client._WF.TractorBeam.TractorBeamOverlay),
        typeof(Content.Client._WF.Xenonids.Dodge.XenoDodgeOverlay),
        typeof(Content.Client._WF.Xenonids.Screech.RMCXenoScreechShockWaveOverlay),
    };

    /// <summary>Client-only preview entities without a marker component.</summary>
    private static readonly HashSet<string> PreviewPrototypes = new()
    {
        "dragshadow",
        "ReactorFlowArrow",
        "RepairGhost",
        "TegCirculatorArrow",
        "TurbineFlowArrow",
        "XenoConstructionGhost",
    };

    private readonly List<Overlay> _removedOverlays = new();
    private readonly List<(EntityUid Uid, SpriteComponent.PostShaderArgs Args)> _removedShaders = new();
    private readonly List<EntityUid> _hiddenSprites = new();
    private EntityUid? _disabledLight;

    /// <summary>
    /// Removes UI and the <paramref name="hidden"/> entities from around a photo square until <see cref="Restore"/>.
    /// </summary>
    public void Hide(PhotoSquare square, MapId map, NetEntity[] hidden)
    {
        foreach (var net in hidden)
        {
            if (TryGetEntity(net, out var uid) && TryComp<SpriteComponent>(uid, out var sprite) && sprite.Visible)
                HideSprite((uid.Value, sprite));
        }

        if (_overlays.TryGetOverlay<ThermalVisionOverlay>(out var thermal)
            && thermal.LightEntity is { } light
            && TryComp<PointLightComponent>(light, out var pointLight)
            && pointLight.Enabled)
        {
            _light.SetEnabled(light, false, pointLight);
            _disabledLight = light;
        }

        var margin = new Vector2(square.Tiles + 2, square.Tiles + 2);
        var box = new Box2Rotated(Box2.CenteredAround(square.Centre, margin), square.Rotation, square.Centre);
        foreach (var uid in _lookup.GetEntitiesIntersecting(map, box, LookupFlags.Uncontained | LookupFlags.Sundries))
        {
            if (!TryComp<SpriteComponent>(uid, out var sprite))
                continue;

            if (sprite.Visible && IsClientSide(uid) && IsPreview(uid))
                HideSprite((uid, sprite));

            foreach (var entry in _sprite.GetPostShaders(sprite))
            {
                if (IsHighlight(uid, entry.Shader))
                    _removedShaders.Add((uid, ToArgs(entry)));
            }
        }

        foreach (var (uid, args) in _removedShaders)
        {
            _sprite.RemovePostShader((uid, null), args.Id);
        }

        foreach (var overlay in _overlays.AllOverlays)
        {
            if ((overlay.Space & ~ScreenSpaces) != 0 && !WorldOverlays.Contains(overlay.GetType()))
                _removedOverlays.Add(overlay);
        }

        foreach (var overlay in _removedOverlays)
        {
            _overlays.RemoveOverlay(overlay);
        }
    }

    /// <summary>Puts back everything <see cref="Hide"/> removed.</summary>
    public void Restore()
    {
        foreach (var overlay in _removedOverlays)
        {
            _overlays.AddOverlay(overlay);
        }

        foreach (var (uid, args) in _removedShaders)
        {
            _sprite.SetPostShader((uid, null), args);
        }

        foreach (var uid in _hiddenSprites)
        {
            _sprite.SetVisible((uid, null), true);
        }

        if (_disabledLight is { } light)
            _light.SetEnabled(light, true);

        _removedOverlays.Clear();
        _removedShaders.Clear();
        _hiddenSprites.Clear();
        _disabledLight = null;
    }

    private bool IsPreview(EntityUid uid)
    {
        return HasComp<ConstructionGhostComponent>(uid)
            || HasComp<MultipartMachineGhostComponent>(uid)
            || MetaData(uid).EntityPrototype is { } proto && PreviewPrototypes.Contains(proto.ID);
    }

    private void HideSprite(Entity<SpriteComponent> sprite)
    {
        _sprite.SetVisible((sprite.Owner, sprite.Comp), false);
        _hiddenSprites.Add(sprite.Owner);
    }

    private bool IsHighlight(EntityUid uid, ShaderInstance shader)
    {
        return TryComp<InteractionOutlineComponent>(uid, out var outline) && outline.HoverShader == shader
            || _targetOutline.IsTargetShader(shader)
            || _dragDrop.IsDropTargetShader(shader);
    }

    private static SpriteComponent.PostShaderArgs ToArgs(SpriteComponent.PostShaderEntry entry)
    {
        return new SpriteComponent.PostShaderArgs(entry.Id, entry.Shader)
        {
            GetScreenTexture = entry.GetScreenTexture,
            RaiseShaderEvent = entry.RaiseShaderEvent,
            Before = entry.Before,
            After = entry.After,
        };
    }
}
