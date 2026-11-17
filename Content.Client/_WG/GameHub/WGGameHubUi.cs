using Content.Client.UserInterface.Fragments;
using Content.Shared._WG.GameHub;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WG.GameHub;

/// <summary>The PDA Gamehub program: a list of games to download, play and compare scores on.</summary>
public sealed partial class WGGameHubUi : UIFragment
{
    private WGGameHubFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new WGGameHubFragment(userInterface.Owner);
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
    }
}

/// <summary>Lists every game prototype with its download state.</summary>
public sealed partial class WGGameHubFragment : BoxContainer
{
    private const float BytesPerMegabyte = 1024f * 1024f;

    [Dependency] private IEntitySystemManager _systems = default!;
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private WGGameDataManager _data = default!;

    private readonly EntityUid _pda;
    private readonly List<Row> _rows = new();

    public WGGameHubFragment(EntityUid pda)
    {
        IoCManager.InjectDependencies(this);
        _pda = pda;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        Margin = new Thickness(6);

        var hub = _systems.GetEntitySystem<WGGameHubSystem>();
        foreach (var game in _protos.EnumeratePrototypes<WGGamePrototype>())
        {
            var row = new Row(game, this);
            _rows.Add(row);
            AddChild(row.Root);
            hub.RequestManifest(game.ID);
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        foreach (var row in _rows)
        {
            row.Update(_data);
        }
    }

    private void OnAction(string game)
    {
        var hub = _systems.GetEntitySystem<WGGameHubSystem>();
        switch (_data.GetStatus(game))
        {
            case WGGameDataStatus.Ready:
                hub.Launch(game, _pda);
                break;
            case WGGameDataStatus.Missing:
            case WGGameDataStatus.Failed:
                hub.StartDownload(game);
                break;
        }
    }

    private void OnLeaderboard(string game)
    {
        _systems.GetEntitySystem<WGGameHubSystem>().OpenLeaderboard(game);
    }

    private sealed class Row
    {
        private readonly string _game;
        private readonly Button _action;
        private readonly ProgressBar _progress;
        private readonly Label _status;

        public readonly BoxContainer Root;

        public Row(WGGamePrototype game, WGGameHubFragment owner)
        {
            _game = game.ID;
            _action = new Button { HorizontalExpand = true };
            _action.OnPressed += _ => owner.OnAction(_game);
            _progress = new ProgressBar { MinValue = 0, MaxValue = 1, MinHeight = 16, Visible = false };
            _status = new Label { Visible = false };

            var leaderboard = new Button { Text = Loc.GetString("wg-gamehub-leaderboard") };
            leaderboard.OnPressed += _ => owner.OnLeaderboard(_game);

            Root = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                Margin = new Thickness(0, 0, 0, 10),
                Children =
                {
                    new Label { Text = Loc.GetString(game.Name), StyleClasses = { "LabelHeading" } },
                    new RichTextLabel { Text = Loc.GetString(game.Description) },
                    _progress,
                    _status,
                    new BoxContainer
                    {
                        Orientation = LayoutOrientation.Horizontal,
                        Children = { _action, leaderboard },
                    },
                },
            };
        }

        public void Update(WGGameDataManager data)
        {
            var status = data.GetStatus(_game);
            var (received, total) = data.GetProgress(_game);

            _progress.Visible = status == WGGameDataStatus.Downloading;
            _status.Visible = status is WGGameDataStatus.Downloading or WGGameDataStatus.Failed;
            _action.Disabled = status is WGGameDataStatus.Unknown or WGGameDataStatus.Downloading;

            switch (status)
            {
                case WGGameDataStatus.Unknown:
                    _action.Text = Loc.GetString("wg-gamehub-checking");
                    break;
                case WGGameDataStatus.Missing:
                    _action.Text = Loc.GetString("wg-gamehub-download", ("size", Megabytes(total)));
                    break;
                case WGGameDataStatus.Downloading:
                    _action.Text = Loc.GetString("wg-gamehub-play");
                    _progress.Value = total > 0 ? (float) received / total : 0f;
                    _status.Text = Loc.GetString("wg-gamehub-downloading", ("received", Megabytes(received)), ("total", Megabytes(total)));
                    break;
                case WGGameDataStatus.Ready:
                    _action.Text = Loc.GetString("wg-gamehub-play");
                    break;
                case WGGameDataStatus.Failed:
                    _action.Text = Loc.GetString("wg-gamehub-retry");
                    _status.Text = Loc.GetString("wg-gamehub-download-failed");
                    break;
            }
        }

        private static string Megabytes(long bytes) => (bytes / BytesPerMegabyte).ToString("0.0");
    }
}
