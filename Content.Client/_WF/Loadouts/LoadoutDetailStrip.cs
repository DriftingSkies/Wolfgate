using System.Linq;
using Content.Client._WF.Stylesheets;
using Content.Client.Stylesheets;
using Content.Shared._NF.Bank;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WF.Loadouts;

/// <summary>
/// Fixed-height strip under the tile grid. It describes the item under the pointer: name, price, why it is
/// locked or can't be paid for, and its description. With nothing hovered it shows what the open group holds:
/// a single pick is described the same way, several picks share the strip as cards. The height never changes,
/// so the grid above doesn't jump.
/// </summary>
public sealed class LoadoutDetailStrip : PanelContainer
{
    public const float StripHeight = 102;

    /// <summary>Cards kept on one row before a second row is started.</summary>
    private const int CardsPerRow = 3;

    private readonly LoadoutIconCache _icons;

    private readonly BoxContainer _entryBox;
    private readonly LoadoutIconView _icon;
    private readonly Label _name;
    private readonly Label _status;
    private readonly Label _price;
    private readonly Button _remove;
    private readonly RichTextLabel _text;
    private LoadoutEntry? _described;

    private readonly Label _message;
    private readonly GridContainer _cards;
    private readonly List<LoadoutIconView> _cardIcons = new();
    private string? _shown;

    /// <summary>Raised when a pick's remove button is pressed.</summary>
    public event Action<LoadoutEntry>? OnRemove;

