namespace Content.Server._WF.Husbandry.Components;

/// <summary>A feeder that makes the enclosed floor round it a pen, keeping livestock in it with an outpost's saves.</summary>
[RegisterComponent, Access(typeof(WFHusbandrySystem))]
public sealed partial class WFAnimalFeederComponent : Component
{
    /// <summary>How many animals the feeder keeps in its pen.</summary>
    [DataField]
    public int Capacity = 6;

    /// <summary>The most tiles a pen may have; floor that runs on further isn't enclosed.</summary>
    [DataField]
    public int MaxPenTiles = 100;
}
