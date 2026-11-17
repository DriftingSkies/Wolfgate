using Content.Client._WG.Remade.Betrayal;
using Content.Shared._WG.Remade.Dread;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;

namespace Content.Client._WG.Remade.Dread;

/// <summary>Silences everything the stone left playing on this client when the server says every stone is gone.</summary>
public sealed class WGRemadeClearSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private WGBetrayalMusicSystem _music = default!;
    [Dependency] private WGDreadBedSystem _bed = default!;

    private const string RemadeAudio = "/Audio/_WG/Remade/";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGRemadeClearEvent>(OnClear);
    }

    private void OnClear(WGRemadeClearEvent ev)
    {
        _bed.Reset();
        _music.Reset();

        // Anything else of the stone's this client started itself.
        var streams = new List<EntityUid>();
        var query = EntityQueryEnumerator<AudioComponent>();
        while (query.MoveNext(out var uid, out var audio))
        {
            // Read into a local first: the access analyzer counts a call on the field as more than a read.
            var file = audio.FileName;
            if (IsClientSide(uid) && file.StartsWith(RemadeAudio, StringComparison.Ordinal))
                streams.Add(uid);
        }

        foreach (var uid in streams)
        {
            _audio.Stop(uid);
        }
    }
}
