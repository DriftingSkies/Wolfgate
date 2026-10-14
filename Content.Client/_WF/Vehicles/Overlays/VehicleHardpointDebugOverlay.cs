using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Client.Resources;
using Content.Client._WF.Vehicles.Turret;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Turret;
using Content.Shared._WF.Vehicles.Weapons;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Containers;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Client._WF.Vehicles.Overlays;

/// <summary>
/// Admin debug overlay for turret hardpoints: poses, muzzles, fire arcs and aims, each with its numbers. The labels are
/// developer diagnostics behind an admin command, so they stay literal English and do not go through Fluent.
/// </summary>
public sealed partial class VehicleHardpointDebugOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV | OverlaySpace.ScreenSpace;

    private const float PixelsPerMeter = 32f;
    private static readonly Vector2 TurretLabelWorldOffset = new(-0.55f, -0.18f);
    private static readonly Vector2 GunLabelWorldOffset = new(0.18f, -0.18f);
    private const float LabelLineHeight = 13f;
    private const float LabelPadding = 4f;
    private const float LabelCharWidth = 7f;
    private const double SpriteDirectionBiasRadians = -0.05;

    public bool Enabled { get; set; }

    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IResourceCache _cache = default!;

    private readonly IEntityManager _ents;
    private readonly SharedTransformSystem _transform;
    private readonly SharedContainerSystem _container;
    private readonly VehicleTurretSystem _turrets;
    private readonly VehicleTurretMuzzleOffsetSystem _turretMuzzle;
    private readonly VehicleTurretVisualSystem _turretVisual;
    private readonly Font _font;
    private readonly EntityQuery<GunFireArcComponent> _fireArcQ;
    private readonly EntityQuery<VehicleTurretComponent> _turretQ;
    private readonly EntityQuery<VehicleTurretMuzzleComponent> _turretMuzzleQ;
    private readonly List<DebugLabel> _labels = new();

    public VehicleHardpointDebugOverlay(IEntityManager ents)
    {
        IoCManager.InjectDependencies(this);
        _ents = ents;
        _transform = ents.System<SharedTransformSystem>();
        _container = ents.System<SharedContainerSystem>();
        _turrets = ents.System<VehicleTurretSystem>();
        _turretMuzzle = ents.System<VehicleTurretMuzzleOffsetSystem>();
        _turretVisual = ents.System<VehicleTurretVisualSystem>();
        _font = _cache.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 12);
        _fireArcQ = ents.GetEntityQuery<GunFireArcComponent>();
        _turretQ = ents.GetEntityQuery<VehicleTurretComponent>();
        _turretMuzzleQ = ents.GetEntityQuery<VehicleTurretMuzzleComponent>();
    }

    /// <summary>One line of a label.</summary>
    private readonly record struct DebugLine(string Text, Color Color);
    /// <summary>A label drawn at a world position.</summary>
    private readonly record struct DebugLabel(Vector2 WorldPosition, List<DebugLine> Lines);

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!Enabled)
            return;

        if (args.Space == OverlaySpace.ScreenSpace)
        {
            DrawLabels(args);
            return;
        }

        _labels.Clear();
        var handle = args.WorldHandle;

        var gunQuery = _ents.EntityQueryEnumerator<GunComponent, VehicleTurretComponent>();
        while (gunQuery.MoveNext(out var uid, out var gun, out var gunTurret))
        {
            if (!TryGetGunPositions(uid, args.MapId, out var origin, out var muzzlePos))
                continue;

            handle.DrawLine(origin, muzzlePos, new Color(0.95f, 0.95f, 0.95f, 0.7f));
            handle.DrawCircle(origin, 0.07f, new Color(0.2f, 0.9f, 1f, 0.9f));
            handle.DrawCircle(muzzlePos, 0.1f, new Color(1f, 0.75f, 0.2f, 0.95f));

            DrawShootArc(uid, muzzlePos, args.MapId, handle);
            DrawShootTarget(gun, muzzlePos, args.MapId, handle);
            AddGunDebugLabel(uid, gun, gunTurret, origin, muzzlePos);
        }

        var turretQuery = _ents.EntityQueryEnumerator<VehicleTurretComponent>();
        while (turretQuery.MoveNext(out var uid, out var turret))
        {
            if (!TryGetTurretOverlayPositions(uid, turret, args.MapId, out var basePos, out var anchorPos, out var turretPos))
                continue;

            handle.DrawCircle(basePos, 0.06f, new Color(0.7f, 0.7f, 0.7f, 0.8f));
            handle.DrawLine(basePos, anchorPos, new Color(0.2f, 0.8f, 0.95f, 0.7f));
            handle.DrawCircle(anchorPos, 0.07f, new Color(0.2f, 0.8f, 0.95f, 0.9f));

            if (anchorPos != turretPos)
            {
                handle.DrawLine(anchorPos, turretPos, new Color(1f, 0.7f, 0.2f, 0.8f));
                handle.DrawCircle(turretPos, 0.08f, new Color(1f, 0.7f, 0.2f, 0.95f));
            }

            AddTurretDebugLabel(uid, turret, basePos, turretPos);
        }
    }

    private void DrawLabels(in OverlayDrawArgs args)
    {
        if (args.ViewportControl == null || _labels.Count == 0)
            return;

        var ordered = new List<(DebugLabel Label, Vector2 Position, float Width, float Height)>(_labels.Count);
        foreach (var label in _labels)
        {
            var screenPos = args.ViewportControl.WorldToScreen(label.WorldPosition);
            var width = GetApproxLabelWidth(label.Lines);
            var height = label.Lines.Count * LabelLineHeight;
            ordered.Add((label, screenPos, width, height));
        }

        ordered.Sort((a, b) =>
        {
            var y = a.Position.Y.CompareTo(b.Position.Y);
            return y != 0 ? y : a.Position.X.CompareTo(b.Position.X);
        });

        var occupied = new List<Box2>(ordered.Count);
        foreach (var label in ordered)
        {
            var position = label.Position;
            var rect = GetLabelRect(position, label.Width, label.Height);

            var iterations = 0;
            while (iterations++ < 16)
            {
                Box2? collision = null;
                foreach (var other in occupied)
                {
                    if (rect.Intersects(other))
                    {
                        collision = other;
                        break;
                    }
                }

                if (collision == null)
                    break;

                position.Y = collision.Value.Bottom + LabelPadding;
                rect = GetLabelRect(position, label.Width, label.Height);
            }

            occupied.Add(rect);
            var linePos = position;

            foreach (var line in label.Label.Lines)
            {
                args.ScreenHandle.DrawString(_font, linePos + Vector2.One, line.Text, Color.Black);
                args.ScreenHandle.DrawString(_font, linePos, line.Text, line.Color);
                linePos.Y += LabelLineHeight;
            }
        }
    }

    private static float GetApproxLabelWidth(List<DebugLine> lines)
    {
        var longest = 0;
        foreach (var line in lines)
        {
            if (line.Text.Length > longest)
                longest = line.Text.Length;
        }

        return longest * LabelCharWidth;
    }

    private static Box2 GetLabelRect(Vector2 position, float width, float height)
    {
        return Box2.FromDimensions(position - new Vector2(LabelPadding, LabelPadding),
            new Vector2(width + LabelPadding * 2f, height + LabelPadding * 2f));
    }

    /// <summary>Where the turret gun is drawn and where its shot leaves, from the rendered pose.</summary>
    private bool TryGetGunPositions(EntityUid uid, MapId mapId, out Vector2 origin, out Vector2 muzzlePos)
    {
        origin = default;
        muzzlePos = default;

        if (!_turretMuzzle.TryGetGunOrigin(uid, null, out var muzzleCoords))
            return false;

        var muzzleMap = _transform.ToMapCoordinates(muzzleCoords);
        if (muzzleMap.MapId != mapId)
            return false;

        muzzlePos = muzzleMap.Position;
        origin = _turretVisual.TryGetRenderedPose(uid, out var renderedCoords, out _)
            ? _transform.ToMapCoordinates(renderedCoords).Position
            : _transform.GetWorldPosition(uid);
        return true;
    }

    private void DrawShootArc(EntityUid uid, Vector2 muzzlePos, MapId mapId, DrawingHandleWorld handle)
    {
        if (!_fireArcQ.TryComp(uid, out var arc) ||
            !_container.TryGetContainingContainer((uid, null), out var container))
        {
            return;
        }

        if (_transform.GetMapId(container.Owner) != mapId)
            return;

        var center = _transform.GetWorldRotation(container.Owner) + arc.AngleOffset;
        var halfArc = Angle.FromDegrees(arc.Arc.Degrees / 2f);

        const float arcLength = 3.5f;
        handle.DrawLine(muzzlePos, muzzlePos + center.ToWorldVec() * arcLength, new Color(0.2f, 0.9f, 0.3f, 0.8f));
        handle.DrawLine(muzzlePos, muzzlePos + (center + halfArc).ToWorldVec() * arcLength, new Color(0.95f, 0.45f, 0.2f, 0.8f));
        handle.DrawLine(muzzlePos, muzzlePos + (center - halfArc).ToWorldVec() * arcLength, new Color(0.95f, 0.45f, 0.2f, 0.8f));
    }

    private void DrawShootTarget(GunComponent gun, Vector2 muzzlePos, MapId mapId, DrawingHandleWorld handle)
    {
        if (gun.ShootCoordinates is not { } shootCoordinates)
            return;

        var targetMap = _transform.ToMapCoordinates(shootCoordinates);
        if (targetMap.MapId != mapId)
            return;

        handle.DrawLine(muzzlePos, targetMap.Position, new Color(0.9f, 0.2f, 0.9f, 0.7f));
        handle.DrawCircle(targetMap.Position, 0.08f, new Color(0.9f, 0.2f, 0.9f, 0.8f));
    }

    private void AddGunDebugLabel(
        EntityUid uid,
        GunComponent gun,
        VehicleTurretComponent turret,
        Vector2 origin,
        Vector2 muzzlePos)
    {
        var lines = new List<DebugLine>
        {
            new($"turret-gun {uid.Id}", Color.White),
            new($"origin {FmtPos(origin)} muzzle {FmtPos(muzzlePos)}", new Color(0.30f, 0.90f, 1f, 1f)),
        };

        if (_turretVisual.TryGetRenderedPose(uid, out _, out var renderedRotation))
        {
            lines.Add(new DebugLine(
                $"phys {FmtDeg(_transform.GetWorldRotation(uid))} rendered {FmtDeg(renderedRotation)}",
                new Color(0.80f, 0.80f, 0.80f, 1f)));
        }

        if (gun.ShootCoordinates is { } shootCoordinates)
        {
            var targetMap = _transform.ToMapCoordinates(shootCoordinates);
            lines.Add(new DebugLine(
                $"target {FmtPos(targetMap.Position)} dist {(targetMap.Position - origin).Length():0.00}",
                new Color(0.95f, 0.40f, 0.95f, 1f)));
        }

        if (_turretMuzzleQ.TryComp(uid, out var turretMuzzle))
        {
            lines.Add(new DebugLine(
                $"alt {(turretMuzzle.UseRightNext ? "R" : "L")}",
                new Color(1f, 0.65f, 0.25f, 1f)));
        }

        _labels.Add(new DebugLabel(muzzlePos + GunLabelWorldOffset, lines));
    }

    private void AddTurretDebugLabel(
        EntityUid uid,
        VehicleTurretComponent turret,
        Vector2 basePos,
        Vector2 turretPos)
    {
        var lines = new List<DebugLine>
        {
            new($"turret {uid.Id}", Color.White),
        };

        if (TryGetVehicle(uid, out var vehicle))
        {
            TryGetAnchorTurret(uid, turret, out var anchorUid, out var anchorTurret);
            var vehicleRot = _transform.GetWorldRotation(vehicle);
            var eyeRot = _eye.CurrentEye.Rotation;
            var vehicleFacing = GetVehicleFacingAngle(vehicle, vehicleRot);
            var renderFacing = GetRenderFacing(turret, anchorTurret, vehicleRot, vehicleFacing, eyeRot);
            var pixelDir = turret.UseDirectionalOffsets ? GetDirectionalDir(renderFacing) : Direction.Invalid;
            var pixelPreset = turret.UseDirectionalOffsets ? GetDirectionalOffset(turret, pixelDir) : turret.PixelOffset;
            var renderedRotation = _turretVisual.TryGetRenderedPose(uid, out _, out var renderedPoseRotation)
                ? renderedPoseRotation
                : _transform.GetWorldRotation(uid);

            lines.Add(new DebugLine(
                $"veh {vehicle.Id} face {vehicleFacing.GetCardinalDir()} eye {FmtDeg(eyeRot)} render {FmtDeg(renderedRotation)}",
                new Color(0.80f, 0.80f, 0.80f, 1f)));
            lines.Add(new DebugLine(
                $"anchor {anchorUid.Id} world {FmtDeg(turret.WorldRotation)} target {FmtDeg(turret.TargetRotation)}",
                new Color(0.55f, 0.90f, 1f, 1f)));
            lines.Add(new DebugLine(
                $"pix {(turret.UseDirectionalOffsets ? pixelDir.ToString() : "-")} {FmtVec(pixelPreset)} base {FmtPos(basePos)} pos {FmtPos(turretPos)}",
                new Color(0.75f, 0.95f, 0.65f, 1f)));
        }

        _labels.Add(new DebugLabel(turretPos + TurretLabelWorldOffset, lines));
    }

    private static string FmtDeg(Angle angle)
    {
        return $"{angle.Degrees:0.0}";
    }

    private static string FmtVec(Vector2 vector)
    {
        return $"<{vector.X:0.00},{vector.Y:0.00}>";
    }

    private static string FmtPos(Vector2 vector)
    {
        return $"{vector.X:0.00},{vector.Y:0.00}";
    }

    private bool TryGetTurretOverlayPositions(
        EntityUid turretUid,
        VehicleTurretComponent turret,
        MapId mapId,
        out Vector2 basePos,
        out Vector2 anchorPos,
        out Vector2 turretPos)
    {
        basePos = default;
        anchorPos = default;
        turretPos = default;

        if (!TryGetVehicle(turretUid, out var vehicle))
            return false;

        var baseCoords = _transform.GetMoverCoordinates(vehicle);
        var baseMap = _transform.ToMapCoordinates(baseCoords);
        if (baseMap.MapId != mapId)
            return false;

        TryGetAnchorTurret(turretUid, turret, out var anchorUid, out var anchorTurret);

        var vehicleRot = _transform.GetWorldRotation(vehicle);
        var eyeRot = _eye.CurrentEye.Rotation;
        var baseFacingAngle = GetVehicleFacingAngle(vehicle, vehicleRot);
        var anchorFacingAngle = GetRenderFacing(anchorTurret, anchorTurret, vehicleRot, baseFacingAngle, eyeRot);
        var anchorPixelOffset = GetPixelOffset(anchorTurret, anchorFacingAngle) / PixelsPerMeter;
        var anchorLocalOffset = GetVehicleLocalOffset(anchorTurret, anchorPixelOffset, vehicleRot, eyeRot);
        var anchorCoords = baseCoords.Offset(anchorLocalOffset);

        basePos = baseMap.Position;
        anchorPos = _transform.ToMapCoordinates(anchorCoords).Position;

        if (anchorUid == turretUid)
        {
            turretPos = anchorPos;
            return true;
        }

        var localRot = anchorTurret.RotateToCursor ? anchorTurret.WorldRotation : Angle.Zero;
        var turretFacingAngle = GetRenderFacing(turret, anchorTurret, vehicleRot, baseFacingAngle, eyeRot);
        var worldOffset = GetPixelOffset(turret, turretFacingAngle) / PixelsPerMeter;
        Vector2 relativeAnchorOffset;
        Vector2 turretLocalOffset;

        if (turret.OffsetRotatesWithTurret)
        {
            if (turret.UseDirectionalOffsets)
            {
                var dir = GetDirectionalDir(turretFacingAngle);
                var snappedAngle = dir.ToAngle();
                relativeAnchorOffset = (-snappedAngle).RotateVec(worldOffset);
                turretLocalOffset = (localRot - snappedAngle).RotateVec(worldOffset);
            }
            else
            {
                relativeAnchorOffset = worldOffset;
                turretLocalOffset = localRot.RotateVec(relativeAnchorOffset);
            }
        }
        else
        {
            turretLocalOffset = (-vehicleRot).RotateVec(worldOffset);
            relativeAnchorOffset = (-localRot).RotateVec(turretLocalOffset);
        }

        MapCoordinates turretMap;
        if (turret.OffsetRotatesWithTurret)
            turretMap = _transform.ToMapCoordinates(new EntityCoordinates(anchorUid, relativeAnchorOffset));
        else
            turretMap = _transform.ToMapCoordinates(baseCoords.Offset(anchorLocalOffset + relativeAnchorOffset));
        if (turretMap.MapId != mapId)
            return false;

        turretPos = turretMap.Position;
        return true;
    }

    private Vector2 GetPixelOffset(VehicleTurretComponent turret, Angle facing)
    {
        if (!turret.UseDirectionalOffsets)
            return turret.PixelOffset;

        var baseOffset = turret.PixelOffset;
        var normalized = facing.Theta % MathHelper.TwoPi;
        if (normalized < 0)
            normalized += MathHelper.TwoPi;

        var dir = GetDirectionalDir(new Angle(normalized));
        return baseOffset + GetDirectionalOffset(turret, dir);
    }

    private static Vector2 GetDirectionalOffset(VehicleTurretComponent turret, Direction dir)
    {
        return dir switch
        {
            Direction.South => turret.PixelOffsetSouth,
            Direction.East => turret.PixelOffsetEast,
            Direction.North => turret.PixelOffsetNorth,
            Direction.West => turret.PixelOffsetWest,
            _ => Vector2.Zero
        };
    }

    /// <summary>The cardinal direction the sprite shows for a facing, with the sprite sheet's slight bias.</summary>
    private static Direction GetDirectionalDir(Angle facing)
    {
        var angle = facing.Reduced().FlipPositive();
        var mod = (Math.Floor(angle.Theta / MathHelper.PiOver2) % 2) - 0.5;
        var modTheta = angle.Theta + mod * SpriteDirectionBiasRadians;

        return ((int) Math.Round(modTheta / MathHelper.PiOver2) % 4) switch
        {
            0 => Direction.South,
            1 => Direction.East,
            2 => Direction.North,
            _ => Direction.West
        };
    }

    private Angle GetVehicleFacingAngle(EntityUid vehicle, Angle vehicleRot)
    {
        return _turrets.GetVehicleFacingAngle(vehicle, vehicleRot);
    }

    private static Vector2 GetVehicleLocalOffset(
        VehicleTurretComponent turret,
        Vector2 offset,
        Angle vehicleRot,
        Angle eyeRot)
    {
        if (turret.UseDirectionalOffsets)
            offset = (-eyeRot).RotateVec(offset);

        return (-vehicleRot).RotateVec(offset);
    }

    private static Angle GetRenderFacing(
        VehicleTurretComponent turret,
        VehicleTurretComponent anchorTurret,
        Angle vehicleRot,
        Angle baseFacingAngle,
        Angle eyeRot)
    {
        return (GetOffsetFacing(turret, anchorTurret, vehicleRot, baseFacingAngle) + eyeRot).Reduced();
    }

    private static Angle GetOffsetFacing(
        VehicleTurretComponent turret,
        VehicleTurretComponent anchorTurret,
        Angle vehicleRot,
        Angle baseFacingAngle)
    {
        if (!turret.OffsetRotatesWithTurret)
            return baseFacingAngle;

        return (vehicleRot + anchorTurret.WorldRotation).Reduced();
    }

    private bool TryGetVehicle(EntityUid turretUid, out EntityUid vehicle)
    {
        vehicle = default;
        var current = turretUid;

        while (_container.TryGetContainingContainer((current, null), out var container))
        {
            var owner = container.Owner;
            if (_ents.HasComponent<RMCVehicleComponent>(owner))
            {
                vehicle = owner;
                return true;
            }

            current = owner;
        }

        return false;
    }

    private void TryGetAnchorTurret(
        EntityUid turretUid,
        VehicleTurretComponent turret,
        out EntityUid anchorUid,
        out VehicleTurretComponent anchorTurret)
    {
        anchorUid = turretUid;
        anchorTurret = turret;

        if (!_ents.HasComponent<VehicleTurretAttachmentComponent>(turretUid))
            return;

        if (!TryGetParentTurret(turretUid, out var parentUid, out var parentTurret))
            return;

        anchorUid = parentUid;
        anchorTurret = parentTurret;
    }

    private bool TryGetParentTurret(
        EntityUid turretUid,
        out EntityUid parentUid,
        out VehicleTurretComponent parentTurret)
    {
        parentUid = default;
        parentTurret = default!;
        var current = turretUid;

        while (_container.TryGetContainingContainer((current, null), out var container))
        {
            var owner = container.Owner;
            if (_turretQ.TryComp(owner, out var turret))
            {
                parentUid = owner;
                parentTurret = turret;
                return true;
            }

            current = owner;
        }

        return false;
    }
}
