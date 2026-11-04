using Content.Shared._WF.TestDummies;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WF.TestDummies;

/// <summary>Shows the test subject prompt in the bottom right when an admin asks, and sends the answer back.</summary>
public sealed partial class WFTestDummyPromptSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    private WFTestDummyChip? _chip;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WFTestDummyPromptEvent>(OnPrompt);
        SubscribeNetworkEvent<WFTestDummyClosedEvent>(OnClosed);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Remove();
    }

    private void OnPrompt(WFTestDummyPromptEvent ev)
    {
        Remove();

        var chip = new WFTestDummyChip(ev, _timing);
        chip.Answered += optedIn =>
        {
            RaiseNetworkEvent(new WFTestDummyAnswerEvent(ev.CallId, optedIn));
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

    private void OnClosed(WFTestDummyClosedEvent ev)
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
