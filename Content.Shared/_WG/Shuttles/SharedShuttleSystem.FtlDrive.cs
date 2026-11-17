using Content.Shared._WG.Lattice;
using Content.Shared._WG.Shuttles;

namespace Content.Shared.Shuttles.Systems;

public abstract partial class SharedShuttleSystem
{
    /// <summary>
    /// Reports whether a ship can jump, by the two checks <see cref="GetFTLRange"/> makes: a drive exists, and the one
    /// it picks is powered; and that the Lattice band it is in lets drives work.
    /// </summary>
    public WGFtlDriveStatus GetFtlDriveStatus(EntityUid shuttleUid)
    {
        if (!TryGetFTLDrive(shuttleUid, out var drive, out _))
            return WGFtlDriveStatus.Missing;

        if (TryComp<WGDriftComponent>(shuttleUid, out var drift) && drift.NoFtl)
            return WGFtlDriveStatus.Unmoored;

        return _powerReceiverSystem.IsPowered(drive.Value) ? WGFtlDriveStatus.Ready : WGFtlDriveStatus.Unpowered;
    }
}
