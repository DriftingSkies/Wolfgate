using Content.Shared._WG.Warheads;
using Robust.Shared.Audio;

namespace Content.Server._WG.Ordnance;

public sealed partial class WGOrdnanceSystem
{
    private void InitializeKlaxon()
    {
        SubscribeLocalEvent<WGOrdnanceKlaxonComponent, ComponentShutdown>(OnKlaxonShutdown);
    }

    private void OnKlaxonShutdown(Entity<WGOrdnanceKlaxonComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.Stream = _audio.Stop(ent.Comp.Stream);
    }

    /// <summary>Sounds the klaxon of every armed warhead that has one, and silences the safe ones.</summary>
    private void UpdateKlaxons()
    {
        var query = EntityQueryEnumerator<WGOrdnanceKlaxonComponent, WGWarheadComponent>();
        while (query.MoveNext(out var uid, out var klaxon, out var warhead))
        {
            var armed = warhead.State != WGWarheadState.Safe;
            if (armed == (klaxon.Stream != null))
                continue;

            if (!armed)
            {
                klaxon.Stream = _audio.Stop(klaxon.Stream);
                continue;
            }

            var audioParams = klaxon.Sound.Params
                .WithLoop(true)
                .WithMaxDistance(klaxon.Range)
                .WithVolume(klaxon.Volume);
            klaxon.Stream = _audio.PlayPvs(klaxon.Sound, uid, audioParams)?.Entity;
        }
    }
}
