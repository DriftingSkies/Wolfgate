using Content.Shared._WG.Encounters;

namespace Content.Client._WG.Encounters;

/// <summary>The colours an encounter is drawn in on the radar and the ghost menu, when its ships have none of their own.</summary>
public static class WGEncounterColors
{
    public static Color Category(WGEncounterCategory category)
    {
        return category switch
        {
            WGEncounterCategory.Patrol => Color.FromHex("#6fb6ff"),
            WGEncounterCategory.Threat => Color.FromHex("#ff5c5c"),
            WGEncounterCategory.Distress => Color.FromHex("#ffae3d"),
            _ => Color.FromHex("#7fe0c8"),
        };
    }
}