    public LoadoutDetailStrip(LoadoutIconCache icons)
    {
        _icons = icons;
        AddStyleClass(StyleWolfgate.StyleClassLoadoutInset);
        SetHeight = StripHeight;
        RectClipContent = true;

        _icon = new LoadoutIconView(icons, 64, 2) { VerticalAlignment = VAlignment.Top };
        _name = new Label
        {
            ClipText = true,
            HorizontalExpand = true,
            StyleClasses = { StyleWolfgate.StyleClassCreatorCardTitle },
        };
        _status = new Label { StyleClasses = { StyleWolfgate.StyleClassLoadoutChip } };
        _price = new Label();
        _remove = RemoveButton();
        _remove.OnPressed += _ =>
        {
            if (_described != null)
                OnRemove?.Invoke(_described);
        };
        _text = new RichTextLabel { HorizontalExpand = true, VerticalAlignment = VAlignment.Top };

        _entryBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            Visible = false,
            Children =
            {
                _icon,
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    HorizontalExpand = true,
                    SeparationOverride = 2,
                    Children =
                    {
                        new BoxContainer
                        {
                            Orientation = BoxContainer.LayoutOrientation.Horizontal,
                            SeparationOverride = 8,
                            Children = { _name, _status, _price, _remove },
                        },
                        _text,
                    },
                },
            },
        };

        _message = new Label
        {
            Align = Label.AlignMode.Center,
            VerticalAlignment = VAlignment.Center,
            StyleClasses = { StyleBase.StyleClassLabelSubText },
        };

        // Cards stretch, so however many picks there are, together they fill the strip.
        _cards = new GridContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HSeparationOverride = 4,
            VSeparationOverride = 4,
            Visible = false,
        };

        AddChild(_entryBox);
        AddChild(_message);
        AddChild(_cards);
    }

    /// <summary>Describes the loadout under the pointer.</summary>
    public void ShowEntry(LoadoutEntry entry)
    {
        Describe(entry, false);
    }

    /// <summary>Shows what a group holds, or a plain message when there is no group or nothing is picked.</summary>
    /// <param name="canRemove">Whether a pick may be dropped; a required pick can only be replaced.</param>
    public void ShowPicks(LoadoutGroupView? group, string? message, Func<LoadoutEntry, bool> canRemove)
    {
        if (group is { Picks.Count: 1 })
        {
            Describe(group.Picks[0], canRemove(group.Picks[0]));
            return;
        }

        _described = null;
        _icon.SetPrototype(null);
        _entryBox.Visible = false;

        var picks = group?.Picks.Count ?? 0;
        _message.Visible = picks == 0;
        _cards.Visible = picks > 0;

        if (picks == 0)
        {
            _message.Text = message ?? Loc.GetString("wf-loadout-detail-none");
            return;
        }

        // Called on every refresh; the cards are only remade when what they show changed.
        var shown = string.Join(' ', group!.Picks.Select(p => $"{p.Id}:{canRemove(p)}:{p.Unaffordable}").Prepend(group.Id.Id));
        if (shown == _shown)
            return;

        _shown = shown;
        ClearCards();

        // One row while the cards stay wide enough to read, two beyond that.
        var rows = picks <= CardsPerRow ? 1 : 2;
        _cards.Columns = (picks + rows - 1) / rows;

        foreach (var pick in group.Picks)
        {
            _cards.AddChild(Card(pick, canRemove(pick), rows == 1));
        }
    }

    private void Describe(LoadoutEntry entry, bool removable)
    {
        _described = entry;
        _entryBox.Visible = true;
        _message.Visible = false;
        _cards.Visible = false;

        _icon.SetPrototype(entry.Icon);
        _name.Text = entry.Name;
        _price.Text = entry.Price > 0
            ? BankSystemExtensions.ToSpesoString(entry.Price)
            : Loc.GetString("wf-loadout-free");
        _remove.Visible = removable;
        _remove.ToolTip = Loc.GetString("wf-loadout-detail-remove", ("name", entry.Name));

        var message = new FormattedMessage();

        if (entry.Locked)
        {
            SetStatus(Loc.GetString("wf-loadout-detail-locked"), LoadoutTone.Danger);
            if (entry.Reason != null)
                message.AddMessage(entry.Reason);
        }
        else if (entry.Unaffordable)
        {
            SetStatus(Loc.GetString("wf-loadout-detail-unaffordable"), LoadoutTone.Danger);
            message.AddText(entry.Group.Replacement is { } replacement
                ? Loc.GetString("wf-loadout-dropped-fallback", ("item", replacement))
                : Loc.GetString("wf-loadout-dropped"));
        }
        else if (entry.Selected)
        {
            SetStatus(Loc.GetString("wf-loadout-detail-selected"), LoadoutTone.Good);
        }
        else
        {
            SetStatus(string.Empty, LoadoutTone.Normal);
        }

        if (entry.Description.Length > 0)
        {
            if (!message.IsEmpty)
                message.PushNewline();

            message.AddText(entry.Description);
        }

        _text.SetMessage(message);
    }

    /// <summary>One pick among several: icon, name, price and a button to drop it.</summary>
    /// <param name="roomy">The card has the strip's full height, enough for a large icon and a wrapped name.</param>
    private Control Card(LoadoutEntry pick, bool removable, bool roomy)
    {
        var icon = new LoadoutIconView(_icons, roomy ? 64 : 32, roomy ? 2 : 1) { VerticalAlignment = VAlignment.Center };
        icon.SetPrototype(pick.Icon);
        _cardIcons.Add(icon);

        var text = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        };

        if (roomy)
        {
            var name = new RichTextLabel { VerticalAlignment = VAlignment.Top };
            name.SetMessage(FormattedMessage.FromUnformatted(pick.Name));
            text.AddChild(name);
        }
        else
        {
            text.AddChild(new Label { Text = pick.Name, ClipText = true });
        }

        if (pick.Price > 0)
        {
            var price = new Label
            {
                Text = BankSystemExtensions.ToSpesoString(pick.Price),
                StyleClasses = { StyleWolfgate.StyleClassLoadoutChip },
            };
            LoadoutTones.Set(price, pick.Unaffordable ? LoadoutTone.Danger : LoadoutTone.Muted);
            text.AddChild(price);
        }

        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            Children = { icon, text },
        };

        if (removable)
        {
            var remove = RemoveButton();
            remove.ToolTip = Loc.GetString("wf-loadout-detail-remove", ("name", pick.Name));
            remove.OnPressed += _ => OnRemove?.Invoke(pick);
            content.AddChild(remove);
        }

        return new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            RectClipContent = true,
            MouseFilter = MouseFilterMode.Pass,
            ToolTip = pick.Name,
            StyleClasses = { StyleWolfgate.StyleClassLoadoutPick },
            Children = { content },
        };
    }

    private void ClearCards()
    {
        foreach (var icon in _cardIcons)
        {
            icon.SetPrototype(null);
        }

        _cardIcons.Clear();
        _cards.DisposeAllChildren();
    }

    private static Button RemoveButton()
    {
        return new Button
        {
            Text = Loc.GetString("wf-loadout-detail-remove-glyph"),
            VerticalAlignment = VAlignment.Center,
        };
    }

    private void SetStatus(string text, LoadoutTone tone)
    {
        _status.Text = text;
        _status.Visible = text.Length > 0;
        LoadoutTones.Set(_status, tone);
    }
}
