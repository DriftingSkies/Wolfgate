namespace Content.Client._WF.Remade.Dread;

/// <summary>A partial so the Remade silence can quiet the whisper bed along with the ambient sound.</summary>
public sealed partial class WFDreadBedSystem
{
    /// <summary>Scales the whole bed, from 0 to 1. 1 leaves it alone.</summary>
    public float BetrayalSilence = 1f;
}
