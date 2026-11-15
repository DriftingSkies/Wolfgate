using Content.Client.UserInterface.Fragments;
using Content.Shared._WF.NanoChat;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;

namespace Content.Client._WF.NanoChat;

/// <summary>The NanoChat app's PDA fragment.</summary>
public sealed partial class NanoChatUi : UIFragment
{
    private NanoChatUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        var fragment = _fragment = new NanoChatUiFragment();
        fragment.OnMessageSent += (type, number, content, job) =>
            userInterface.SendMessage(new CartridgeUiMessage(new NanoChatUiMessageEvent(type, number, content, job,
                type == NanoChatUiMessageType.SendMessage && fragment.Subtle,
                effect: type == NanoChatUiMessageType.SendMessage ? fragment.Effect : NanoChatEffect.None)));
        fragment.OnGroupAction += (type, group, name, numbers) =>
            userInterface.SendMessage(new CartridgeUiMessage(new NanoChatUiMessageEvent(type, group, name, numbers: numbers)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is NanoChatUiState cast)
            _fragment?.UpdateState(cast);
    }
}
