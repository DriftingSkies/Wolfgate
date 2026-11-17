using System.Linq;
using Content.Server.CartridgeLoader;
using Content.Shared._WG.Bench;
using Content.Shared._WG.CCVar;
using Content.Shared.CartridgeLoader;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;

namespace Content.Server._WG.Bench;

/// <summary>The Bench app and terminal: what each viewer is shown, and their buttons.</summary>
public sealed partial class WGBenchSystem
{
    [Dependency] private MobStateSystem _mobState = default!;

    private void OnCartridgeReady(Entity<WGBenchCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        PushCartridge(args.Loader);
    }

    private void OnCartridgeMessage(Entity<WGBenchCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not WGBenchCartridgeMessage message)
            return;

        var loader = GetEntity(args.LoaderUid);
        // Only the PDA's carrier may use it.
        if (Transform(loader).ParentUid != args.Actor)
            return;

        Handle(args.Actor, message.Action);
        PushCartridge(loader);
    }

    private void OnTerminalOpened(Entity<WGBenchTerminalComponent> ent, ref BoundUIOpenedEvent args)
    {
        PushTerminal(ent, args.Actor);
    }

    private void OnTerminalMessage(Entity<WGBenchTerminalComponent> ent, ref WGBenchTerminalMessage args)
    {
        Handle(args.Actor, args.Action);
        PushTerminal(ent, args.Actor);
    }

    /// <summary>Carries out a button press from the app or terminal. Everything but a refresh needs a judge.</summary>
    private void Handle(EntityUid actor, WGBenchAction action)
    {
        if (action is WGBenchRefreshAction)
            return;

        if (!IsJudge(actor))
        {
            _popup.PopupEntity(Loc.GetString("wg-bench-error-not-judge"), actor, actor);
            return;
        }

        switch (action)
        {
            case WGBenchIssueAction issue:
                var offender = GetEntity(issue.Offender);
                WGBenchTicket? ticket = null;
                var error = !Exists(offender) || !InRange(actor, offender)
                    ? Loc.GetString("wg-bench-error-range")
                    : Issue(actor, offender, issue.Charges, issue.Ship, issue.Notes, out ticket);
                _popup.PopupEntity(error ?? Loc.GetString("wg-bench-issued", ("number", ticket?.Number ?? 0)), actor, actor);
                break;
            case WGBenchPardonAction pardon:
                if (Pardon(pardon.Number, Name(actor)))
                    _popup.PopupEntity(Loc.GetString("wg-bench-pardon-done", ("number", pardon.Number)), actor, actor);
                break;
            case WGBenchPostOrdinanceAction post:
                if (PostOrdinance(post.Title, post.Text, post.Fine, post.Level, Name(actor)) is { } ordinance)
                    _adminLog.Add(Shared.Database.LogType.Action, Shared.Database.LogImpact.Low,
                        $"{ToPrettyString(actor):judge} posted Bench ordinance {ordinance.Title}");
                break;
            case WGBenchRemoveOrdinanceAction remove:
                _ordinances.RemoveAll(o => o.Id == remove.Id);
                break;
        }
    }

    /// <summary>
    /// The nearby list counts anyone whose body touches the range, so the issue check allows a step's slack past it.
    /// </summary>
    private const float IssueSlack = 1.5f;

    private bool InRange(EntityUid judge, EntityUid offender)
    {
        var range = _cfg.GetCVar(BenchCVars.Range) + IssueSlack;
        return Transform(judge).Coordinates.TryDistance(EntityManager, Transform(offender).Coordinates, out var distance)
            && distance <= range;
    }

    /// <summary>What one viewer sees: the laws always, the ledger and nearby people only for a judge.</summary>
    public WGBenchUiState State(EntityUid viewer)
    {
        var judge = IsJudge(viewer);
        var state = new WGBenchUiState
        {
            Judge = judge,
            Ordinances = _ordinances.Select(o => new WGBenchOrdinanceView
            {
                Id = o.Id, Title = o.Title, Text = o.Text, Fine = o.Fine, Level = o.Level, PostedBy = o.PostedBy,
            }).ToList(),
        };

        if (!judge)
            return state;

        // The same reach Issue allows, so whoever is listed can be ticketed and someone stepping back stays listed.
        foreach (var person in _lookup.GetEntitiesInRange<ActorComponent>(Transform(viewer).Coordinates, _cfg.GetCVar(BenchCVars.Range) + IssueSlack))
        {
            if (person.Owner == viewer || _mobState.IsDead(person) || !InRange(viewer, person))
                continue;
            state.Nearby.Add(new WGBenchPersonView { Entity = GetNetEntity(person), Name = Name(person) });
        }

        state.Nearby.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        for (var i = _tickets.Count - 1; i >= 0; i--)
        {
            var t = _tickets[i];
            state.Tickets.Add(new WGBenchTicketView
            {
                Number = t.Number, Offender = t.Offender, Judge = t.Judge, Ship = t.Ship, Notes = t.Notes,
                Charges = t.Charges, Total = t.Total, Paid = t.Paid, Owed = t.Owed, Level = t.Level, Status = t.Status,
                Issued = t.Issued, Deadline = t.Deadline,
            });
        }

        return state;
    }

    private void PushCartridge(EntityUid loader)
    {
        var holder = Transform(loader).ParentUid;
        if (!TryComp<ActorComponent>(holder, out var actor))
            return;

        _cartridgeLoader.UpdateCartridgeUiState(loader, State(holder), actor.PlayerSession);
    }

    private void PushTerminal(EntityUid terminal, EntityUid viewer)
    {
        _ui.ServerSendUiMessage(terminal, WGBenchTerminalUiKey.Key, new WGBenchTerminalStateMessage(State(viewer)), viewer);
    }

    /// <summary>Keeps every open app and terminal current: nearby people move and debts are paid down.</summary>
    private void RefreshOpen()
    {
        var cartridges = EntityQueryEnumerator<WGBenchCartridgeComponent, CartridgeComponent>();
        while (cartridges.MoveNext(out var uid, out _, out var cartridge))
        {
            if (cartridge.LoaderUid is { } loader
                && TryComp<CartridgeLoaderComponent>(loader, out var loaderComp)
                && loaderComp.ActiveProgram == uid
                && _ui.IsUiOpen(loader, loaderComp.UiKey))
                PushCartridge(loader);
        }

        var terminals = EntityQueryEnumerator<WGBenchTerminalComponent>();
        while (terminals.MoveNext(out var uid, out _))
        {
            foreach (var viewer in _ui.GetActors(uid, WGBenchTerminalUiKey.Key).ToList())
            {
                PushTerminal(uid, viewer);
            }
        }
    }
}
