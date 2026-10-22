using Content.Shared._Common.Consent;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.CorticalBorer;

/// <summary>
/// Lets players refuse cortical borers through the consent menu.
/// </summary>
public sealed class BorerEntryConsentSystem : EntitySystem
{
    [Dependency] private SharedConsentSystem _consent = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>Consent toggle that allows borers to enter.</summary>
    public static readonly ProtoId<ConsentTogglePrototype> Toggle = "WFCorticalBorerEntry";

    /// <summary>Whether the target allows a borer in; tells the borer when it doesn't.</summary>
    public bool Allows(EntityUid borer, EntityUid target)
    {
        if (_consent.HasConsent(target, Toggle))
            return true;

        _popup.PopupEntity(Loc.GetString("wf-cortical-borer-no-consent", ("target", Identity.Entity(target, EntityManager))),
            borer, borer, PopupType.Medium);
        return false;
    }
}
