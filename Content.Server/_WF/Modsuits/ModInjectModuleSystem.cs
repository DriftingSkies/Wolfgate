using Content.Server.Body.Systems;
using Content.Shared._WF.Modsuits;
using Content.Shared._WF.Modsuits.Modules;
using Content.Shared.Stunnable;
using Content.Shared.StatusEffect;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;

namespace Content.Server._WF.Modsuits;

/// <summary>Injects a module's solution into the wearer, optionally clearing stuns.</summary>
public sealed partial class ModInjectModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModInjectModuleComponent, ModModuleUsedEvent>(OnUsed);
    }

    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    private void OnUsed(Entity<ModInjectModuleComponent> ent, ref ModModuleUsedEvent args)
    {
        args.Handled = true;
        _audio.PlayPredicted(ent.Comp.Sound, args.User, args.User);

        if (_net.IsClient)
            return;

        if (ent.Comp.ClearStuns)
        {
            _status.TryRemoveStatusEffect(args.User, "Stun");
            _status.TryRemoveStatusEffect(args.User, "KnockedDown");
        }

        _bloodstream.TryAddToChemicals(args.User, ent.Comp.Solution.Clone());
    }
}
