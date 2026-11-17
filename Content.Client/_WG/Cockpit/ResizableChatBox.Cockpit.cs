namespace Content.Client.UserInterface.Systems.Chat.Widgets;

public sealed partial class ResizableChatBox
{
    private bool _wgCockpitDocked;

    /// <summary>The cockpit owns this chat's bounds while retaining its input and history; undocking re-clamps it.</summary>
    public bool WgCockpitDocked
    {
        get => _wgCockpitDocked;
        set
        {
            if (_wgCockpitDocked == value)
                return;
            _wgCockpitDocked = value;
            if (!value)
                ClampAfterDelay();
        }
    }
}
