using Content.Shared._WF.EventStaff;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.EventStaff;

/// <summary>
/// Picks whether a player's ahelp is a gameplay issue or a player issue.
/// </summary>
public sealed class AHelpCategorySelector : BoxContainer
{
    private readonly OptionButton _options;

    /// <summary>
    /// The category the player picked.
    /// </summary>
    public AHelpCategory Category => (AHelpCategory) _options.SelectedId;

    public AHelpCategorySelector()
    {
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 6;
        Margin = new Thickness(0, 0, 0, 4);

        AddChild(new Label { Text = Loc.GetString("wf-bwoink-category-label") });

        _options = new OptionButton
        {
            HorizontalExpand = true,
            ToolTip = Loc.GetString("wf-bwoink-category-hint"),
        };
        _options.AddItem(Loc.GetString("wf-bwoink-category-option-player"), (int) AHelpCategory.Player);
        _options.AddItem(Loc.GetString("wf-bwoink-category-option-gameplay"), (int) AHelpCategory.Gameplay);
        _options.OnItemSelected += args => _options.SelectId(args.Id);
        AddChild(_options);
    }
}
