using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Photography;

/// <summary>An instant camera: takes a photo of the area around a target and prints it, or stores it.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PhotoCameraComponent : Component
{
    /// <summary>Shots left on the film. Ignored when the camera stores photos instead of printing.</summary>
    [DataField, AutoNetworkedField]
    public int FilmLeft = 10;

    /// <summary>Shots a full film gives.</summary>
    [DataField, AutoNetworkedField]
    public int FilmMax = 10;

    /// <summary>Half-width setting, see <see cref="PhotoRules.TilesForAperture"/>.</summary>
    [DataField, AutoNetworkedField]
    public int Aperture = 2;

    [DataField, AutoNetworkedField]
    public bool Monochrome;

    /// <summary>Whether the camera lights the scene when it takes a photo.</summary>
    [DataField, AutoNetworkedField]
    public bool Flash = true;

    /// <summary>Whether a printed photo asks its photographer for a name, description and caption.</summary>
    [DataField, AutoNetworkedField]
    public bool Customise = true;

    /// <summary>Whether only the photographer hears the shutter and the settings clicks.</summary>
    [DataField]
    public bool Silent;

    /// <summary>Whether ghosts show up in its photos.</summary>
    [DataField]
    public bool SeeGhosts;

    /// <summary>Whether revenants it photographs are revealed, stunned and drained.</summary>
    [DataField]
    public bool StealsSouls;

    /// <summary>Whether photos go to its <see cref="PhotoGalleryComponent"/> rather than being printed.</summary>
    [DataField]
    public bool StoresPhotos;

    /// <summary>Whether its user is a station AI, whose sight is limited to the station cameras.</summary>
    [DataField]
    public bool CameraNetSight;

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(6.4);

    /// <summary>When the camera can take its next photo.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan NextShot;

    /// <summary>How long the flash light stays on.</summary>
    [DataField]
    public TimeSpan FlashDuration = TimeSpan.FromSeconds(0.2);

    /// <summary>Farthest a photo's centre may be from its photographer, in tiles.</summary>
    [DataField]
    public float Range = 12f;

    [DataField]
    public SoundSpecifier ShutterSound = new SoundCollectionSpecifier("WFPhotoShutter");

    [DataField]
    public SoundSpecifier ClickSound = new SoundPathSpecifier("/Audio/UserInterface/click.ogg");

    [DataField]
    public SoundSpecifier FilmSound = new SoundPathSpecifier("/Audio/UserInterface/click.ogg");
}

/// <summary>A film cartridge that refills an empty camera.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class PhotoFilmComponent : Component;

/// <summary>A printed photo. The image lives on the server under <see cref="Hash"/>.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class PhotoComponent : Component
{
    /// <summary>Hex SHA-256 of the PNG. Empty for a blank photo.</summary>
    [DataField, AutoNetworkedField]
    public string Hash = string.Empty;

    /// <summary>Image size in pixels.</summary>
    [DataField, AutoNetworkedField]
    public int Width;

    [DataField, AutoNetworkedField]
    public int Height;

    /// <summary>The picture drawn on the sprite, as base64 RGB bytes, so drawing it needs no image download.</summary>
    [DataField, AutoNetworkedField]
    public string Thumbnail = string.Empty;

    /// <summary>Text written on the back.</summary>
    [DataField, AutoNetworkedField]
    public string BackText = string.Empty;

    /// <summary>Whether a ghost was caught in it.</summary>
    [DataField, AutoNetworkedField]
    public bool Haunted;

    /// <summary>Whether it carried over from an earlier round, which shows it aged.</summary>
    [DataField, AutoNetworkedField]
    public bool Old;
}

/// <summary>A wall frame that shows one photo.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class PhotoFrameComponent : Component
{
    /// <summary>Container that holds the framed photo.</summary>
    public const string ContainerId = "photo";

    /// <summary>Sprite state shown while the frame is empty. Null shows the bare frame.</summary>
    [DataField, AutoNetworkedField]
    public string? PortraitState;

    /// <summary>Name and description shown while empty, for portraits.</summary>
    [DataField]
    public LocId? PortraitName;

    [DataField]
    public LocId? PortraitDescription;

    /// <summary>Tool that cuts the photo out.</summary>
    [DataField]
    public string CutQuality = "Cutting";

    [DataField]
    public SoundSpecifier CutSound = new SoundPathSpecifier("/Audio/Items/wirecutter.ogg");
}

/// <summary>Photos kept in memory rather than printed: a station AI or a cyborg's camera.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class PhotoGalleryComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<PhotoGalleryEntry> Photos = new();

    /// <summary>Most photos kept. The oldest goes when a new one would pass it.</summary>
    [DataField]
    public int Capacity = 50;

    /// <summary>Number given to the next photo, so numbers don't repeat after a delete.</summary>
    [DataField]
    public int NextNumber = 1;

    /// <summary>Battery charge a print costs. Null for a gallery that can't print.</summary>
    [DataField]
    public float? PrintCharge;

    [DataField]
    public SoundSpecifier PrintSound = new SoundPathSpecifier("/Audio/_WF/Photography/taperecorder_print.ogg");
}

/// <summary>One stored photo in a gallery.</summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial record PhotoGalleryEntry
{
    /// <summary>Hex SHA-256 of the PNG.</summary>
    [DataField]
    public string Hash = string.Empty;

    /// <summary>Image size in pixels.</summary>
    [DataField]
    public int Width;

    [DataField]
    public int Height;

    [DataField]
    public string Name = string.Empty;

    [DataField]
    public string Description = string.Empty;

    [DataField]
    public bool Haunted;
}

/// <summary>Gives a starting camera and personal album, unless an album came back from the last round.</summary>
[RegisterComponent]
public sealed partial class PhotographerComponent : Component;

[Serializable, NetSerializable]
public enum PhotoCameraVisuals : byte
{
    Ready,
}

[Serializable, NetSerializable]
public enum PhotoUiKey : byte
{
    Viewer,
    Gallery,
}

[Serializable, NetSerializable]
public enum PhotoVisualLayers : byte
{
    Base,
    Photo,
    Thumbnail,
}
