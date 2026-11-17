using Content.Client._WG.Boombox.UI;
using Content.Shared._WG.Audio.InternetSound;
using Content.Shared._WG.Boombox;
using Robust.Client.GameObjects;

namespace Content.Client._WG.Boombox;

/// <summary>Refreshes an open boombox window when the box or its track changes.</summary>
public sealed partial class WGBoomboxSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WGBoomboxComponent, AfterAutoHandleStateEvent>((uid, _, _) => Refresh(uid));
        SubscribeLocalEvent<InternetSoundSourceComponent, AfterAutoHandleStateEvent>((uid, _, _) => Refresh(uid));
    }

    private void Refresh(EntityUid uid)
    {
        if (_ui.TryGetOpenUi<WGBoomboxBoundUserInterface>(uid, WGBoomboxUiKey.Key, out var bui))
            bui.Update();
    }
}
