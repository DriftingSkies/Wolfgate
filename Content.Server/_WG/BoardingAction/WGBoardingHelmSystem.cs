using System.Linq;
using Content.Server._WG.BoardingAction.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WG.BoardingAction;
using Content.Shared.Shuttles.Components;

namespace Content.Server._WG.BoardingAction;

/// <summary>Builds what a ship's helm banner shows about hacks and disruption, and answers its release button.</summary>
public sealed partial class WGBoardingHelmSystem : EntitySystem
{
    [Dependency] private ShuttleConsoleSystem _console = default!;
    [Dependency] private WGFtlDisruptionSystem _disruption = default!;
    [Dependency] private WGHackHarpoonSystem _hack = default!;

    private readonly HashSet<EntityUid> _group = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShuttleConsoleComponent, WGBoardingReleaseCableMessage>(OnRelease);
    }

    private void OnRelease(Entity<ShuttleConsoleComponent> console, ref WGBoardingReleaseCableMessage args)
    {
        if (TryComp<PilotComponent>(args.Actor, out var pilot) && pilot.Console == console.Owner &&
            Transform(console).GridUid is { } grid)
            _hack.ReleaseCables(grid);
    }

    /// <summary>The banner state for a ship, or null if there is nothing to show.</summary>
    public WGBoardingHelmState? GetState(EntityUid? grid)
    {
        if (grid is not { } ship)
            return null;

        var state = new WGBoardingHelmState();
        if (TryComp<WGHackedGridComponent>(ship, out var hacked))
        {
            state.Hackers = Names(hacked.Hackers.Values);
            state.Lockdown = hacked.LockedOut.Count > 0;
        }

        if (_hack.CableTarget(ship) is { } cable)
            state.CableTarget = ShipName(cable);

        return state.Empty ? null : state;
    }

    /// <summary>Pushes the current state to the open helms of a ship and everything docked to it.</summary>
    public void Refresh(EntityUid? grid)
    {
        if (grid is not { } ship || TerminatingOrDeleted(ship))
            return;

        _group.Clear();
        _disruption.GetDockedShips(ship, _group);
        foreach (var docked in _group)
        {
            _console.RefreshShuttleConsoles(docked);
        }
    }

    private string[] Names(IEnumerable<EntityUid> ships)
    {
        return ships.Distinct()
            .Where(ship => !TerminatingOrDeleted(ship))
            .Select(ShipName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>A ship's name, unless it is hiding it.</summary>
    private string ShipName(EntityUid ship)
    {
        // Helms refresh while grids are being torn down, and a holder can outlast its ship by a moment.
        if (TerminatingOrDeleted(ship) || TryComp<IFFComponent>(ship, out var iff) &&
            (iff.Flags & (IFFFlags.Hide | IFFFlags.HideLabel | IFFFlags.HideLabelAlways)) != 0)
            return Loc.GetString("wg-boarding-unknown-ship");

        var name = MetaData(ship).EntityName;
        return string.IsNullOrWhiteSpace(name) ? Loc.GetString("wg-boarding-unknown-ship") : name;
    }
}
