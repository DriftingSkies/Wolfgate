using System.Linq;
using Content.Client._WG.Stylesheets;
using Content.Shared._WG.CombatMusic;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.CombatMusic;

/// <summary>Creator card listing every combat track, each with a checkbox for the playlist and a Listen button.</summary>
public sealed partial class WGCombatMusicPicker : PanelContainer
{
    /// <summary>The playlist changed, in editor order.</summary>
    public event Action<List<ProtoId<WGCombatTrackPrototype>>>? OnChanged;

    /// <summary>Listen was pressed: the track to preview, or null to stop.</summary>
    public event Action<WGCombatTrackPrototype?>? OnPreview;

    private readonly List<(WGCombatTrackPrototype Track, CheckBox Box, Button Listen)> _rows = new();
    private readonly Label _summary;
    private ProtoId<WGCombatTrackPrototype>? _previewing;

    public WGCombatMusicPicker(IPrototypeManager proto)
    {
        StyleClasses.Add(StyleWolfgate.StyleClassCreatorCard);
        HorizontalExpand = true;

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
        };
        AddChild(box);

        box.AddChild(new Label
        {
            Text = Loc.GetString("wg-combat-music-heading"),
            StyleClasses = { StyleWolfgate.StyleClassCreatorHeading },
        });
        box.AddChild(new RichTextLabel { Text = Loc.GetString("wg-combat-music-help") });

        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        var all = new Button { Text = Loc.GetString("wg-combat-music-all") };
        all.OnPressed += _ => SetAll(true);
        var none = new Button { Text = Loc.GetString("wg-combat-music-none") };
        none.OnPressed += _ => SetAll(false);
        _summary = new Label { HorizontalExpand = true, Align = Label.AlignMode.Right };
        buttons.AddChild(all);
        buttons.AddChild(none);
        buttons.AddChild(_summary);
        box.AddChild(buttons);

        foreach (var track in WGCombatMusicRules.Ordered(proto))
        {
            box.AddChild(MakeRow(track));
        }

        UpdateSummary();
    }

    /// <summary>Ticks the tracks in the playlist.</summary>
    public void SetPlaylist(IReadOnlyCollection<ProtoId<WGCombatTrackPrototype>> tracks)
    {
        foreach (var (track, box, _) in _rows)
        {
            box.Pressed = tracks.Contains(track.ID);
        }

        UpdateSummary();
    }

    /// <summary>Shows which track, if any, is playing as a preview.</summary>
    public void SetPreviewing(ProtoId<WGCombatTrackPrototype>? track)
    {
        _previewing = track;
        foreach (var (row, _, listen) in _rows)
        {
            listen.Text = Loc.GetString(row.ID == track ? "wg-combat-music-stop" : "wg-combat-music-listen");
        }
    }

    private BoxContainer MakeRow(WGCombatTrackPrototype track)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };

        var check = new CheckBox { VerticalAlignment = VAlignment.Top };
        check.OnToggled += _ => Changed();

        var text = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        text.AddChild(new Label { Text = Loc.GetString(track.Name) });
        if (track.Description is { } desc)
        {
            text.AddChild(new Label
            {
                Text = Loc.GetString(desc),
                FontColorOverride = Color.FromHex("#a8a8b0"),
                ClipText = true,
            });
        }

        text.AddChild(new Label
        {
            Text = Loc.GetString(track.Credits),
            FontColorOverride = Color.FromHex("#6e6e78"),
            ClipText = true,
        });

        var listen = new Button
        {
            Text = Loc.GetString("wg-combat-music-listen"),
            MinWidth = 70,
            VerticalAlignment = VAlignment.Top,
        };
        listen.OnPressed += _ => OnPreview?.Invoke(_previewing == track.ID ? null : track);

        row.AddChild(check);
        row.AddChild(text);
        row.AddChild(listen);
        _rows.Add((track, check, listen));
        return row;
    }

    private void SetAll(bool on)
    {
        foreach (var (_, box, _) in _rows)
        {
            box.Pressed = on;
        }

        Changed();
    }

    private void Changed()
    {
        UpdateSummary();
        var playlist = new List<ProtoId<WGCombatTrackPrototype>>();
        foreach (var (track, box, _) in _rows)
        {
            if (box.Pressed)
                playlist.Add(track.ID);
        }

        OnChanged?.Invoke(playlist);
    }

    private void UpdateSummary()
    {
        var count = 0;
        foreach (var (_, box, _) in _rows)
        {
            if (box.Pressed)
                count++;
        }

        _summary.Text = count == 0
            ? Loc.GetString("wg-combat-music-summary-default")
            : Loc.GetString("wg-combat-music-summary", ("count", count));
    }
}
