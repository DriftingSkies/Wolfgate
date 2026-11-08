using Content.Server.Body.Components;
using Content.Shared._WF.MeridianTraits.Hemophage;
using Content.Shared.Jittering;
using Content.Shared.Popups;
using Content.Shared.Speech.EntitySystems;
using Robust.Shared.Random;

namespace Content.Server._WF.MeridianTraits.Hemophage;

/// <summary>
/// A stand-in for Meridian's disgust, limited to what the tumor finds vile: animal blood and food it rejects.
/// Nausea fades on its own; while it lasts it brings stutters and shakes.
/// </summary>
public sealed partial class WFHemophageSystem
{
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private SharedStutteringSystem _stutter = default!;

    /// <summary>Raises nausea to at least <paramref name="level"/>, telling the hemophage when it first turns.</summary>
    public void Sicken(Entity<WFHemophageComponent> ent, float level)
    {
        if (ent.Comp.Nausea < ent.Comp.NauseaGross && level >= ent.Comp.NauseaGross)
            _popup.PopupEntity(Loc.GetString("wf-hemophage-nausea"), ent, ent, PopupType.MediumCaution);

        ent.Comp.Nausea = MathF.Max(ent.Comp.Nausea, level);
    }

    private void TickNausea(Entity<WFHemophageComponent, BloodstreamComponent> ent, float seconds)
    {
        var (uid, comp, _) = ent;
        if (comp.Nausea <= 0f)
            return;

        var interval = TimeSpan.FromSeconds(seconds * 2);
        if (comp.Nausea >= comp.NauseaGross)
        {
            if (_random.Prob(0.1f * seconds))
                _stutter.DoStutter(uid, interval, true);
            if (_random.Prob(0.1f * seconds))
                _jitter.DoJitter(uid, interval, true);
        }

        comp.Nausea = MathF.Max(0f, comp.Nausea - comp.NauseaDecayPerSecond * seconds);
    }
}
