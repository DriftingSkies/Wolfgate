using Content.Server._Mono.AmmoLoader;
using Content.Shared._Mono.AmmoLoader;
using Content.Shared._WG.Weapons.Ranged.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Server._WG.Weapons.Ranged.Systems;

/// <summary>
/// Reloads auto-fed guns from their linked ammo loaders once a shot has resolved.
/// </summary>
public sealed partial class WGLoaderAutoFeedSystem : EntitySystem
{
    [Dependency] private AmmoLoaderSystem _ammoLoader = default!;

    private readonly HashSet<EntityUid> _pending = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGLoaderAutoFeedComponent, AmmoShotEvent>(OnShot);
    }

    private void OnShot(EntityUid uid, WGLoaderAutoFeedComponent comp, AmmoShotEvent args)
    {
        _pending.Add(uid);
    }

    public override void Update(float frameTime)
    {
        if (_pending.Count == 0)
            return;

        foreach (var gun in _pending)
        {
            if (!TryComp<DeviceLinkSinkComponent>(gun, out var sink))
                continue;

            foreach (var source in sink.LinkedSources)
            {
                if (!TryComp<AmmoLoaderComponent>(source, out var loader) ||
                    !Transform(source).Anchored ||
                    !_ammoLoader.IsTurretLinked((source, loader), gun))
                    continue;

                if (_ammoLoader.TryTransferAmmoTo((source, loader), gun))
                    break;
            }
        }

        _pending.Clear();
    }
}
