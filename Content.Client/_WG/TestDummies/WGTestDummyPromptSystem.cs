using Content.Shared._WG.TestDummies;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WG.TestDummies;

/// <summary>Shows the test subject prompt in the bottom right when an admin asks, and sends the answer back.</summary>
public sealed partial class WGTestDummyPromptSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    private WGTestDummyChip? _chip;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WGTestDummyPromptEvent>(OnPrompt);
        SubscribeNetworkEvent<WGTestDummyClosedEvent>(OnClosed);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Remove();
    }

    private void OnPrompt(WGTestDummyPromptEvent ev)
    {
        Remove();

        var chip = new WGTestDummyChip(ev, _timing);
        chip.Answered += optedIn =>
        {
            RaiseNetworkEvent(new WGTestDummyAnswerEvent(ev.CallId, optedIn));
            Remove();
        };

        _chip = chip;
        _ui.WindowRoot.AddChild(chip);
        LayoutContainer.SetAnchorAndMarginPreset(chip, LayoutContainer.LayoutPreset.BottomRight, margin: 16);
        LayoutContainer.SetGrowHorizontal(chip, LayoutContainer.GrowDirection.Begin);
        LayoutContainer.SetGrowVertical(chip, LayoutContainer.GrowDirection.Begin);
    }

    /// <summary>Takes down a prompt whose countdown ran out; the player counts as not having answered.</summary>
    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_chip != null && _chip.Until != TimeSpan.Zero && _timing.RealTime >= _chip.Until)
            Remove();
    }

    private void OnClosed(WGTestDummyClosedEvent ev)
    {
        if (_chip?.CallId == ev.CallId)
            Remove();
    }

    private void Remove()
    {
        _chip?.Orphan();
        _chip = null;
    }
}
