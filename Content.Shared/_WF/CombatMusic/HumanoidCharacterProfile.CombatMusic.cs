using System.Linq;
using Content.Shared._WF.CombatMusic;
using Robust.Shared.Prototypes;

namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    /// <summary>Tracks this character's combat music shuffles through. Empty plays their faction's own.</summary>
    [DataField]
    public List<ProtoId<WFCombatTrackPrototype>> CombatMusic { get; set; } = new();

    /// <summary>Copy of this profile with the given combat playlist.</summary>
    public HumanoidCharacterProfile WithCombatMusic(IEnumerable<ProtoId<WFCombatTrackPrototype>> tracks)
    {
        return new(this) { CombatMusic = new(tracks) };
    }

    private bool CombatMusicEqual(HumanoidCharacterProfile other)
    {
        return CombatMusic.SequenceEqual(other.CombatMusic);
    }
}
