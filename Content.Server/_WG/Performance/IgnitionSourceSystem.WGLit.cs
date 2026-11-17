using Content.Shared.IgnitionSource;

namespace Content.Server.IgnitionSource;

/// <summary>
/// Tracks lit ignition sources, so the per-tick pass doesn't walk every unlit lighter, torch and welder in the round.
/// </summary>
public sealed partial class IgnitionSourceSystem
{
    /// <summary>Ignition sources that are lit right now.</summary>
    public readonly HashSet<EntityUid> WGLit = new();

    private readonly List<EntityUid> _wgLitWalk = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<IgnitionSourceComponent, ComponentStartup>(WGOnStartup);
        SubscribeLocalEvent<IgnitionSourceComponent, ComponentShutdown>(WGOnShutdown);
    }

    private void WGOnStartup(Entity<IgnitionSourceComponent> ent, ref ComponentStartup args)
    {
        WGIgnitedChanged(ent, ent.Comp.Ignited);
    }

    private void WGOnShutdown(Entity<IgnitionSourceComponent> ent, ref ComponentShutdown args)
    {
        WGLit.Remove(ent);
    }

    protected override void WGIgnitedChanged(EntityUid uid, bool ignited)
    {
        if (ignited)
            WGLit.Add(uid);
        else
            WGLit.Remove(uid);
    }
}
