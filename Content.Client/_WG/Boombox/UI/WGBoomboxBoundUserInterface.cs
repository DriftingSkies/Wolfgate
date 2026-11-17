using Content.Shared._WG.Audio.InternetSound;
using Content.Shared._WG.Boombox;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Boombox.UI;

/// <summary>Opens the boombox window and turns its keys into messages.</summary>
public sealed class WGBoomboxBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private WGBoomboxWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<WGBoomboxWindow>();
        _window.SetOwner(Owner);
        _window.OnAdd += url => SendMessage(new WGBoomboxAddMessage(url));
        _window.OnSkip += () => SendMessage(new WGBoomboxSkipMessage());
        _window.OnStop += () => SendMessage(new WGBoomboxStopMessage());
        _window.OnShuffle += () => SendMessage(new WGBoomboxShuffleMessage());
        _window.OnRepeat += repeat => SendMessage(new WGBoomboxRepeatMessage(repeat));
        _window.OnClear += () => SendMessage(new WGBoomboxClearMessage());
        _window.OnRemove += id => SendMessage(new WGBoomboxRemoveMessage(id));
        _window.OnMoveUp += id => SendMessage(new WGBoomboxMoveUpMessage(id));
        _window.OnVolume += volume => SendMessage(new WGBoomboxVolumeMessage(volume));

        Update();
    }

    public override void Update()
    {
        base.Update();

        if (_window == null || !EntMan.TryGetComponent(Owner, out WGBoomboxComponent? box))
            return;

        EntMan.TryGetComponent(Owner, out InternetSoundSourceComponent? source);
        _window.UpdateState(box, source);
    }
}
