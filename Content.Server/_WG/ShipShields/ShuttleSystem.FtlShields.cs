namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    /// <summary>Drops departing fields as soon as spoolup has successfully started.</summary>
    private void SuppressWolfgateFtlShields(EntityUid shuttle)
    {
        var departing = new HashSet<EntityUid>();
        GetAllDockedShuttles(shuttle, departing);
        foreach (var grid in departing)
            _wgCollisionShields.SuppressWolfgateShieldForFtl(grid);
    }
}
