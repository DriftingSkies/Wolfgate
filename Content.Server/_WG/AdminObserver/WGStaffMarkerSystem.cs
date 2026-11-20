using Content.Server.Administration.Managers;
using Content.Shared._WG.AdminObserver;
using Content.Shared.Administration;
using Content.Shared.Chat;
using Content.Shared.Examine;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._WG.AdminObserver;

/// <summary>
/// The staff marker's fixed part: whoever carries <see cref="WGStaffMarkerComponent"/> says so on examine and in
/// front of their name in chat. Also fills the marker from an admin's saved choices and their flags.
/// </summary>
public sealed partial class WGStaffMarkerSystem : EntitySystem
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    /// <summary>The visuals the client can draw. A marker with none of them gets the nameplate.</summary>
    public const WGMarkerVisuals Supported = WGMarkerVisuals.Nameplate | WGMarkerVisuals.Sigil | WGMarkerVisuals.Aura
                                             | WGMarkerVisuals.Shield | WGMarkerVisuals.Hologram
                                             | WGMarkerVisuals.Glyph | WGMarkerVisuals.Ring;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGStaffMarkerComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WGStaffMarkerComponent, TransformSpeakerNameEvent>(OnSpeakerName);
    }

    private void OnExamined(Entity<WGStaffMarkerComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wg-staff-marker-examine",
            ("ent", ent.Owner),
            ("label", FormattedMessage.EscapeText(ent.Comp.Label))));
    }

    private void OnSpeakerName(Entity<WGStaffMarkerComponent> ent, ref TransformSpeakerNameEvent args)
    {
        args.VoiceName = Loc.GetString("wg-staff-marker-name", ("label", ent.Comp.Label), ("name", args.VoiceName));
    }

    /// <summary>The visuals drawn on the avatar itself. A nameplate and a sigil sit beside it.</summary>
    public const WGMarkerVisuals OnAvatar = WGMarkerVisuals.Aura | WGMarkerVisuals.Shield | WGMarkerVisuals.Hologram
                                            | WGMarkerVisuals.Glyph | WGMarkerVisuals.Ring;

    /// <summary>What an object, mob or character avatar gets when the admin chose nothing drawn on the avatar itself.</summary>
    public const WGMarkerVisuals ObjectDefault = WGMarkerVisuals.Hologram | WGMarkerVisuals.Ring;

    /// <summary>Marks an entity as this admin, by their saved choices, rank and flags.</summary>
    public WGStaffMarkerComponent Mark(
        EntityUid uid,
        ICommonSession admin,
        WGStaffMarkerSettings settings,
        WGAvatarKind kind = WGAvatarKind.Ghost)
    {
        return Mark(uid, settings, _admin.GetAdminData(admin)?.Title, admin.Name, _admin.HasAdminFlag(admin, AdminFlags.Host), kind);
    }

    /// <summary>
    /// Marks an entity as staff. The colour and sigil follow <paramref name="host"/>, not the saved settings: only
    /// a holder of the Host flag keeps a colour of their own.
    /// </summary>
    public WGStaffMarkerComponent Mark(
        EntityUid uid,
        WGStaffMarkerSettings settings,
        string? rank,
        string account,
        bool host,
        WGAvatarKind kind = WGAvatarKind.Ghost)
    {
        var marker = EnsureComp<WGStaffMarkerComponent>(uid);
        marker.Label = Label(settings.Wording, rank, account);
        marker.Visuals = VisualsFor(kind, settings.Visuals);
        marker.Host = host;
        marker.Color = MarkerColor(settings.Color, host);
        marker.Ring = Variant(WGMarkerVisuals.Ring, settings.Ring);
        marker.Glyph = Variant(WGMarkerVisuals.Glyph, settings.Glyph);
        marker.Shield = Variant(WGMarkerVisuals.Shield, settings.Shield);
        marker.Aura = Variant(WGMarkerVisuals.Aura, settings.Aura);
        Dirty(uid, marker);
        return marker;
    }

    /// <summary>
    /// The visuals an avatar is marked with: the chosen ones, never none. An object, mob or character could pass for
    /// the real thing with a plate or sigil beside it and nothing else, so it also gets <see cref="ObjectDefault"/>
    /// unless the admin chose something drawn on the avatar.
    /// </summary>
    public static WGMarkerVisuals VisualsFor(WGAvatarKind kind, WGMarkerVisuals chosen)
    {
        var visuals = chosen & Supported;
        if (kind is WGAvatarKind.Object or WGAvatarKind.Mob or WGAvatarKind.Character && (visuals & OnAvatar) == 0)
            visuals |= ObjectDefault;

        return visuals == WGMarkerVisuals.None ? WGMarkerVisuals.Nameplate : visuals;
    }

    /// <summary>The look a visual takes: the chosen one if it exists and is a look for that visual, else the default.</summary>
    public string Variant(WGMarkerVisuals visual, string chosen)
    {
        if (_prototypes.TryIndex<WGStaffMarkerVariantPrototype>(chosen, out var variant) && variant.Visual == visual)
            return variant.ID;

        foreach (var candidate in _prototypes.EnumeratePrototypes<WGStaffMarkerVariantPrototype>())
        {
            if (candidate.Visual == visual && candidate.Default)
                return candidate.ID;
        }

        return string.Empty;
    }

    /// <summary>The staff wording for an admin. A missing rank reads as "Staff".</summary>
    public string Label(WGMarkerWording wording, string? rank, string account)
    {
        var staff = Loc.GetString("wg-staff-marker-label-staff");
        rank = string.IsNullOrWhiteSpace(rank) ? staff : rank;

        return wording switch
        {
            WGMarkerWording.Staff => staff,
            WGMarkerWording.Name => account,
            WGMarkerWording.RankAndName => Loc.GetString("wg-staff-marker-label-rank-and-name", ("rank", rank), ("name", account)),
            _ => rank,
        };
    }

    /// <summary>
    /// The marker colour: fixed for staff, the saved one for a holder of the Host flag unless it is fully
    /// transparent, which would hide the marker.
    /// </summary>
    public static Color MarkerColor(string? saved, bool host)
    {
        if (!host)
            return WGStaffMarkerComponent.StaffColor;

        return saved != null && Color.TryFromHex(saved, out var color) && color.A > 0f ? color : WGStaffMarkerComponent.HostColor;
    }
}
