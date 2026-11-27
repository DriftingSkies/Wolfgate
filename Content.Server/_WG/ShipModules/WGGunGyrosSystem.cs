using Content.Shared._WG.ShipModules;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.ShipModules;

/// <summary>
/// Runs advanced gyros: rotating guns on a hull that has them turn faster, and each wide swing whirs and spins a ring
/// round the gun.
/// </summary>
public sealed partial class WGGunGyrosSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AudioSystem _audio = default!;

    /// <summary>Shortest time between two swing effects of one gun.</summary>
    private static readonly TimeSpan SwingInterval = TimeSpan.FromSeconds(1.5);

    private readonly Dictionary<EntityUid, TimeSpan> _lastSwing = new();
    private EntityQuery<WGGunGyrosComponent> _gyros;

    public override void Initialize()
    {
        base.Initialize();
        _gyros = GetEntityQuery<WGGunGyrosComponent>();
        SubscribeLocalEvent<WGShipModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<WGShipModuleDeactivatedEvent>(OnDeactivated);
    }

    private void OnActivated(ref WGShipModuleActivatedEvent args)
    {
        if (args.Module.Effect is not WGGunGyrosEffect effect)
            return;

        var gyros = EnsureComp<WGGunGyrosComponent>(args.Grid);
        gyros.TurnFactor = effect.TurnFactor;
        gyros.SwingSound = effect.SwingSound;
        gyros.SwingAngle = effect.SwingAngle;
    }

    private void OnDeactivated(ref WGShipModuleDeactivatedEvent args)
    {
        if (args.Module.Effect is WGGunGyrosEffect)
            RemCompDeferred<WGGunGyrosComponent>(args.Grid);
    }

    /// <summary>
    /// Multiplier on a gun's turn rate this tick, from its hull's gyros; a turn of more than the swing angle
    /// (<paramref name="offset"/>, in radians) whirs and glows.
    /// </summary>
    public float TurnFactor(EntityUid gun, EntityUid? grid, double offset)
    {
        if (!_gyros.TryComp(grid, out var gyros))
            return 1f;

        var now = _timing.CurTime;
        if (Math.Abs(offset) >= MathHelper.DegreesToRadians(gyros.SwingAngle)
            && (!_lastSwing.TryGetValue(gun, out var last) || now >= last + SwingInterval))
        {
            if (_lastSwing.Count > 2048)
                _lastSwing.Clear();
            _lastSwing[gun] = now;
            _audio.PlayPvs(gyros.SwingSound, gun, AudioParams.Default.WithVolume(-6f));
            RaiseNetworkEvent(new WGGunGyroSwingEvent(GetNetEntity(gun)), Filter.Pvs(gun));
        }

        return MathF.Max(gyros.TurnFactor, 0f);
    }
}

/// <summary>On a ship's grid while advanced gyros are fitted.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WGGunGyrosComponent : Component
{
    /// <summary>Multiplier on each gun's turn rate.</summary>
    [ViewVariables]
    public float TurnFactor = 1.5f;

    [ViewVariables]
    public SoundSpecifier? SwingSound;

    /// <summary>Degrees off its aim a gun must be for its swing to whir and glow.</summary>
    [ViewVariables]
    public float SwingAngle = 20f;
}
