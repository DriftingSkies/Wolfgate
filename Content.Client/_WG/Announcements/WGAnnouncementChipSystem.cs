using Content.Shared._WG.Announcements;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.Announcements;

/// <summary>Shows announcement chips stacked at the top centre of the screen.</summary>
public sealed partial class WGAnnouncementChipSystem : EntitySystem
{
    [Dependency] private IUserInterfaceManager _ui = default!;

    /// <summary>Older chips are dropped past this many.</summary>
    private const int MaxChips = 4;

    private readonly List<WGAnnouncementChip> _chips = new();
    private BoxContainer? _stack;

    /// <summary>Chips on screen now.</summary>
    public int ChipCount => _chips.Count;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGAnnouncementChipEvent>(OnChip);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _stack?.Orphan();
        _stack = null;
        _chips.Clear();
    }

    private void OnChip(WGAnnouncementChipEvent ev)
    {
        var stack = EnsureStack();

        var chip = new WGAnnouncementChip { HorizontalAlignment = Control.HAlignment.Center };
        chip.Set(ev);
        chip.Dismissed += Remove;

        stack.AddChild(chip);
        chip.SetPositionInParent(0);
        _chips.Add(chip);

        while (_chips.Count > MaxChips)
            Remove(_chips[0]);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        for (var i = _chips.Count - 1; i >= 0; i--)
        {
            var chip = _chips[i];
            chip.Tick(frameTime);
            if (chip.Expired)
                Remove(chip);
        }
    }

    private void Remove(WGAnnouncementChip chip)
    {
        chip.Dismissed -= Remove;
        chip.Orphan();
        _chips.Remove(chip);
    }

    private BoxContainer EnsureStack()
    {
        if (_stack is { Parent: not null })
            return _stack;

        _stack = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            MouseFilter = Control.MouseFilterMode.Ignore,
        };

        _ui.PopupRoot.AddChild(_stack);
        LayoutContainer.SetAnchorPreset(_stack, LayoutContainer.LayoutPreset.CenterTop);
        LayoutContainer.SetGrowHorizontal(_stack, LayoutContainer.GrowDirection.Both);
        LayoutContainer.SetMarginTop(_stack, 48);
        return _stack;
    }
}
