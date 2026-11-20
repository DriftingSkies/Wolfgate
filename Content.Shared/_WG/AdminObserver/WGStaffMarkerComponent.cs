using Content.Shared.StatusIcon;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.AdminObserver;

/// <summary>
/// Marks an entity players can see as a member of staff: a line on examine, a tag on its name in chat, and at
/// least one visual. The server fills every field; nothing here is the client's to choose.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WGStaffMarkerComponent : Component
{
    /// <summary>What everyone without the Host flag is marked in.</summary>
    public static readonly Color StaffColor = Color.FromHex("#b3357d");

    /// <summary>What a holder of the Host flag is marked in until they choose a colour.</summary>
    public static readonly Color HostColor = Color.FromHex("#fe019a");

    /// <summary>The staff wording: a rank, "Staff", an account name, or rank and name.</summary>
    [DataField, AutoNetworkedField]
    public string Label = string.Empty;

    /// <summary>Which visuals are shown. Never <see cref="WGMarkerVisuals.None"/>.</summary>
    [DataField, AutoNetworkedField]
    public WGMarkerVisuals Visuals = WGMarkerVisuals.Nameplate;

    /// <summary>The colour of every coloured visual.</summary>
    [DataField, AutoNetworkedField]
    public Color Color = StaffColor;

    /// <summary>Whether the marked admin holds the Host flag, which changes the sigil.</summary>
    [DataField, AutoNetworkedField]
    public bool Host;

    /// <summary>The look of each visual that has several, as a <see cref="WGStaffMarkerVariantPrototype"/> ID.</summary>
    [DataField, AutoNetworkedField]
    public string Ring = string.Empty;

    /// <inheritdoc cref="Ring"/>
    [DataField, AutoNetworkedField]
    public string Glyph = string.Empty;

    /// <inheritdoc cref="Ring"/>
    [DataField, AutoNetworkedField]
    public string Shield = string.Empty;

    /// <inheritdoc cref="Ring"/>
    [DataField, AutoNetworkedField]
    public string Aura = string.Empty;

    /// <summary>The sigil beside the head of a member of staff.</summary>
    [DataField]
    public ProtoId<SsdIconPrototype> AdminSigil = "WGStaffSigilAdmin";

    /// <summary>The sigil beside the head of a holder of the Host flag.</summary>
    [DataField]
    public ProtoId<SsdIconPrototype> HostSigil = "WGStaffSigilHost";
}
