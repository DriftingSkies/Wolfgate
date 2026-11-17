using System.Numerics;
using Robust.Shared.Player;

namespace Content.Server._WG.NetTraffic;

/// <summary>A ship shell kept off the network; the players near its flight are told where it goes instead.</summary>
[RegisterComponent, Access(typeof(WGNetShellSystem))]
public sealed partial class WGNetShellComponent : Component
{
    public int Id;

    /// <summary>Whether players have been told it was fired.</summary>
    public bool Announced;

    /// <summary>The velocity players were last told.</summary>
    public Vector2 Velocity;

    /// <summary>The players told, who get its course changes and its end.</summary>
    public List<ICommonSession> Viewers = new();
}
