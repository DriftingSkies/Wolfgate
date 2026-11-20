using System.Numerics;
using Content.Shared._WG.Ghost;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Utility;

namespace Content.Client._WG.Ghost;

/// <summary>One ghost notice: the source's sprite, a heading, the text and a draining bar. Click orbits, right-click dismisses.</summary>
public sealed class GhostNoticeChip : PanelContainer
{
    private const float FadeTime = 0.6f;

    private readonly ProgressBar _bar;
    private readonly float _duration;
    private float _elapsed;

    public NetEntity Source { get; }

    /// <summary>Raised on a left click.</summary>
    public event Action<GhostNoticeChip>? Orbited;

    /// <summary>Raised on a right click.</summary>
    public event Action<GhostNoticeChip>? Dismissed;

    public bool Expired => _elapsed >= _duration;

    public GhostNoticeChip(GhostNoticeEvent notice, IEntityManager entities)
    {
        Source = notice.Source;
        _duration = MathF.Max(notice.Duration, 1f);
        MouseFilter = MouseFilterMode.Stop;
        MinWidth = 300;
        MaxWidth = 360;
        ToolTip = Loc.GetString("wg-ghost-notice-hint");
        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.Black.WithAlpha(0.8f),
            BorderColor = notice.Color,
            BorderThickness = new Thickness(2),
        };

        SpriteView icon;
        if (entities.TryGetEntity(notice.Source, out var live) && entities.EntityExists(live))
        {
            icon = new SpriteView();
            icon.SetEntity(live);
        }
        else
        {
            var view = new EntityPrototypeView();
            view.SetPrototype(notice.Prototype);
            icon = view;
        }

        icon.SetSize = new Vector2(48, 48);
        icon.OverrideDirection = Direction.South;
        icon.VerticalAlignment = VAlignment.Center;
        icon.MouseFilter = MouseFilterMode.Ignore;

        var title = new Label
        {
            Text = notice.Title.ToUpperInvariant(),
            FontColorOverride = notice.Color,
            StyleClasses = { "LabelHeading" },
            MouseFilter = MouseFilterMode.Ignore,
        };

        var text = new RichTextLabel { HorizontalExpand = true, MouseFilter = MouseFilterMode.Ignore };
        text.SetMessage(FormattedMessage.FromUnformatted(notice.Text));

        _bar = new ProgressBar
        {
            MinValue = 0f,
            MaxValue = 1f,
            Value = 1f,
            MinHeight = 4,
            Margin = new Thickness(0, 4, 0, 0),
            MouseFilter = MouseFilterMode.Ignore,
            ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = notice.Color },
            BackgroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = notice.Color.WithAlpha(0.2f) },
        };

        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            MouseFilter = MouseFilterMode.Ignore,
        };
        body.AddChild(title);
        body.AddChild(text);
        body.AddChild(_bar);

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            Margin = new Thickness(6, 4, 8, 4),
            MouseFilter = MouseFilterMode.Ignore,
        };
        row.AddChild(icon);
        row.AddChild(body);
        AddChild(row);
    }

    /// <summary>Drains the bar and fades out at the end.</summary>
    public void Tick(float frameTime)
    {
        _elapsed += frameTime;
        _bar.Value = Math.Clamp(1f - _elapsed / _duration, 0f, 1f);
        Modulate = Color.White.WithAlpha(Math.Clamp((_duration - _elapsed) / FadeTime, 0f, 1f));
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function == EngineKeyFunctions.UIClick)
        {
            Orbited?.Invoke(this);
            args.Handle();
        }
        else if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            Dismissed?.Invoke(this);
            args.Handle();
        }
    }
}
