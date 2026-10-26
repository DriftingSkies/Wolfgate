using Content.Shared._WF.MarkingToggles;
using Content.Shared.ActionBlocker;
using Content.Shared.Humanoid;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.MarkingToggles;

/// <summary>
/// Lets a player show or hide their own body's markings mid-round. Species markings use their own visible flag;
/// custom markings are listed as hidden on the humanoid component. Neither is saved to the character.
/// </summary>
public sealed class MarkingToggleSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>Most toggles one player may send in <see cref="Window"/>: each one resends the whole appearance.</summary>
    private const int Budget = 20;

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    private readonly Dictionary<ICommonSession, (TimeSpan Start, int Count)> _spent = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<MarkingToggleRequestEvent>(OnToggle);
        SubscribeNetworkEvent<CustomMarkingToggleRequestEvent>(OnCustomToggle);
        _player.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _player.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Disconnected)
            _spent.Remove(args.Session);
    }

    private void OnToggle(MarkingToggleRequestEvent msg, EntitySessionEventArgs args)
    {
        if (!TryGetBody(args.SenderSession, out var body)
            || !body.Comp.MarkingSet.Markings.TryGetValue(msg.Category, out var markings))
            return;

        foreach (var marking in markings)
        {
            if (marking.MarkingId != msg.MarkingId || marking.Visible == msg.Visible)
                continue;

            marking.Visible = msg.Visible;
            Dirty(body);
            return;
        }
    }

    private void OnCustomToggle(CustomMarkingToggleRequestEvent msg, EntitySessionEventArgs args)
    {
        if (!TryGetBody(args.SenderSession, out var body) || !body.Comp.CustomMarkings.Contains(msg.Marking))
            return;

        var hidden = body.Comp.HiddenCustomMarkings;
        var changed = msg.Visible ? hidden.Remove(msg.Marking) : !hidden.Contains(msg.Marking);
        if (!changed)
            return;

        if (!msg.Visible)
            hidden.Add(msg.Marking);

        Dirty(body);
    }

    /// <summary>The body a player controls, if it may change its markings now.</summary>
    private bool TryGetBody(ICommonSession session, out Entity<HumanoidAppearanceComponent> body)
    {
        body = default;
        if (session.AttachedEntity is not { } uid || !TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
            return false;

        var now = _timing.RealTime;
        if (!_spent.TryGetValue(session, out var spent) || now - spent.Start >= Window)
            spent = (now, 0);

        if (spent.Count >= Budget)
            return false;

        _spent[session] = (spent.Start, spent.Count + 1);
        if (!_actionBlocker.CanConsciouslyPerformAction(uid))
        {
            _popup.PopupEntity(Loc.GetString("wf-marking-toggle-cannot"), uid, uid);
            return false;
        }

        body = (uid, humanoid);
        return true;
    }
}
