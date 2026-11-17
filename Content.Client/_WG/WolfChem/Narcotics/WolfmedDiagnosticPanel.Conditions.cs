using System.Linq;
using Content.Shared._WG.WolfChem.Narcotics;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.MedicalScanner;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WG.Wolfmed.Medical;

/// <summary>The Conditions tab: drug levels, held effects, crashes and dependence, each with what clears it.</summary>
public sealed partial class WolfmedDiagnosticPanel
{
    private static readonly Color ConditionWarning = Color.FromHex("#ff9100");
    private static readonly Color ConditionDanger = Color.FromHex("#ff4040");
    private static readonly Color ConditionHint = Color.FromHex("#a0a0a0");

    private void DrawConditions(HealthAnalyzerScannedUserMessage msg)
    {
        ConditionsContainer.RemoveAllChildren();
        if (msg.ScanMode != true || msg.Conditions is not { } conditions)
        {
            ConditionLine(Loc.GetString("wg-analyzer-conditions-unavailable"));
            return;
        }

        if (conditions.Drugs.Count == 0 && conditions.Held.Count == 0 && conditions.Crashes.Count == 0
            && conditions.Habits.Count == 0)
        {
            ConditionLine(Loc.GetString("wg-analyzer-conditions-none"));
            return;
        }

        if (conditions.Drugs.Count > 0)
        {
            ConditionHeading("wg-analyzer-drugs-title");
            foreach (var drug in conditions.Drugs)
            {
                ConditionLine(Loc.GetString("wg-analyzer-drug",
                        ("family", Capitalize(FamilyName(drug.Family))),
                        ("phase", Loc.GetString($"wg-analyzer-phase-{drug.Phase.ToString().ToLowerInvariant()}"))),
                    drug.Phase == WGDrugPhase.Overdose ? ConditionDanger : null);

                if (drug.Causes.Count == 0)
                    ConditionLine(Loc.GetString("wg-analyzer-drug-no-cause"), ConditionHint, 1);

                foreach (var cause in drug.Causes)
                {
                    ConditionCause(cause);
                }

                if (drug.Antidotes.Count > 0)
                    ConditionLine(Loc.GetString("wg-analyzer-antidote", ("reagents", ReagentNames(drug.Antidotes))), ConditionHint, 1);
            }
        }

        if (conditions.Held.Count > 0)
        {
            ConditionHeading("wg-analyzer-held-title");
            foreach (var held in conditions.Held.OrderByDescending(held => held.SecondsLeft))
            {
                var name = Loc.TryGetString($"reagent-effect-status-effect-{held.Key}", out var effect) ? effect : held.Key;
                ConditionLine(Loc.GetString("wg-analyzer-held", ("effect", Capitalize(name)), ("time", ConditionTime(held.SecondsLeft))));

                foreach (var cause in held.Causes)
                {
                    ConditionCause(cause);
                }

                foreach (var source in held.Sources)
                {
                    ConditionLine(Loc.GetString($"wg-analyzer-source-{source.Kind.ToString().ToLowerInvariant()}",
                        ("family", FamilyName(source.Family))), ConditionHint, 1);
                }

                if (held.Causes.Count == 0 && held.Sources.Count == 0)
                    ConditionLine(Loc.GetString("wg-analyzer-held-fading"), ConditionHint, 1);
            }
        }

        if (conditions.Crashes.Count > 0)
        {
            ConditionHeading("wg-analyzer-crashes-title");
            foreach (var crash in conditions.Crashes)
            {
                ConditionLine(Loc.GetString("wg-analyzer-crash", ("family", FamilyName(crash.Family)),
                    ("time", ConditionTime(crash.SecondsLeft))), ConditionWarning);
                ConditionLine(Loc.GetString("wg-analyzer-crash-hint"), ConditionHint, 1);
            }
        }

        if (conditions.Habits.Count > 0)
        {
            ConditionHeading("wg-analyzer-habits-title");
            foreach (var habit in conditions.Habits)
            {
                var family = FamilyName(habit.Family);
                ConditionLine(Loc.GetString("wg-analyzer-habit", ("family", family), ("meter", Percent(habit.Meter))),
                    habit.Stage > 0 ? ConditionWarning : null);

                if (habit.Stage > 0)
                {
                    ConditionLine(Loc.GetString("wg-analyzer-withdrawal", ("stage", habit.Stage), ("family", family)),
                        ConditionDanger, 1);
                }
                else if (habit.WithdrawalIn >= 0f)
                {
                    ConditionLine(Loc.GetString("wg-analyzer-withdrawal-in", ("time", ConditionTime(habit.WithdrawalIn)),
                        ("family", family)), ConditionWarning, 1);
                }

                if (float.IsPositiveInfinity(habit.CleanIn))
                    ConditionLine(Loc.GetString("wg-analyzer-clean-never", ("floor", Percent(habit.Floor))), ConditionHint, 1);
                else if (habit.CleanIn >= 0f)
                    ConditionLine(Loc.GetString("wg-analyzer-clean-in", ("time", ConditionTime(habit.CleanIn))), ConditionHint, 1);

                if (habit.Tolerance > 0.01f)
                {
                    ConditionLine(Loc.GetString("wg-analyzer-tolerance", ("tolerance", Percent(habit.Tolerance)),
                        ("weaker", Percent(1f - 1f / (1f + habit.Tolerance)))), ConditionHint, 1);
                }
            }
        }
    }

