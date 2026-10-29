using Content.Shared.Item.ItemToggle.Components;
using Robust.Shared.Audio.Systems;

namespace Content.Client._WF.Audio;

/// <summary>
/// Stops a toggled item's predicted active loop once the item is off. The loop is local to the client that turned the
/// item on, so when someone else turned it off only their copy stopped and the holder kept hearing the hum.
/// </summary>
public sealed class WFToggleLoopStopSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<ItemToggleActiveSoundComponent, ItemToggleComponent>();
        while (query.MoveNext(out _, out var sound, out var toggle))
        {
            if (!toggle.Activated && sound.PlayingStream != null)
                sound.PlayingStream = _audio.Stop(sound.PlayingStream);
        }
    }
}
