using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ShipShields;

/// <summary>A ship's heat. Shield generators on the hull read their damage from it and add to what it holds and sheds.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WGShipHeatComponent : Component
{
    /// <summary>Heat a hull holds before its tiles are counted in.</summary>
    public const float HullCapacity = 8000f;

    /// <summary>Heat the hull holds now.</summary>
    [DataField]
    public float Heat;

    /// <summary>The part of the heat that came through the shield. It stays while the field is up and taking hits.</summary>
    [DataField]
    public float Hard;

    /// <summary>Heat a hull holds whatever its size.</summary>
    [DataField]
    public float BaseCapacity = HullCapacity;

    /// <summary>Heat each of a small hull's tiles adds to what it holds; each further tile adds less (<c>WGShipShieldEffects.HullCapacity</c>).</summary>
    [DataField]
    public float CapacityPerTile = 15f;

    /// <summary>Heat a hull's size-based capacity tapers toward, however large.</summary>
    [DataField]
    public float MaxCapacity = 30000f;

    /// <summary>
    /// Seconds a hull's own plain rate takes to shed a full load of heat. A generator adds its rate on top; with no
    /// field up the ship cools at <c>WGShipShieldEffects.FieldDownCooling</c> times that.
    /// </summary>
    [DataField]
    public float CoolSeconds = 80f;

    /// <summary>Heat the hull holds by its size. A ship generator adds its capacity to this; an outpost generator's replaces it.</summary>
    [ViewVariables]
    public float Capacity = HullCapacity;

    /// <summary>Heat the hull itself sheds each second at the plain rate, from its size.</summary>
    [ViewVariables]
    public float Dissipation = HullCapacity / 80f;

    /// <summary>Heat the hull's built-in sink holds; it sheds it in <see cref="CoolSeconds"/>. An installed ship generator takes its place.</summary>
    [DataField]
    public float SinkCapacity = 20000f;

    /// <summary>Heat the hull holds with no generator: its size plus its built-in sink.</summary>
    public float BareCapacity => Capacity + SinkCapacity;

    /// <summary>Heat the hull sheds each second at the plain rate with no generator.</summary>
    public float BareDissipation => Dissipation + (CoolSeconds > 0f ? SinkCapacity / CoolSeconds : 0f);

    /// <summary>Whether capacity and cooling match the hull's tiles; cleared when the hull gains or loses one.</summary>
    [ViewVariables]
    public bool Sized;

    /// <summary>Share of each shot's damage the ship's own guns add to its heat, unless a gun sets its own (<see cref="WGShipGunHeatComponent"/>).</summary>
    [DataField]
    public float FireHeat = 0.25f;

    /// <summary>Thermal signature each point of heat the hull holds adds for long-range sensors.</summary>
    [DataField]
    public float HeatSignature = 25f;

    /// <summary>Thermal signature a venting hull adds for long-range sensors, on top of its heat's.</summary>
    [DataField]
    public float VentSignature = 1500000f;

    /// <summary>Played across the hull when a vent starts.</summary>
    [DataField]
    public SoundSpecifier VentSound = new SoundPathSpecifier("/Audio/_WG/ShipShields/vent.ogg");

    /// <summary>Spawned along the hull's edge while it vents.</summary>
    [DataField]
    public EntProtoId? VentEffect = "WGEffectShipHeatVent";

    /// <summary>The generator cooling the hull; any other generator on it only reads the heat.</summary>
    [ViewVariables]
    public EntityUid? Cooler;

    /// <summary>Whether the hull is venting: field down, guns silent, cooling at the fast rate until no heat is left.</summary>
    [ViewVariables]
    public bool Venting;

    /// <summary>Seconds of cooling the current vent still runs for however little heat is left (<c>WGShipShieldEffects.MinVentSeconds</c> at its start).</summary>
    [ViewVariables]
    public float VentHold;

    /// <summary>Whether damage beat the field down and an NPC crew has yet to make its call on it, once the overload ends.</summary>
    [ViewVariables]
    public bool Collapsed;

    /// <summary>When the field last took a hit.</summary>
    [ViewVariables]
    public TimeSpan LastHit;

    /// <summary>When the next plumes leave the hull.</summary>
    [ViewVariables]
    public TimeSpan NextPlume;

    /// <summary>Where plumes can leave the hull and which way they drift, in grid space; found when a vent starts.</summary>
    public readonly List<(Vector2 Position, Angle Facing)> Plumes = new();
}