    /// <summary>A reagent behind an effect, how long it takes to clear and what removes it faster.</summary>
    private void ConditionCause(WGAnalyzerCause cause)
    {
        var name = ReagentNames(new List<string> { cause.Reagent });
        var text = cause.Stomach
            ? Loc.GetString("wg-analyzer-cause-stomach", ("reagent", name), ("quantity", cause.Quantity))
            : cause.ClearSeconds >= 0f
                ? Loc.GetString("wg-analyzer-cause", ("reagent", name), ("quantity", cause.Quantity),
                    ("time", ConditionTime(cause.ClearSeconds)))
                : Loc.GetString("wg-analyzer-cause-stalled", ("reagent", name), ("quantity", cause.Quantity));

        ConditionLine(text, null, 1);
        if (cause.RemovedBy.Count > 0)
            ConditionLine(Loc.GetString("wg-analyzer-removed-by", ("reagents", ReagentNames(cause.RemovedBy))), ConditionHint, 2);
    }

    private void ConditionHeading(string loc)
    {
        ConditionsContainer.AddChild(new Label
        {
            Text = Loc.GetString(loc),
            StyleClasses = { "LabelHeading" },
            Margin = new Thickness(0, ConditionsContainer.ChildCount > 0 ? 6 : 0, 0, 0),
        });
        ConditionsContainer.AddChild(new PanelContainer { StyleClasses = { "LowDivider" }, Margin = new Thickness(0, 2, 0, 4) });
    }

    /// <summary>A wrapping line, indented by <paramref name="indent"/> steps.</summary>
    private void ConditionLine(string text, Color? color = null, int indent = 0)
    {
        var message = new FormattedMessage();
        if (color is { } tint)
            message.PushColor(tint);
        message.AddText(text);
        if (color != null)
            message.Pop();

        var label = new RichTextLabel
        {
            HorizontalExpand = true,
            Margin = new Thickness(indent * 12, 0, 0, 0),
        };
        label.SetMessage(message);
        ConditionsContainer.AddChild(label);
    }

    private string ReagentNames(List<string> reagents)
    {
        return string.Join(", ", reagents.Select(id =>
            _prototypes.TryIndex(id, out ReagentPrototype? reagent) ? reagent.LocalizedName : id));
    }

    private static string FamilyName(WGDrugFamily family)
    {
        return Loc.GetString($"wg-drug-family-{family.ToString().ToLowerInvariant()}");
    }

    private static int Percent(float share)
    {
        return (int) MathF.Round(share * 100f);
    }

    /// <summary>Seconds as m:ss.</summary>
    private static string ConditionTime(float seconds)
    {
        var total = (int) MathF.Ceiling(Math.Max(0f, seconds));
        return Loc.GetString("wg-analyzer-time", ("minutes", total / 60), ("seconds", (total % 60).ToString("00")));
    }
}
