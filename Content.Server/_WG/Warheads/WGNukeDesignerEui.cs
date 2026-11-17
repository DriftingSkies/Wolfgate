using Content.Server._WG.Warheads.Components;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared._WG.Warheads;
using Content.Shared.Administration;
using Content.Shared.Eui;
using Content.Shared.Explosion;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Warheads;

/// <summary>Server half of the nuke designer: lists presets and chassis, and builds or sets off designs.</summary>
public sealed partial class WGNukeDesignerEui : BaseEui
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private string? _status;

    public WGNukeDesignerEui()
    {
        IoCManager.InjectDependencies(this);
    }

    public override void Opened()
    {
        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        var state = new WGNukeDesignerEuiState { Status = _status };
        _status = null;

        foreach (var preset in _proto.EnumeratePrototypes<WGWarheadPayloadPrototype>())
        {
            state.Presets[preset.ID] = preset.Payload;
        }

        var factory = _entMan.ComponentFactory;
        foreach (var proto in _proto.EnumeratePrototypes<EntityPrototype>())
        {
            if (proto.Abstract
                || !proto.TryGetComponent<WGWarheadComponent>(out _, factory)
                || proto.TryGetComponent<WGImpactFuzeComponent>(out _, factory))
                continue;

            state.Chassis[proto.ID] = proto.Name;
        }

        foreach (var explosion in _proto.EnumeratePrototypes<ExplosionPrototype>())
        {
            state.ExplosionTypes.Add(explosion.ID);
        }

        state.ExplosionTypes.Sort();
        return state;
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (!_admin.HasAdminFlag(Player, AdminFlags.Fun))
        {
            Close();
            return;
        }

        if (Player.AttachedEntity is not { } admin)
            return;

        var warheads = _entMan.System<WGWarheadSystem>();
        var xform = _entMan.System<SharedTransformSystem>();

        switch (msg)
        {
            case WGNukeDesignerSpawnMessage spawn:
                var error = Validate(spawn.Design.Payload) ?? ValidateChassis(spawn.Design.Chassis);
                if (error != null)
                {
                    _status = error;
                    StateDirty();
                    return;
                }

                var uid = warheads.SpawnDesign(spawn.Design, xform.GetMapCoordinates(admin), admin);
                _status = Loc.GetString("wg-nuke-designer-status-spawned", ("entity", _entMan.ToPrettyString(uid)));
                StateDirty();
                break;

            case WGNukeDesignerDetonateMessage detonate:
                if (Validate(detonate.Payload) is { } invalid)
                {
                    _status = invalid;
                    StateDirty();
                    return;
                }

                var adminXform = _entMan.GetComponent<TransformComponent>(admin);
                warheads.Detonate(xform.GetMapCoordinates(admin, adminXform), adminXform.GridUid,
                    detonate.Payload.Clone(), admin);
                _status = Loc.GetString("wg-nuke-designer-status-detonated");
                StateDirty();
                break;
        }
    }

    private string? ValidateChassis(string chassis)
    {
        return _proto.TryIndex<EntityPrototype>(chassis, out var proto)
               && proto.TryGetComponent<WGWarheadComponent>(out _, _entMan.ComponentFactory)
            ? null
            : Loc.GetString("wg-nuke-designer-status-bad-chassis");
    }

    private string? Validate(WGWarheadPayload payload)
    {
        if (payload.ExplosionType is { } type && !_proto.HasIndex<ExplosionPrototype>(type))
            return Loc.GetString("wg-nuke-designer-status-bad-explosion");

        if (!_proto.HasIndex<EntityPrototype>(payload.FalloutPrototype))
            return Loc.GetString("wg-nuke-designer-status-bad-fallout");

        return null;
    }
}
