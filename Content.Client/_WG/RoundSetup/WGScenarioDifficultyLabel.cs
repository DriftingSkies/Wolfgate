using Content.Shared._WG.RoundSetup;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.RoundSetup;

/// <summary>Colour-coded scenario difficulty for cards, details and crew rows.</summary>
public static class WGScenarioDifficultyLabel
{
    /// <summary>Blue to red, easiest first.</summary>
    public static Color Color(WGScenarioDifficulty difficulty) => difficulty switch
    {
        WGScenarioDifficulty.Easy => Robust.Shared.Maths.Color.FromHex("#4fb3ff"),
        WGScenarioDifficulty.Normal => Robust.Shared.Maths.Color.FromHex("#6fd36f"),
        WGScenarioDifficulty.Hard => Robust.Shared.Maths.Color.FromHex("#ffc83d"),
        WGScenarioDifficulty.VeryHard => Robust.Shared.Maths.Color.FromHex("#ff7a2f"),
        _ => Robust.Shared.Maths.Color.FromHex("#e02424"),
    };

    /// <summary>The level's name alone, such as "Very Hard".</summary>
    public static string Name(WGScenarioDifficulty difficulty) =>
        Loc.GetString("wg-round-setup-difficulty-" + difficulty.ToString().ToLowerInvariant());

    /// <summary>"Difficulty: Normal", coloured by level.</summary>
    public static Label Create(WGRoundScenario scenario)
    {
        var difficulty = WGRoundSetupPolicy.Difficulty(scenario);
        return new Label
        {
            Text = Loc.GetString("wg-round-setup-difficulty", ("level", Name(difficulty))),
            FontColorOverride = Color(difficulty),
        };
    }
}
