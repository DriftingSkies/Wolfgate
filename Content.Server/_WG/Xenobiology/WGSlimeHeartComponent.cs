using Content.Shared.FixedPoint;
using Content.Shared.Tag;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WG.Xenobiology;

/// <summary>
/// A living ship fitting grown from a slime extract. Bolted down and fed, it regrows the damaged parts of its hull.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WGSlimeHeartComponent : Component
{
    /// <summary>Extracts with this tag feed the heart.</summary>
    [DataField(required: true)]
    public ProtoId<TagPrototype> Food;

    /// <summary>The heart beats until this time and is dormant after it.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan FedUntil;

    /// <summary>How long a newly grown heart beats before its first feeding; none, so it starts dormant.</summary>
    [DataField]
    public TimeSpan StartingFeed = TimeSpan.Zero;

    [DataField]
    public TimeSpan FeedPerExtract = TimeSpan.FromMinutes(20);

    /// <summary>The most feeding a heart holds; an extract that would go past it is refused.</summary>
    [DataField]
    public TimeSpan MaxFeed = TimeSpan.FromMinutes(60);

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(5);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextUpdate;

    /// <summary>A part regrows only once it has gone this long without taking damage.</summary>
    [DataField]
    public TimeSpan QuietDelay = TimeSpan.FromSeconds(30);

    /// <summary>Share of a hull part's full integrity regrown each <see cref="Interval"/>.</summary>
    [DataField]
    public float RegrowFraction = 0.01f;

    /// <summary>Light energy while it beats, and while it is regrowing the hull.</summary>
    [DataField]
    public float Glow = 1.5f;

    [DataField]
    public float MendingGlow = 3f;

    /// <summary>Played on each hull part as it regrows.</summary>
    [DataField]
    public EntProtoId? RegrowEffect;

    [DataField]
    public SoundSpecifier FeedSound = new SoundPathSpecifier("/Audio/Voice/Talk/slime.ogg");

    /// <summary>Damaged parts on the hull: their damage at the last look and when that damage last rose.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, (FixedPoint2 Damage, TimeSpan Since)> Wounds = new();
}
