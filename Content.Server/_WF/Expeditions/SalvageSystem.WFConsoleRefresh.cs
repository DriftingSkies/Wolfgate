using Content.Shared.Salvage.Expeditions;

namespace Content.Server.Salvage;

public sealed partial class SalvageSystem
{
    /// <summary>
    /// Refreshes an expedition console whenever it is opened or re-anchored, so a stale empty state never sticks.
    /// </summary>
    private void InitializeWFConsoleRefresh()
    {
        SubscribeLocalEvent<SalvageExpeditionConsoleComponent, BoundUIOpenedEvent>(OnWFConsoleOpened);
        SubscribeLocalEvent<SalvageExpeditionConsoleComponent, AnchorStateChangedEvent>(OnWFConsoleAnchor);
    }

    private void OnWFConsoleOpened(Entity<SalvageExpeditionConsoleComponent> console, ref BoundUIOpenedEvent args)
    {
        if (!Equals(args.UiKey, SalvageConsoleUiKey.Expedition))
            return;

        // A station whose offers lapsed without regenerating gets them now instead of an empty board.
        if (_station.GetOwningStation(console) is { } station &&
            TryComp<SalvageExpeditionDataComponent>(station, out var data) &&
            !data.Claimed &&
            data.Missions.Count == 0 &&
            data.NextOffer <= _timing.CurTime)
        {
            data.NextOffer = _timing.CurTime + TimeSpan.FromSeconds(_cooldown);
            GenerateMissions(data);
            UpdateConsoles(station, data);
        }

        UpdateConsole(console);
    }

    private void OnWFConsoleAnchor(Entity<SalvageExpeditionConsoleComponent> console, ref AnchorStateChangedEvent args)
    {
        UpdateConsole(console);
    }
}
