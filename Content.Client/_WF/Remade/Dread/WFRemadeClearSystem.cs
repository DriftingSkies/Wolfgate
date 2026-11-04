using Content.Client._WF.Remade.Betrayal;
using Content.Shared._WF.Remade.Dread;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;

namespace Content.Client._WF.Remade.Dread;

/// <summary>Silences everything the stone left playing on this client when the server says every stone is gone.</summary>
public sealed class WFRemadeClearSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private WFBetrayalMusicSystem _music = default!;
    [Dependency] private WFDreadBedSystem _bed = default!;

    private const string RemadeAudio = "/Audio/_WF/Remade/";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFRemadeClearEvent>(OnClear);
    }

    private void OnClear(WFRemadeClearEvent ev)
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
