using System.Linq;
using System.Text;
using Content.Shared.Damage;
using Content.Shared.Ghost;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Localizations;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Revenant.Components;

namespace Content.Server._WF.Photography;

/// <summary>Writes a photo's description from what the server sees in it.</summary>
public sealed partial class PhotoCaptionSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;

    /// <summary>A mob counts as hurt in a photo past this share of its critical damage.</summary>
    private const float HurtFraction = 0.25f;

    /// <summary>What a photo caught: its description, and the ghosts and revenants in it.</summary>
    public sealed record Caption(string Description, bool Haunted, List<EntityUid> Revenants);

    /// <summary>Describes the mobs among <paramref name="seen"/> for a photo of a square of the given size.</summary>
    public Caption Describe(IEnumerable<EntityUid> seen, int tiles, bool seeGhosts)
    {
        var text = new StringBuilder(Loc.GetString("wf-photo-caption-area", ("size", tiles)));
        var haunted = false;
        var revenants = new List<EntityUid>();

        foreach (var uid in seen)
        {
            if (HasComp<RevenantComponent>(uid))
            {
                if (!seeGhosts)
                    continue;

                revenants.Add(uid);
                haunted = true;
                text.Append(' ').Append(Loc.GetString("wf-photo-caption-ghost"));
                continue;
            }

            if (HasComp<GhostComponent>(uid))
            {
                if (!seeGhosts)
                    continue;

                haunted = true;
                text.Append(' ').Append(Loc.GetString("wf-photo-caption-ghost"));
                continue;
            }

            if (!HasComp<MobStateComponent>(uid))
                continue;

            text.Append(' ').Append(DescribeMob(uid));
        }

        return new Caption(text.ToString(), haunted, revenants);
    }

    private string DescribeMob(EntityUid uid)
    {
        var name = Identity.Name(uid, EntityManager);
        var line = Loc.GetString(IsHurt(uid) ? "wf-photo-caption-mob-hurt" : "wf-photo-caption-mob", ("name", name));

        var held = _hands.EnumerateHeld(uid)
            .Select(item => Loc.GetString("wf-photo-caption-item", ("item", item)))
            .ToList();
        if (held.Count == 0)
            return line;

        var holding = Loc.GetString("wf-photo-caption-holding",
            ("mob", Identity.Entity(uid, EntityManager)),
            ("items", ContentLocalizationManager.FormatList(held)));
        return $"{line} {holding}";
    }

    private bool IsHurt(EntityUid uid)
    {
        if (!TryComp<DamageableComponent>(uid, out var damage)
            || !_thresholds.TryGetThresholdForState(uid, MobState.Critical, out var critical)
            || critical is not { } threshold
            || threshold <= 0)
            return false;

        return damage.TotalDamage > threshold * HurtFraction;
    }
}
