using Content.Shared._WF.Planets;

namespace Content.Server.Pinpointer;

public sealed partial class NavMapSystem
{
    /// <summary>
    /// A planet layer grid is a world's whole terrain. Its nav map would cover every loaded chunk, cost hundreds of
    /// milliseconds to send whenever the layer reaches a client, and nothing shows it, so a station on a planet gets
    /// no nav map for the layer grid.
    /// </summary>
    private bool WfSkipsNavMap(EntityUid grid) => HasComp<WFPlanetLayerComponent>(grid);
}
