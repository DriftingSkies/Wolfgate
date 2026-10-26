using System.Numerics;
using System.Text;
using Content.Client._WF.CustomMarkings;
using Content.Client._WF.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Shared._WF.CustomMarkings;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WF.MarkingToggles.UI;

/// <summary>A checkbox for each marking on the player's body: ticked shows it, unticked hides it.</summary>
public sealed class MarkingToggleWindow : FancyWindow
{
    [Dependency] private IEntityManager _entMan = default!;

    /// <summary>How often the boxes are read back from the body, in seconds, so a refused change shows.</summary>
    private const float SyncInterval = 0.5f;

    private readonly MarkingToggleSystem _toggles;
    private readonly CustomMarkingSystem _custom;
    private readonly BoxContainer _rows;
    private readonly Label _empty;
    private readonly List<(CheckBox Box, Func<HumanoidAppearanceComponent, bool> Shown)> _boxes = new();

    /// <summary>What the rows were built from, to rebuild them when the markings change.</summary>
    private string _built = string.Empty;

    private float _sinceSync;

    /// <summary>The body whose markings are listed.</summary>
    public EntityUid Body;

    public MarkingToggleWindow()
    {
        IoCManager.InjectDependencies(this);
        _toggles = _entMan.System<MarkingToggleSystem>();
        _custom = _entMan.System<CustomMarkingSystem>();

        Title = Loc.GetString("wf-marking-toggle-window-title");
        MinSize = new Vector2(300, 200);
        SetSize = new Vector2(340, 460);

        _rows = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        _empty = new Label { Text = Loc.GetString("wf-marking-toggle-none"), Visible = false };
        ContentsContainer.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            SeparationOverride = 6,
            Children =
            {
                new Label { Text = Loc.GetString("wf-marking-toggle-hint"), StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel } },
                _empty,
                new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _rows } },
            },
        });

        _custom.LibraryUpdated += OnLibraryUpdated;
        if (_custom.Library == null)
            _custom.RequestLibrary();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _custom.LibraryUpdated -= OnLibraryUpdated;
    }

    private void OnLibraryUpdated()
    {
        _built = string.Empty;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!_entMan.TryGetComponent(Body, out HumanoidAppearanceComponent? humanoid))
            return;

        _sinceSync += args.DeltaSeconds;
        var layout = Layout(humanoid);
        if (layout != _built)
        {
            _built = layout;
            Rebuild(humanoid);
        }
        else if (_sinceSync < SyncInterval)
        {
            return;
        }

        _sinceSync = 0;
        foreach (var (box, shown) in _boxes)
        {
            box.Pressed = shown(humanoid);
        }
    }

    /// <summary>The markings a body has, as text: when it changes, the rows are made again.</summary>
    private static string Layout(HumanoidAppearanceComponent humanoid)
    {
        var layout = new StringBuilder();
        foreach (var (category, markings) in humanoid.MarkingSet.Markings)
        {
            layout.Append(category).Append(':');
            foreach (var marking in markings)
            {
                layout.Append(marking.MarkingId).Append(',');
            }
        }

        foreach (var marking in humanoid.CustomMarkings)
        {
            layout.Append(marking.Hash).Append(',');
        }

        return layout.ToString();
    }

    private void Rebuild(HumanoidAppearanceComponent humanoid)
    {
        _rows.RemoveAllChildren();
        _boxes.Clear();

        foreach (var (category, markings) in humanoid.MarkingSet.Markings)
        {
            if (markings.Count == 0)
                continue;

            AddHeading(Loc.GetString($"markings-category-{category}"));
            foreach (var marking in markings)
            {
                var id = marking.MarkingId;
                var name = Loc.TryGetString($"marking-{id}", out var named) ? named : id;
                AddBox(name,
                    shown => _toggles.SetVisible(category, id, shown),
                    body => body.MarkingSet.Markings.TryGetValue(category, out var list)
                            && list.Find(m => m.MarkingId == id) is { Visible: true });
            }
        }

        if (humanoid.CustomMarkings.Count > 0)
        {
            AddHeading(Loc.GetString("wf-marking-toggle-custom"));
            for (var i = 0; i < humanoid.CustomMarkings.Count; i++)
            {
                var marking = humanoid.CustomMarkings[i];
                AddBox(CustomName(marking, i),
                    shown => _toggles.SetVisible(marking, shown),
                    body => !body.HiddenCustomMarkings.Contains(marking));
            }
        }

        _empty.Visible = _boxes.Count == 0;
    }

    /// <summary>The library's name for a custom marking, or its place in the list when the library doesn't have it.</summary>
    private string CustomName(CustomMarking marking, int index)
    {
        if (_custom.Library is { } library)
        {
            foreach (var entry in library)
            {
                if (entry.Hash == marking.Hash)
                    return entry.Name;
            }
        }

        return Loc.GetString("wf-marking-toggle-custom-unnamed", ("number", index + 1));
    }

    private void AddHeading(string text)
    {
        _rows.AddChild(new Label
        {
            Text = text,
            Margin = new Thickness(0, _rows.ChildCount == 0 ? 0 : 6, 0, 0),
            StyleClasses = { StyleWolfgate.StyleClassCreatorHeading },
        });
    }

    private void AddBox(string text, Action<bool> toggled, Func<HumanoidAppearanceComponent, bool> shown)
    {
        var box = new CheckBox { Text = text };
        box.OnToggled += args => toggled(args.Pressed);
        _rows.AddChild(box);
        _boxes.Add((box, shown));
    }
}
