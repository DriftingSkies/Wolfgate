using System.Numerics;
using Content.Shared._WG.Cooking;
using Content.Shared.Chemistry.EntitySystems;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._WG.Cooking;

/// <summary>Opens the stirring window over a pot on the hob and sends the stir's score to the server.</summary>
public sealed class WGStirBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private DefaultWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<DefaultWindow>();
        _window.Title = Loc.GetString("wg-stir-title");
        _window.MinSize = new Vector2(320, 380);

        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(6), SeparationOverride = 4 };
        box.AddChild(new Label { Text = Loc.GetString("wg-stir-hint"), StyleClasses = { "LabelSubText" } });
        var game = new WGStirMinigame { HorizontalExpand = true, VerticalExpand = true };

        // Tint the soup the colour of what is in the pot on the hearth's hob.
        var pot = Owner;
        if (EntMan.System<Robust.Shared.Containers.SharedContainerSystem>().TryGetContainer(Owner, "wg-hob", out var hob) && hob.ContainedEntities.Count > 0)
            pot = hob.ContainedEntities[0];
        if (EntMan.TrySystem<SharedSolutionContainerSystem>(out var solutions) && solutions.TryGetSolution(pot, "pot", out _, out var soup) && soup.Volume > 0)
            game.Soup = soup.GetColor(IoCManager.Resolve<Robust.Shared.Prototypes.IPrototypeManager>());

        game.OnFinished += score =>
        {
            SendMessage(new WGStirMessage(score));
            Close();
        };
        box.AddChild(game);
        game.Start();
        _window.Contents.AddChild(box);
    }
}
