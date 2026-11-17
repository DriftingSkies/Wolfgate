using Content.Shared.Speech.Components;

namespace Content.Server.Speech.EntitySystems;

public sealed partial class VocalSystem
{
    /// <summary>Loads the species' emote sounds for the entity's current sex again.</summary>
    public void ReloadSounds(Entity<VocalComponent> ent)
    {
        LoadSounds(ent, ent.Comp);
    }
}
