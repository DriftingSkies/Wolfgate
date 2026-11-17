using Content.Server.Power.Components;
using Content.Shared._WG.Lattice;
using Content.Shared.Clothing;
using Robust.Shared.Map.Components;

namespace Content.Server._WG.Lattice;

/// <summary>
/// Shelter from Drift. A powered, anchored stabiliser shelters the ship it stands on; a charm worn round the neck
/// shelters its wearer.
/// </summary>
public sealed partial class WGLatticeShelterSystem : EntitySystem
{
    private const float Interval = 2f;

    private float _accumulator;
    private readonly Dictionary<EntityUid, float> _sheltered = new();
    private readonly List<EntityUid> _lapsed = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WGLatticeCharmComponent, ClothingGotEquippedEvent>(OnCharmEquipped);
        SubscribeLocalEvent<WGLatticeCharmComponent, ClothingGotUnequippedEvent>(OnCharmUnequipped);
    }

    private void OnCharmEquipped(Entity<WGLatticeCharmComponent> charm, ref ClothingGotEquippedEvent args)
    {
        var shelter = EnsureComp<WGLatticeShelterComponent>(args.Wearer);
        shelter.Shelter = charm.Comp.Shelter;
        shelter.StopsTaking = false;
        Dirty(args.Wearer, shelter);
    }

    private void OnCharmUnequipped(Entity<WGLatticeCharmComponent> charm, ref ClothingGotUnequippedEvent args)
    {
        RemComp<WGLatticeShelterComponent>(args.Wearer);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < Interval)
            return;

        _accumulator -= Interval;
        _sheltered.Clear();
        var stabilisers = EntityQueryEnumerator<WGLatticeStabiliserComponent, ApcPowerReceiverComponent, TransformComponent>();
        while (stabilisers.MoveNext(out _, out var stabiliser, out var power, out var xform))
        {
            if (!xform.Anchored || !power.Powered || xform.GridUid is not { } grid)
                continue;

            _sheltered[grid] = MathF.Max(_sheltered.GetValueOrDefault(grid), stabiliser.Shelter);
        }

        _lapsed.Clear();
        var ships = EntityQueryEnumerator<WGLatticeShelterComponent, MapGridComponent>();
        while (ships.MoveNext(out var uid, out _, out _))
        {
            if (!_sheltered.ContainsKey(uid))
                _lapsed.Add(uid);
        }

        foreach (var uid in _lapsed)
        {
            RemComp<WGLatticeShelterComponent>(uid);
        }

        foreach (var (grid, value) in _sheltered)
        {
            var shelter = EnsureComp<WGLatticeShelterComponent>(grid);
            if (MathHelper.CloseTo(shelter.Shelter, value) && shelter.StopsTaking)
                continue;

            shelter.Shelter = value;
            shelter.StopsTaking = true;
            Dirty(grid, shelter);
        }
    }
}
