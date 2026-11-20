using Content.Server.Light.EntitySystems;
using Content.Shared.Fluids.Components;
using Content.Shared.Light.Components;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ShipAdmin;

public sealed partial class WGShipAdminSystem
{
    [Dependency] private PoweredLightSystem _lights = default!;
    [Dependency] private TagSystem _tags = default!;

    private static readonly ProtoId<TagPrototype> TrashTag = "Trash";
    private static readonly EntProtoId SpareBulb = "LightBulb";
    private static readonly EntProtoId SpareTube = "LightTube";

    /// <summary>Deletes loose trash lying on the ship's floor; nothing anchored, worn or held is touched.</summary>
    private int RemoveTrash(EntityUid grid)
    {
        var count = 0;
        var tagged = EntityQueryEnumerator<TagComponent, TransformComponent>();
        while (tagged.MoveNext(out var uid, out var tags, out var xform))
        {
            // Parented straight to the grid: not in a bag, a hand or a bin.
            if (xform.ParentUid != grid || xform.Anchored || !_tags.HasTag(tags, TrashTag))
                continue;

            QueueDel(uid);
            count++;
        }

        return count;
    }

    /// <summary>Deletes every puddle on the ship.</summary>
    private int RemovePuddles(EntityUid grid)
    {
        var count = 0;
        var puddles = EntityQueryEnumerator<PuddleComponent, TransformComponent>();
        while (puddles.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            QueueDel(uid);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Puts a working lamp in every light fitting on the ship that has none, or a broken or burned one. Switches are
    /// left as they are: a fitting someone turned off stays off with its new lamp.
    /// </summary>
    private int FixLights(EntityUid grid)
    {
        var count = 0;
        var fittings = EntityQueryEnumerator<PoweredLightComponent, TransformComponent>();
        while (fittings.MoveNext(out var uid, out var light, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            var bulb = _lights.GetBulb(uid, light);
            if (bulb != null && CompOrNull<LightBulbComponent>(bulb)?.State is LightBulbState.Normal)
                continue;

            // The working lamp is in hand before the old one comes out, so a fitting is never left emptier.
            if (WorkingLamp(light, xform.Coordinates) is not { } spare)
                continue;

            if (bulb != null && _lights.EjectBulb(uid, null, light) is { } old)
                QueueDel(old);

            if (_lights.InsertBulb(uid, spare, light))
                count++;
            else
                QueueDel(spare);
        }

        return count;
    }

    /// <summary>
    /// A lamp that fits and works: the fitting's own kind, unless that comes broken (as a broken-tube variant does),
    /// then a plain bulb or tube. Null when neither fits.
    /// </summary>
    private EntityUid? WorkingLamp(PoweredLightComponent light, EntityCoordinates at)
    {
        var plain = light.BulbType == LightBulbType.Bulb ? SpareBulb : SpareTube;
        foreach (var proto in new[] { light.HasLampOnSpawn ?? plain, plain })
        {
            var lamp = Spawn(proto, at);
            if (TryComp<LightBulbComponent>(lamp, out var comp) && comp.Type == light.BulbType && comp.State == LightBulbState.Normal)
                return lamp;

            QueueDel(lamp);
        }

        return null;
    }
}
