using Content.Shared._WG.Ghost;
using Content.Shared.Ghost;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.Ghost;

/// <summary>Stacks ghost notice chips down the right of the screen while the local player is a ghost.</summary>
public sealed class GhostNoticeSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private GhostOrbitSystem _orbit = default!;

    /// <summary>Older chips are dropped past this many.</summary>
    private const int MaxChips = 5;

    private readonly List<GhostNoticeChip> _chips = new();
    private BoxContainer? _stack;

    /// <summary>Chips on screen now, oldest first.</summary>
    public IReadOnlyList<GhostNoticeChip> Chips => _chips;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<GhostNoticeEvent>(OnNotice);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Clear();
        _stack?.Orphan();
        _stack = null;
    }

    private bool IsGhost()
    {
        return _player.LocalEntity is { } local && HasComp<GhostComponent>(local);
    }

    private void OnNotice(GhostNoticeEvent ev)
    {
        if (!IsGhost())
            return;

        var chip = new GhostNoticeChip(ev, EntityManager);
        chip.Orbited += OnOrbited;
        chip.Dismissed += Remove;
        EnsureStack().AddChild(chip);
        chip.SetPositionInParent(0);
        _chips.Add(chip);

        while (_chips.Count > MaxChips)
            Remove(_chips[0]);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_chips.Count == 0)
            return;

        if (!IsGhost())
        {
            Clear();
            return;
        }

        for (var i = _chips.Count - 1; i >= 0; i--)
        {
            var chip = _chips[i];
            chip.Tick(frameTime);
            if (chip.Expired)
                Remove(chip);
        }
    }

    private void OnOrbited(GhostNoticeChip chip)
    {
        _orbit.Orbit(chip.Source);
        Remove(chip);
    }

    private void Remove(GhostNoticeChip chip)
    {
        chip.Orbited -= OnOrbited;
        chip.Dismissed -= Remove;
        chip.Orphan();
        _chips.Remove(chip);
    }

    private void Clear()
    {
        for (var i = _chips.Count - 1; i >= 0; i--)
            Remove(_chips[i]);
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

        // Below the alert icons at the top right, clear of the announcement chips at the top centre.
        _ui.PopupRoot.AddChild(_stack);
        LayoutContainer.SetAnchorPreset(_stack, LayoutContainer.LayoutPreset.TopRight);
        LayoutContainer.SetGrowHorizontal(_stack, LayoutContainer.GrowDirection.Begin);
        LayoutContainer.SetMarginTop(_stack, 220);
        LayoutContainer.SetMarginRight(_stack, -12);
        return _stack;
    }
}
