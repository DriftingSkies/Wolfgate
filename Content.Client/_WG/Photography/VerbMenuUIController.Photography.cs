using Content.Client.ContextMenu.UI;
using Content.Shared.Verbs;

namespace Content.Client.Verbs.UI;

public sealed partial class VerbMenuUIController
{
    /// <summary>The top category a nested category sits under.</summary>
    private static VerbCategory RootCategory(VerbCategory category)
    {
        while (category.Parent != null)
        {
            category = category.Parent;
        }

        return category;
    }

    /// <summary>Whether a category or any category under it has verbs.</summary>
    private bool HasVerbsUnder(VerbCategory category)
    {
        foreach (var verb in CurrentVerbs)
        {
            for (var current = verb.Category; current != null; current = current.Parent)
            {
                if (current.Text == category.Text)
                    return true;
            }
        }

        return false;
    }

    /// <summary>Adds the categories directly under a category to its submenu.</summary>
    private void AddChildCategories(VerbCategory parent, ContextMenuPopup submenu)
    {
        var added = new HashSet<string>();
        foreach (var verb in CurrentVerbs)
        {
            for (var current = verb.Category; current?.Parent != null; current = current.Parent)
            {
                if (current.Parent.Text == parent.Text && added.Add(current.Text))
                    AddVerbCategory(current, submenu);
            }
        }
    }
}
