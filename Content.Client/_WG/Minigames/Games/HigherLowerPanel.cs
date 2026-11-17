using Content.Shared._WG.Minigames;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.Minigames.Games;

/// <summary>Higher or Lower: the face-up card, the run, and higher, lower and cash out.</summary>
public sealed class HigherLowerPanel : MinigamePanel
{
    private readonly Label _card = new() { HorizontalAlignment = HAlignment.Center };
    private readonly Label _run = new() { HorizontalAlignment = HAlignment.Center };
    private readonly Button _higher;
    private readonly Button _lower;
    private readonly Button _cashOut;

    public HigherLowerPanel()
    {
        AddChild(new Label { Text = Loc.GetString("wg-minigame-cards-rules"), HorizontalAlignment = HAlignment.Center });
        _card.StyleClasses.Add("LabelHeading");
        AddChild(_card);
        AddChild(_run);

        var row = Row();
        _higher = MakeButton(Loc.GetString("wg-minigame-cards-higher"), () => Move(HigherLowerGame.Higher));
        _lower = MakeButton(Loc.GetString("wg-minigame-cards-lower"), () => Move(HigherLowerGame.Lower));
        _cashOut = MakeButton(Loc.GetString("wg-minigame-cards-cash-out"), () => Move(HigherLowerGame.CashOut));
        row.AddChild(_higher);
        row.AddChild(_lower);
        row.AddChild(_cashOut);
        AddChild(row);
    }

    public override void UpdateState(MinigameState state)
    {
        if (state.View is not HigherLowerView view)
            return;

        _card.Text = Loc.GetString("wg-minigame-cards-card",
            ("rank", Loc.GetString($"wg-minigame-cards-rank-{view.Card}")),
            ("suit", Loc.GetString($"wg-minigame-cards-suit-{view.Suit}")));
        _run.Text = view.NextMultiplier > 0
            ? Loc.GetString("wg-minigame-cards-run", ("streak", view.Streak), ("max", view.MaxStreak), ("next", view.NextMultiplier))
            : Loc.GetString("wg-minigame-cards-run-top", ("streak", view.Streak));

        var playing = state.Status == MinigameStatus.Playing;
        _higher.Disabled = !playing;
        _lower.Disabled = !playing;
        _cashOut.Disabled = !playing || view.Streak == 0;
    }
}
