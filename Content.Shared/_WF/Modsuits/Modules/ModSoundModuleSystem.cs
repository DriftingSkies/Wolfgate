using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Random;

namespace Content.Shared._WF.Modsuits.Modules;

public sealed partial class ModSoundModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModSoundModuleComponent, ModModuleUsedEvent>(OnSoundUsed);
        SubscribeLocalEvent<ModSpawnModuleComponent, ModModuleUsedEvent>(OnSpawnUsed);
    }

    [Dependency] private INetManager _net = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    private void OnSoundUsed(Entity<ModSoundModuleComponent> ent, ref ModModuleUsedEvent args)
    {
        _audio.PlayPredicted(ent.Comp.Sound, args.User, args.User);
        args.Handled = true;
    }
    private void OnSpawnUsed(Entity<ModSpawnModuleComponent> ent, ref ModModuleUsedEvent args)
    {
        var prototype = ent.Comp.RandomPrototypes.Count > 0
            ? _random.Pick(ent.Comp.RandomPrototypes)
            : ent.Comp.Prototype;

        if (_net.IsServer && prototype != null)
        {
            var spawned = Spawn(prototype, Transform(args.User).Coordinates);
            if (ent.Comp.ToHand)
                _hands.PickupOrDrop(args.User, spawned);
        }

        args.Handled = true;
    }
}
