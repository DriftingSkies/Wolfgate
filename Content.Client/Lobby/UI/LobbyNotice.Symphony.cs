using System.Numerics;
using Content.Shared.Symphony;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client.Lobby.UI;

/// <summary>
/// Keeps the last lobby notice the server sent, so the lobby shows it however late it is opened.
/// </summary>
public sealed class SymphonyLobbyNoticeSystem : EntitySystem
{
    public SymphonyLobbyNoticeEvent? Current { get; private set; }
    public event Action<SymphonyLobbyNoticeEvent?>? Changed;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SymphonyLobbyNoticeEvent>(ev =>
        {
            Current = ev.IsEmpty ? null : ev;
            Changed?.Invoke(Current);
        });
    }
}

/// <summary>
/// The lobby notice: a dark glass card with a coloured bar down its side, a title and a few lines. Hidden when there
/// is no notice. Placed at the top of the lobby's centre column.
/// </summary>
public sealed class SymphonyLobbyNotice : PanelContainer
{
    private readonly Label _title;
    private readonly RichTextLabel _body;
    private readonly StyleBoxFlat _box;
    private SymphonyLobbyNoticeSystem? _system;

    public SymphonyLobbyNotice()
    {
        Visible = false;
        HorizontalAlignment = HAlignment.Center;
        MaxWidth = 620;
        Margin = new Thickness(0, 0, 0, 8);
        _box = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#12161fe6"),
            BorderThickness = new Thickness(5, 0, 0, 0),
            ContentMarginLeftOverride = 18,
            ContentMarginRightOverride = 18,
            ContentMarginTopOverride = 12,
            ContentMarginBottomOverride = 14,
        };
        PanelOverride = _box;

        _title = new Label { StyleClasses = { "LabelHeading" } };
        _body = new RichTextLabel { Margin = new Thickness(0, 6, 0, 0) };
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        column.AddChild(_title);
        column.AddChild(_body);
        AddChild(column);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        _system = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<SymphonyLobbyNoticeSystem>();
        _system.Changed += Show;
        Show(_system.Current);
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        if (_system != null)
            _system.Changed -= Show;
    }

    private static Color AccentOf(string style) => style switch
    {
        "warning" => Color.FromHex("#f0a830"),
        "event" => Color.FromHex("#b06cf0"),
        _ => Color.FromHex("#4f9cf7"),
    };

    private void Show(SymphonyLobbyNoticeEvent? notice)
    {
        if (notice == null)
        {
            Visible = false;
            return;
        }
        var accent = AccentOf(notice.Style);
        _box.BorderColor = accent;
        _title.Text = notice.Title ?? "";
        _title.Visible = !string.IsNullOrWhiteSpace(notice.Title);
        _title.FontColorOverride = accent;
        var body = new FormattedMessage();
        body.AddMarkupPermissive(notice.Body ?? "");
        _body.SetMessage(body);
        _body.Visible = !string.IsNullOrWhiteSpace(notice.Body);
        Visible = true;
    }
}
