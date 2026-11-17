using Robust.Shared.Network;

namespace Content.Shared._WG.ShipCommand;

/// <summary>
/// A client can't tell who counts as a ship's staff, so it predicts no emag aboard a ship with
/// <see cref="WGShipCommandComponent.StaffOnlyWires"/> and leaves the server to decide; a predicted success would show
/// the emag working on a ship that then refuses it.
/// </summary>
public sealed class WGShipEmagPredictionSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGEmagAttemptEvent>(OnEmagAttempt);
    }

    private void OnEmagAttempt(ref WGEmagAttemptEvent args)
    {
        if (_net.IsClient
            && Transform(args.Target).GridUid is { } grid
            && TryComp<WGShipCommandComponent>(grid, out var command)
            && command.StaffOnlyWires)
            args.Cancelled = true;
    }
}
