using Robust.Shared.Audio;

namespace Content.Server._WG.Fragments;

/// <summary>One of the fragment's announcements: its text, sound, two colours and how corrupted it comes through.</summary>
[DataDefinition]
public sealed partial class WGFragmentLine
{
    /// <summary>The announcement; it may use $coords for the fragment's position, which corruption never touches.</summary>
    [DataField(required: true)]
    public LocId Text;

    /// <summary>Used in place of <see cref="Text"/> when members of both factions were aboard.</summary>
    [DataField]
    public LocId? JointText;

    /// <summary>Played to everyone the line reaches; the volume in its params is kept.</summary>
    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>Colour of the sender line.</summary>
    [DataField]
    public Color SenderColor = Color.FromHex("#A05CC8");

    /// <summary>Colour of the announcement text.</summary>
    [DataField]
    public Color TextColor = Color.FromHex("#C42B3C");

    /// <summary>Share of its letters swapped for noise; the coordinates stay readable.</summary>
    [DataField]
    public float Corruption;

    /// <summary>Share of the sender's letters swapped for noise.</summary>
    [DataField]
    public float SenderCorruption;

    /// <summary>Words from the hidden list written over words of the text.</summary>
    [DataField]
    public int HiddenWords;

    /// <summary>Bursts of letters that flicker live in chat.</summary>
    [DataField]
    public int Flickers;

    /// <summary>Whispers, one of which follows the announcement after <see cref="VoiceDelay"/>.</summary>
    [DataField]
    public List<LocId> Voice = new();

    /// <summary>How long after the announcement its whisper follows.</summary>
    [DataField]
    public TimeSpan VoiceDelay = TimeSpan.FromSeconds(5);
}
