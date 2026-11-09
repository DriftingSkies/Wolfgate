using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Lattice;

/// <summary>On a sector map: the Lattice holds it, banded by its profile.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WFLatticeFieldComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<WFLatticeProfilePrototype> Profile = "WFDefault";

    /// <summary>Inner edge of each band for the intact Lattice, in metres from the centre; the first is 0.</summary>
    [DataField, AutoNetworkedField]
    public List<float> Edges = new();

    /// <summary>Field strength at each band edge after the first, for the intact Lattice.</summary>
    [DataField, AutoNetworkedField]
    public List<float> Thresholds = new();

    /// <summary>Every anchor's world position on the map, so clients can work out the field too.</summary>
    [DataField, AutoNetworkedField]
    public List<Vector2> Anchors = new();

    /// <summary>Share of the primary anchors still standing, 0 to 1. Kyphrus looks worse as it falls.</summary>
    [DataField, AutoNetworkedField]
    public float Integrity = 1f;

    /// <summary>Each anchor's strength, matching <see cref="Anchors"/>.</summary>
    [DataField, AutoNetworkedField]
    public List<float> Strengths = new();

    /// <summary>The primary anchors' places round the ring, in order; beam i runs from corner i to the next.</summary>
    [DataField, AutoNetworkedField]
    public List<Vector2> Hex = new();

    /// <summary>Whether each beam of <see cref="Hex"/> still stands.</summary>
    [DataField, AutoNetworkedField]
    public List<bool> HexUp = new();

    /// <summary>Each corner's anchor station name, matching <see cref="Hex"/>, for the status app.</summary>
    [DataField, AutoNetworkedField]
    public List<string> HexNames = new();

    /// <summary>Whether each corner's anchor still stands, matching <see cref="Hex"/>.</summary>
    [DataField, AutoNetworkedField]
    public List<bool> HexAlive = new();

    /// <summary>Caelestinus Central's middle and the radius round it where a broken slice ends.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 CentralCentre;

    [DataField, AutoNetworkedField]
    public float CentralRadius;

    /// <summary>Whether Caelestinus Central's own anchor stands.</summary>
    [DataField, AutoNetworkedField]
    public bool CentralStanding;

    /// <summary>Flesh ships sent against the anchors that are still out there.</summary>
    [DataField, AutoNetworkedField]
    public int FleshShips;

    /// <summary>Every standing anchor that keeps corruption off, and how far.</summary>
    [DataField, AutoNetworkedField]
    public List<Vector2> Wards = new();

    [DataField, AutoNetworkedField]
    public List<float> WardRadii = new();

    /// <summary>The Unstringing: every primary anchor is gone, nothing holds anywhere, and the Coda is everywhere.</summary>
    [DataField, AutoNetworkedField]
    public bool Unstrung;

    /// <summary>Tacet: Kyphrus has imploded and the sector has gone dark.</summary>
    [DataField, AutoNetworkedField]
    public bool Tacet;
}
