using Content.Client.UserInterface.Fragments;
using Content.Shared._WG.PdaStorage;
using Content.Shared.CartridgeLoader;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.Client._WG.PdaStorage;

public sealed partial class PdaStoreUi : UIFragment
{
    private PdaStoreUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        void Send(PdaStoreAction action, string appId, NetEntity program)
        {
            userInterface.SendMessage(new CartridgeUiMessage(new PdaStoreMessage(action, appId, program)));
        }

        // The loader calls Setup again when the same store is reopened; keep the live fragment and only
        // point it at the current interface. A disposed root needs a fresh fragment.
        if (_fragment is { Disposed: false })
        {
            _fragment.SetActionHandler(Send);
            return;
        }

        _fragment = new PdaStoreUiFragment();
        _fragment.SetActionHandler(Send);
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is PdaStoreUiState storeState)
            _fragment?.UpdateState(storeState);
    }
}
