using Content.Client.UserInterface.Fragments;
using Content.Shared._WG.Bench;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Bench;

/// <summary>The Bench PDA app.</summary>
public sealed partial class BenchUi : UIFragment
{
    private WGBenchPanel? _panel;

    public override Control GetUIFragmentRoot()
    {
        return _panel!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _panel = new WGBenchPanel();
        _panel.OnAction += action => userInterface.SendMessage(new CartridgeUiMessage(new WGBenchCartridgeMessage(action)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is WGBenchUiState cast)
            _panel?.UpdateState(cast);
    }
}
