using System.Linq;
using System.Numerics;
using Content.Client._WG.Stylesheets;
using Content.Client._WG.UserInterface.Controls;
using Content.Client.Lobby;
using Content.Shared._WG.CustomMarkings;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;

namespace Content.Client._WG.CustomMarkings.UI;

/// <summary>
/// The pixel editor for one custom marking: four facings drawn over the character's body, one frame at a time for
/// an animated marking, then saved to the player's library under a name and a placement.
/// </summary>
public sealed partial class CustomMarkingEditorWindow : CustomMarkingWindow
{
    /// <summary>What the canvas, the tools and the facing buttons change.</summary>
    private enum Layer
    {
        /// <summary>The marking's pixels.</summary>
        Drawing,

        /// <summary>What the marking erases from the body under it.</summary>
        Erase,

        /// <summary>Which drawn pixels glow.</summary>
        Glow,
    }

    private enum Tool
    {
        Pencil,
        Eraser,
        Fill,
        Picker,
    }

    private const int CanvasScale = 12;
    private const int FacingScale = 2;

    /// <summary>
    /// Width of the window's content. It has to hold the tools, the canvas and the side panel side by side: about
    /// 760 with the longest facing names.
    /// </summary>
    private const float ContentWidth = 790;

    /// <summary>How many of the colours a marking is drawn in are offered to pick again.</summary>
    private const int MaxUsedColours = 16;

    /// <summary>Tall enough for a frame's preview, its time and a scroll bar under them.</summary>
    private const float FrameStripHeight = CustomMarkingRules.FrameSize * CustomMarkingFrameStrip.PreviewScale + 44;

    private static readonly string[] FacingNames =
    {
        "wg-custom-marking-facing-south",
        "wg-custom-marking-facing-north",
        "wg-custom-marking-facing-east",
        "wg-custom-marking-facing-west",
    };

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IInputManager _input = default!;

    private readonly CustomMarkingSystem _system;
    private readonly LobbyUIController _lobby;
    private readonly HumanoidCharacterProfile? _profile;

    /// <summary>The library entry being changed, or null for a new marking.</summary>
    private readonly CustomMarkingEntry? _entry;

    /// <summary>The character slot a new marking is offered to once saved, or null for every character.</summary>
    public int? Slot { get; init; }

    private readonly CustomMarkingSketch _sketch;
    private readonly CustomMarkingArt _opened;

    /// <summary>The body pixels erased by the character's other markings, and what those draw over solidly.</summary>
    private readonly byte[] _othersErase = new byte[CustomMarkingRules.EraseBytes];

    private readonly byte[] _othersSolid = new byte[CustomMarkingRules.EraseBytes];

    /// <summary>Everything erased from the body shown: by the other markings and by this one as it stands.</summary>
    private readonly byte[] _erase = new byte[CustomMarkingRules.EraseBytes];

    private readonly CustomMarkingCanvas _canvas;
    private readonly CustomMarkingBodySampler _sampler;
    private readonly Label _canvasHeading;
    private readonly RichTextLabel _toolHint;
    private readonly CustomMarkingCanvas[] _facings = new CustomMarkingCanvas[CustomMarkingRules.Facings];
    private readonly WolfgateColorPicker _colour;
    private readonly Slider _opacity;
    private readonly FloatSpinBox _opacityValue;
    private readonly BoxContainer _usedColours;
    private readonly Label _noUsedColours;
    private readonly List<Rgba32> _usedColourList = new();
    private readonly string _openedName;
    private readonly CustomMarkingPlacement _openedPlacement;
    private CustomMarkingCloseWindow? _closePrompt;
    // Set once the edits are saved or discarded, so closing no longer asks.
    private bool _closeConfirmed;
    private readonly LineEdit _name;
    private readonly OptionButton _placement;
    private readonly Label _placementSummary;
    private readonly CheckBox _showBody;
    private readonly CheckBox _showHair;
    private readonly CheckBox _showClothes;
    private readonly CustomMarkingIconButton _symmetry;
    private readonly CustomMarkingIconButton _sideMirror;
    private readonly CustomMarkingIconButton _undoButton;
    private readonly CustomMarkingIconButton _redoButton;
    private readonly List<(CustomMarkingIconButton Button, string Direction)> _nudges = new();
    private readonly CustomMarkingIconButton _flipButton;
    private readonly CustomMarkingIconButton _mirrorButton;
    private readonly CustomMarkingIconButton _clearButton;
    private readonly Label _frameLabel;
    private readonly CustomMarkingFrameStrip _frameStrip;
    private readonly CustomMarkingIconButton _addFrame;
    private readonly CustomMarkingIconButton _removeFrame;
    private readonly CustomMarkingIconButton _playButton;
    private readonly CustomMarkingIconButton _reverseFrames;
    private readonly CustomMarkingIconButton _pingPongButton;
    private readonly CustomMarkingIconButton _timeAllButton;
    private readonly CheckBox _onionSkin;
    private readonly FloatSpinBox _frameTime;
    private readonly Control _frameTimeRow;
    private readonly Button _saveButton;
    private readonly Label _status;

    private readonly Dictionary<Layer, Button> _layerButtons = new();
    private readonly Dictionary<Tool, CustomMarkingIconButton> _toolButtons = new();

    private Layer _layer = Layer.Drawing;
    private Tool _tool = Tool.Pencil;

    /// <summary>Where the stroke being drawn last reached.</summary>
    private Vector2i? _last;

    /// <summary>Where the last stroke on this facing ended, which Shift+click draws a straight line from.</summary>
    private Vector2i? _lineEnd;

    /// <summary>Whether the stroke being drawn only picks a colour.</summary>
    private bool _picking;

    private EntityUid? _body;
    private int _request = -1;

    /// <summary>The frame shown and drawn on.</summary>
    private int _frame;

    private bool _playing;

    /// <summary>Where playing is in <see cref="CustomMarkingArt.PlayOrder"/>.</summary>
    private int _playStep;

    /// <summary>How long the frame shown has been up while playing, in seconds.</summary>
    private float _playTime;

    /// <summary>Set while the frame time box is filled in from the art, which is not the player changing it.</summary>
    private bool _fillingTime;

    /// <summary>Whether the status line is saying that too much of the body is erased.</summary>
    private bool _erasedTooMuch;

    /// <summary>Raised once the server has saved the marking: the entry as it was opened, if any, and as saved.</summary>
    public Action<CustomMarkingEntry?, CustomMarkingEntry>? OnSaved;

    /// <param name="profile">The character whose body is drawn under the art, without the marking being edited.</param>
    public CustomMarkingEditorWindow(CustomMarkingEntry? entry, CustomMarkingArt art, string name, HumanoidCharacterProfile? profile)
    {
        IoCManager.InjectDependencies(this);
        _system = _entMan.System<CustomMarkingSystem>();
        _lobby = UserInterfaceManager.GetUIController<LobbyUIController>();
        _entry = entry;
        _sketch = new CustomMarkingSketch(art);
        _opened = art.Clone();
        _profile = profile;

        Title = Loc.GetString(entry == null ? "wg-custom-marking-editor-title-new" : "wg-custom-marking-editor-title-edit");
        Resizable = false;

        if (profile != null)
        {
            foreach (var worn in profile.CustomMarkings)
            {
                if (_system.TryGetErase(worn.Hash, out var mask))
                    CustomMarkingErase.Add(_othersErase, mask);

                if (_system.TryReadArt(worn.Hash, out var other))
                    CustomMarkingErase.Add(_othersSolid, other.Solid());
            }
        }

        // The canvas, what it changes, how the tool picked works, and the body sampler the colour picker reads from
        _canvas = new CustomMarkingCanvas(CanvasScale, true) { Art = _sketch.Art, ShowGrid = true };
        _canvas.Stroke += OnStroke;
        _canvas.StrokeEnded += EndStroke;
        _canvasHeading = Heading(FacingNames[0]);
        _canvasHeading.HorizontalExpand = true;
        _canvasHeading.VAlign = Label.VAlignMode.Center;
        _sampler = new CustomMarkingBodySampler();
        UserInterfaceManager.RootControl.AddChild(_sampler);

        var layerGroup = new ButtonGroup();
        var layers = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 2 };
        foreach (var layer in Enum.GetValues<Layer>())
        {
            if (layer == Layer.Erase && !_system.EraseEnabled || layer == Layer.Glow && !_system.GlowEnabled)
                continue;

            var key = layer.ToString().ToLowerInvariant();
            var button = new Button
            {
                Text = Loc.GetString($"wg-custom-marking-layer-{key}"),
                ToolTip = Loc.GetString($"wg-custom-marking-layer-{key}-tooltip", ("percent", CustomMarkingErase.MinKeptPercent)),
                ToggleMode = true,
                Group = layerGroup,
                Pressed = layer == _layer,
                StyleClasses = { StyleWolfgate.StyleClassCreatorToggle },
            };
            button.OnPressed += _ => SetLayer(layer);
            _layerButtons[layer] = button;
            layers.AddChild(button);
        }

        // Only worth showing when there is more than the drawing to change.
        layers.Visible = _layerButtons.Count > 1;

        _toolHint = new RichTextLabel { SetWidth = CustomMarkingRules.FrameSize * CanvasScale };

        // Tools, in blocks two wide down the left of the canvas: drawing, history, and what can be done to the
        // facing shown
        var toolGroup = new ButtonGroup();
        var drawing = ToolGrid();
        foreach (var tool in Enum.GetValues<Tool>())
        {
            var button = new CustomMarkingIconButton(tool.ToString().ToLowerInvariant(), string.Empty)
            {
                ToggleMode = true,
                Group = toolGroup,
                Pressed = tool == _tool,
            };
            button.AddStyleClass(StyleWolfgate.StyleClassCreatorToggle);
            button.OnPressed += _ => SetTool(tool);
            _toolButtons[tool] = button;
            drawing.AddChild(button);
        }

        _symmetry = new CustomMarkingIconButton("symmetry", Loc.GetString("wg-custom-marking-editor-symmetry")) { ToggleMode = true };
        _symmetry.AddStyleClass(StyleWolfgate.StyleClassCreatorToggle);
        _symmetry.OnToggled += args => SetMirror(args.Pressed);
        drawing.AddChild(_symmetry);

        _sideMirror = new CustomMarkingIconButton("sides", Loc.GetString("wg-custom-marking-editor-side-mirror")) { ToggleMode = true };
        _sideMirror.AddStyleClass(StyleWolfgate.StyleClassCreatorToggle);
        _sideMirror.OnToggled += args => SetSideMirror(args.Pressed);
        drawing.AddChild(_sideMirror);

        _undoButton = Command("undo", "wg-custom-marking-editor-undo", Undo);
        _redoButton = Command("redo", "wg-custom-marking-editor-redo", Redo);
        var history = ToolGrid();
        history.AddChild(_undoButton);
        history.AddChild(_redoButton);

        var facingTools = ToolGrid();
        foreach (var (direction, dx, dy) in new[] { ("left", -1, 0), ("right", 1, 0), ("up", 0, -1), ("down", 0, 1) })
        {
            var button = new CustomMarkingIconButton(direction, string.Empty);
            button.OnPressed += _ => Nudge(dx, dy);
            _nudges.Add((button, direction));
            facingTools.AddChild(button);
        }

        _flipButton = new CustomMarkingIconButton("flip", string.Empty);
        _flipButton.OnPressed += _ => Flip();
        _mirrorButton = new CustomMarkingIconButton("mirror", string.Empty);
        _mirrorButton.OnPressed += _ => CopyToOtherSide();
        _clearButton = new CustomMarkingIconButton("trash", string.Empty);
        _clearButton.OnPressed += _ => ClearFacing();
        facingTools.AddChild(_flipButton);
        facingTools.AddChild(_mirrorButton);
        facingTools.AddChild(_clearButton);

        var tools = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            Children =
            {
                ToolBlock("wg-custom-marking-editor-group-draw", drawing),
                ToolBlock("wg-custom-marking-editor-group-history", history),
                ToolBlock("wg-custom-marking-editor-group-facing", facingTools),
            },
        };

        // The four facings, each on the body, and what the canvas shows
        var facingGroup = new ButtonGroup();
        var facingGrid = new GridContainer { Columns = 2, HSeparationOverride = 4, VSeparationOverride = 4 };
        for (var i = 0; i < _facings.Length; i++)
        {
            var facing = i;
            _facings[i] = new CustomMarkingCanvas(FacingScale, false) { Art = _sketch.Art, Facing = i };
            var tile = new ContainerButton
            {
                StyleClasses = { ContainerButton.StyleClassButton, StyleWolfgate.StyleClassCreatorToggle },
                ToggleMode = true,
                Group = facingGroup,
                Pressed = i == 0,
                // The body on the tile shows which way it faces; the canvas heading names the one picked.
                ToolTip = Loc.GetString(FacingNames[i]),
                Children = { _facings[i] },
            };
            tile.OnPressed += _ => SetFacing(facing);
            facingGrid.AddChild(tile);
        }

        // The frames of an animated marking: a strip to pick and move them, what can be done to them, and how long
        // each shows
        _frameLabel = new Label
        {
            VerticalAlignment = VAlignment.Center,
            StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel },
        };
        _frameStrip = new CustomMarkingFrameStrip
        {
            SetUpPreview = SetUpPreview,
            ToolTip = Loc.GetString("wg-custom-marking-editor-frame-strip"),
        };
        _frameStrip.FramePicked += SetFrame;
        _frameStrip.DragStarted += () =>
        {
            StopPlaying();
            _sketch.Begin();
        };
        _frameStrip.FrameMoved += (from, to) =>
        {
            Art.MoveFrame(from, to);
            ShowFrame(to);
            UpdateButtons();
        };
        _frameStrip.DragEnded += () =>
        {
            _sketch.End();
            Changed();
        };

        _playButton = new CustomMarkingIconButton("play", Loc.GetString("wg-custom-marking-editor-frame-play")) { ToggleMode = true };
        _playButton.AddStyleClass(StyleWolfgate.StyleClassCreatorToggle);
        _playButton.OnToggled += args => SetPlaying(args.Pressed);
        _addFrame = Command("add", "wg-custom-marking-editor-frame-add", AddFrame);
        _removeFrame = Command("remove", "wg-custom-marking-editor-frame-remove", RemoveFrame);
        _reverseFrames = Command("reverse", "wg-custom-marking-editor-frame-reverse", () =>
        {
            StopPlaying();
            Change(edited => edited.ReverseFrames());
            SetFrame(Art.Frames - 1 - _frame);
        });
        _timeAllButton = Command("timeall", "wg-custom-marking-editor-frame-time-all",
            () => Change(edited => edited.SetAllFrameTimes(edited.GetFrameTime(_frame))));
        _pingPongButton = new CustomMarkingIconButton("pingpong", Loc.GetString("wg-custom-marking-editor-frame-ping-pong"))
        {
            ToggleMode = true,
            Pressed = art.PingPong,
        };
        _pingPongButton.AddStyleClass(StyleWolfgate.StyleClassCreatorToggle);
        _pingPongButton.OnToggled += args =>
        {
            // The play order changes under a running playback.
            StopPlaying();
            Change(edited => edited.PingPong = args.Pressed);
        };
        _onionSkin = new CheckBox
        {
            Text = Loc.GetString("wg-custom-marking-editor-onion-skin"),
            ToolTip = Loc.GetString("wg-custom-marking-editor-onion-skin-tooltip"),
        };
        _onionSkin.OnPressed += _ => _canvas.OnionSkin = _onionSkin.Pressed;
        _frameTime = new FloatSpinBox(0.01f, 2)
        {
            HorizontalExpand = true,
            IsValid = seconds => seconds * 1000 >= CustomMarkingRules.MinFrameTime && seconds * 1000 <= CustomMarkingRules.MaxFrameTime,
        };
        _frameTime.OnValueChanged += args => SetFrameTime(args.Value);
        _frameTimeRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            HorizontalExpand = true,
            ToolTip = Loc.GetString("wg-custom-marking-editor-frame-time-tooltip",
                ("min", CustomMarkingRules.MinFrameTime / 1000f),
                ("max", CustomMarkingRules.MaxFrameTime / 1000f)),
            Children = { FieldLabel("wg-custom-marking-editor-frame-time"), _frameTime },
        };

        var frameHeading = Heading("wg-custom-marking-editor-frames");
        frameHeading.HorizontalExpand = true;
        var frames = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            // A server that only allows still markings shows the frames of one made before, to take them out.
            Visible = MaxFrames > 1 || art.Frames > 1,
            Children =
            {
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    Children = { frameHeading, _frameLabel },
                },
                new ScrollContainer
                {
                    HScrollEnabled = true,
                    VScrollEnabled = false,
                    HorizontalExpand = true,
                    MinHeight = FrameStripHeight,
                    Children = { _frameStrip },
                },
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    SeparationOverride = 4,
                    Children = { _playButton, _addFrame, _removeFrame, _reverseFrames, _timeAllButton, _pingPongButton },
                },
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    SeparationOverride = 8,
                    Children = { _frameTimeRow, _onionSkin },
                },
            },
        };

        _showBody = new CheckBox { Text = Loc.GetString("wg-custom-marking-editor-show-body"), Pressed = true };
        _showBody.OnPressed += _ => UpdateBody();
        _showClothes = new CheckBox { Text = Loc.GetString("wg-custom-marking-editor-show-clothes") };
        _showClothes.OnPressed += _ => ReloadBody();
        var showGrid = new CheckBox { Text = Loc.GetString("wg-custom-marking-editor-show-grid"), Pressed = true };
        showGrid.OnPressed += _ => _canvas.ShowGrid = showGrid.Pressed;
        _showHair = new CheckBox
        {
            Text = Loc.GetString("wg-custom-marking-editor-show-hair"),
            ToolTip = Loc.GetString("wg-custom-marking-editor-show-hair-tooltip"),
            Pressed = true,
        };
        _showHair.OnPressed += _ => UpdateBody();

        var view = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Children =
            {
                Heading("wg-custom-marking-editor-facings"),
                facingGrid,
                new Control { MinHeight = 4 },
                frames,
                new Control { VerticalExpand = true },
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    SeparationOverride = 8,
                    Children = { _showBody, _showClothes },
                },
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    SeparationOverride = 8,
                    Children = { _showHair, showGrid },
                },
            },
        };

        // Colour: the palette, the character's own colours beside it, the opacity, and the colours already used
        _colour = new WolfgateColorPicker { Color = Color.Black, VerticalAlignment = VAlignment.Center };
        _colour.OnColorChanged += _ => DrawAgain();
        _opacity = new Slider { MinValue = 5, MaxValue = 100, Value = 100, MinWidth = 120, VerticalAlignment = VAlignment.Center };
        _opacityValue = new FloatSpinBox(1f, 0)
        {
            Name = "OpacityValue",
            MinWidth = 90,
            VerticalAlignment = VAlignment.Center,
            IsValid = value => value >= _opacity.MinValue && value <= _opacity.MaxValue,
            ToolTip = Loc.GetString("wg-custom-marking-editor-opacity-tooltip"),
        };
        _opacityValue.Value = _opacity.Value;
        // Setting the box's value raises nothing, and the slider only reports a change, so the two can't loop.
        _opacity.OnValueChanged += _ => _opacityValue.Value = MathF.Round(_opacity.Value);
        _opacityValue.OnValueChanged += args => _opacity.Value = args.Value;
        var colours = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 10,
            Children = { _colour },
        };
        if (profile != null)
        {
            colours.AddChild(new GridContainer
            {
                Columns = 2,
                HSeparationOverride = 2,
                VSeparationOverride = 2,
                VerticalAlignment = VAlignment.Center,
                Children =
                {
                    Swatch(profile.Appearance.SkinColor, Loc.GetString("wg-custom-marking-editor-colour-skin")),
                    Swatch(profile.Appearance.HairColor, Loc.GetString("wg-custom-marking-editor-colour-hair")),
                    Swatch(profile.Appearance.FacialHairColor, Loc.GetString("wg-custom-marking-editor-colour-facial-hair")),
                    Swatch(profile.Appearance.EyeColor, Loc.GetString("wg-custom-marking-editor-colour-eyes")),
                },
            });
        }

        colours.AddChild(new Control { HorizontalExpand = true });
        colours.AddChild(FieldLabel("wg-custom-marking-editor-opacity"));
        colours.AddChild(_opacity);
        colours.AddChild(_opacityValue);

        _usedColours = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 2 };
        _noUsedColours = new Label
        {
            Text = Loc.GetString("wg-custom-marking-editor-used-colours-none"),
            VerticalAlignment = VAlignment.Center,
            StyleClasses = { "LabelSubText" },
        };

        // Name and placement, with what the placement means beside them
        _name = new LineEdit
        {
            Text = name,
            MinWidth = 200,
            PlaceHolder = Loc.GetString("wg-custom-marking-default-name"),
            IsValid = text => text.Length <= CustomMarkingRules.MaxNameLength,
        };
        _placement = new OptionButton { MinWidth = 170 };
        foreach (var placement in Enum.GetValues<CustomMarkingPlacement>())
        {
            _placement.AddItem(Loc.GetString(PlacementName(placement)), (int) placement);
        }

        _placement.SelectId((int) (entry?.Placement ?? CustomMarkingPlacement.Skin));
        _openedName = CustomMarkingRules.CleanName(name);
        _openedPlacement = Placement;
        _placement.OnItemSelected += args =>
        {
            _placement.SelectId(args.Id);
            UpdatePlacement();
        };
        _placementSummary = new Label
        {
            ClipText = true,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel },
        };

        _status = new Label { ClipText = true, HorizontalExpand = true, VerticalAlignment = VAlignment.Center };
        _saveButton = new Button { Text = Loc.GetString("wg-custom-marking-editor-save"), StyleClasses = { StyleWolfgate.StyleClassCreatorPrimary } };
        _saveButton.OnPressed += _ => Save();
        var cancel = new Button { Text = Loc.GetString("wg-custom-marking-editor-cancel") };
        cancel.OnPressed += _ => Close();

        Contents.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            SetWidth = ContentWidth,
            Children =
            {
                Card(new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    SeparationOverride = 6,
                    Children =
                    {
                        FieldLabel("wg-custom-marking-editor-name"),
                        _name,
                        new Control { MinWidth = 6 },
                        FieldLabel("wg-custom-marking-editor-placement"),
                        _placement,
                        _placementSummary,
                    },
                }, true),
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    SeparationOverride = 8,
                    Children =
                    {
                        Card(new BoxContainer
                        {
                            Orientation = BoxContainer.LayoutOrientation.Horizontal,
                            SeparationOverride = 8,
                            Children =
                            {
                                tools,
                                new BoxContainer
                                {
                                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                                    SeparationOverride = 6,
                                    Children =
                                    {
                                        new BoxContainer
                                        {
                                            Orientation = BoxContainer.LayoutOrientation.Horizontal,
                                            SeparationOverride = 6,
                                            Children = { _canvasHeading, layers },
                                        },
                                        _canvas,
                                        _toolHint,
                                    },
                                },
                            },
                        }, false),
                        Card(view, true),
                    },
                },
                Card(new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    SeparationOverride = 6,
                    Children =
                    {
                        colours,
                        new BoxContainer
                        {
                            Orientation = BoxContainer.LayoutOrientation.Horizontal,
                            SeparationOverride = 8,
                            Children = { FieldLabel("wg-custom-marking-editor-used-colours"), _usedColours, _noUsedColours },
                        },
                    },
                }, true),
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    SeparationOverride = 6,
                    Children = { _status, cancel, _saveButton },
                },
            },
        });

        _system.SaveAnswered += OnSaveAnswered;
        _cfg.OnValueChanged(CustomMarkingCVars.BodyReach, OnBodyReachChanged);
        _input.FirstChanceOnKeyEvent += OnKey;
        OnClose += () =>
        {
            _closePrompt?.Close();
            _system.SaveAnswered -= OnSaveAnswered;
            _cfg.UnsubValueChanged(CustomMarkingCVars.BodyReach, OnBodyReachChanged);
            _input.FirstChanceOnKeyEvent -= OnKey;
            DeleteBody();
            _sampler.Orphan();
        };

        ReloadBody();
        UpdatePlacement();
        SetLayer(Layer.Drawing);
        Changed();
    }

    private CustomMarkingArt Art => _sketch.Art;

    private int Facing => _canvas.Facing;

    private CustomMarkingPlacement Placement => (CustomMarkingPlacement) _placement.SelectedId;

    /// <summary>Most frames the server lets a marking be saved with.</summary>
    private int MaxFrames => Math.Clamp(_cfg.GetCVar(CustomMarkingCVars.MaxFrames), 1, CustomMarkingRules.MaxFrames);

    /// <summary>The small pictures of the art over the body: the facings, and the frames in the strip.</summary>
    private IEnumerable<CustomMarkingCanvas> Previews => _facings.Concat(_frameStrip.Previews);

    public static string PlacementName(CustomMarkingPlacement placement)
    {
        return $"wg-custom-marking-placement-{placement.ToString().ToLowerInvariant()}";
    }

    private static Label Heading(string loc)
    {
        return new Label { Text = Loc.GetString(loc), StyleClasses = { StyleWolfgate.StyleClassCreatorHeading } };
    }

    private static Label FieldLabel(string loc)
    {
        return new Label
        {
            Text = Loc.GetString(loc),
            VerticalAlignment = VAlignment.Center,
            StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel },
        };
    }

    /// <summary>Groups editor controls using the character creator's inset panels.</summary>
    private static PanelContainer Card(Control content, bool expand)
    {
        return new PanelContainer
        {
            StyleClasses = { StyleWolfgate.StyleClassCreatorGroup },
            HorizontalExpand = expand,
            Children = { content },
        };
    }

    /// <summary>A block of icon buttons, two to a row.</summary>
    private static GridContainer ToolGrid()
    {
        return new GridContainer { Columns = 2, HSeparationOverride = 4, VSeparationOverride = 4 };
    }

    /// <summary>A block of tools under a small heading saying what they are for.</summary>
    private static BoxContainer ToolBlock(string loc, Control grid)
    {
        var heading = FieldLabel(loc);
        heading.Align = Label.AlignMode.Center;
        return new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
            Children = { heading, grid },
        };
    }

    private static CustomMarkingIconButton Command(string icon, string loc, Action act)
    {
        var button = new CustomMarkingIconButton(icon, Loc.GetString(loc));
        button.OnPressed += _ => act();
        return button;
    }

    /// <summary>Enables a button with its tooltip, or disables it with the reason why under the tooltip.</summary>
    private static void Allow(BaseButton button, string tooltip, string? reason)
    {
        button.Disabled = reason != null;
        button.ToolTip = reason == null ? tooltip : $"{tooltip}\n{reason}";
    }

    private Control Swatch(Color color, string tooltip, Action? picked = null)
    {
        var swatch = new ContainerButton
        {
            StyleClasses = { WolfgateColorPicker.StyleClassSwatch },
            ToolTip = tooltip,
            Children =
            {
                new PanelContainer
                {
                    PanelOverride = new StyleBoxFlat(color),
                    MinSize = new Vector2(20, 20),
                    Margin = new Thickness(2),
                },
            },
        };
        swatch.OnPressed += _ =>
        {
            if (picked != null)
                picked();
            else
                _colour.Color = color.WithAlpha(1f);

            DrawAgain();
        };
        return swatch;
    }

    /// <summary>Turns mirror drawing on or off, with a line on the canvas where the mirror stands.</summary>
    private void SetMirror(bool on)
    {
        _sketch.Mirror = on;
        _symmetry.Pressed = on;
        UpdateMirror();
    }

    /// <summary>Turns side mirroring on or off: drawing on one side view draws on the other too.</summary>
    private void SetSideMirror(bool on)
    {
        _sketch.SideMirror = on;
        _sideMirror.Pressed = on;
    }

    /// <summary>Stands the mirror on the middle of the body in the facing shown, read off its torso.</summary>
    private void UpdateMirror()
    {
        var axis = CustomMarkingSketch.DefaultMirrorAxis;
        if (_body is { } body
            && _entMan.TryGetComponent(body, out SpriteComponent? sprite)
            && _system.MirrorAxis((body, sprite), Facing) is { } middle)
            axis = middle;

        _sketch.MirrorAxis = axis;
        _canvas.MirrorAxis = _sketch.Mirror ? axis : null;
    }

    /// <summary>Picks what the canvas changes. Fill has nothing to fill on the erased body, so it gives way to the pencil.</summary>
    private void SetLayer(Layer layer)
    {
        if (!_layerButtons.ContainsKey(layer))
            return;

        _layer = layer;
        _layerButtons[layer].Pressed = true;
        _canvas.ShowErase = layer == Layer.Erase;
        _canvas.ShowGlow = layer == Layer.Glow;
        if (layer == Layer.Erase && _tool == Tool.Fill)
            SetTool(Tool.Pencil);

        UpdateTools();
        UpdateFacingButtons();
    }

    private void SetTool(Tool tool)
    {
        if (_toolButtons[tool].Disabled)
            return;

        _tool = tool;
        _toolButtons[tool].Pressed = true;
        UpdateTools();
    }

    /// <summary>Says what each tool does on the layer picked, and how the picked one is used.</summary>
    private void UpdateTools()
    {
        var layer = _layer.ToString().ToLowerInvariant();
        foreach (var (tool, button) in _toolButtons)
        {
            var reason = tool == Tool.Fill && _layer == Layer.Erase ? Loc.GetString("wg-custom-marking-tool-fill-erase") : null;
            Allow(button, Loc.GetString($"wg-custom-marking-tool-{tool.ToString().ToLowerInvariant()}", ("layer", layer)), reason);
        }

        var hint = Loc.GetString($"wg-custom-marking-hint-{_tool.ToString().ToLowerInvariant()}", ("layer", layer));
        _toolHint.SetMessage(FormattedMessage.FromUnformatted($"{hint} {Loc.GetString("wg-custom-marking-hint-keys")}"));
    }

    /// <summary>
    /// Says what the facing buttons change on the layer picked. Glow stays on its pixels, so only the drawing's
    /// buttons move it.
    /// </summary>
    private void UpdateFacingButtons()
    {
        var layer = _layer == Layer.Erase ? "erase" : "drawing";
        var glow = _layer == Layer.Glow ? Loc.GetString("wg-custom-marking-editor-glow-moves") : null;
        foreach (var (button, direction) in _nudges)
        {
            Allow(button, Loc.GetString("wg-custom-marking-editor-nudge", ("layer", layer), ("direction", direction)), glow);
        }

        Allow(_flipButton, Loc.GetString("wg-custom-marking-editor-flip", ("layer", layer)), glow);
        var side = Facing is CustomMarkingArt.East or CustomMarkingArt.West;
        Allow(_mirrorButton, Loc.GetString("wg-custom-marking-editor-mirror", ("layer", layer)),
            glow ?? (side ? null : Loc.GetString("wg-custom-marking-editor-mirror-sides")));
        Allow(_clearButton, Loc.GetString("wg-custom-marking-editor-clear", ("layer", _layer.ToString().ToLowerInvariant())), null);
    }

    private void Nudge(int dx, int dy)
    {
        Change(art =>
        {
            if (_layer == Layer.Erase)
                art.ShiftErase(Facing, dx, dy);
            else
                art.Shift(_frame, Facing, dx, dy);
        });
    }

    private void Flip()
    {
        Change(art =>
        {
            if (_layer == Layer.Erase)
                art.FlipErase(Facing, _sketch.MirrorAxis);
            else
                art.Flip(_frame, Facing, _sketch.MirrorAxis);
        });
    }

    private void CopyToOtherSide()
    {
        Change(art =>
        {
            if (_layer == Layer.Erase)
                art.CopyErase(Facing, Opposite(Facing), true);
            else
                art.CopyFacing(_frame, Facing, Opposite(Facing), true);
        });
    }

    private void ClearFacing()
    {
        Change(art =>
        {
            switch (_layer)
            {
                case Layer.Erase:
                    art.ClearErase(Facing);
                    break;
                case Layer.Glow:
                    art.ClearGlow(_frame, Facing);
                    break;
                default:
                    art.Clear(_frame, Facing);
                    break;
            }
        });
    }

    /// <summary>Goes back to drawing with the pencil once a colour is chosen, from the palette or off the canvas.</summary>
    private void DrawAgain()
    {
        SetLayer(Layer.Drawing);
        if (_tool is Tool.Picker or Tool.Eraser)
            SetTool(Tool.Pencil);
    }

    private Rgba32 Ink()
    {
        return CustomMarkingArt.ToPixel(_colour.Color.WithAlpha(_opacity.Value / 100f));
    }

    /// <summary>Takes up a drawn pixel's colour and opacity.</summary>
    private void SetInk(Rgba32 pixel)
    {
        _colour.Color = CustomMarkingArt.ToColor(pixel).WithAlpha(1f);
        _opacity.Value = MathF.Max(_opacity.MinValue, pixel.A * 100f / byte.MaxValue);
    }

    private void SetFacing(int facing)
    {
        _canvas.Facing = facing;
        _sampler.Facing = facing;
        _canvasHeading.Text = Loc.GetString(FacingNames[facing]);
        _lineEnd = null;
        UpdateMirror();
        UpdateButtons();
    }

    /// <summary>The other side view, which is this one seen in a mirror.</summary>
    private static int Opposite(int facing)
    {
        return facing == CustomMarkingArt.East ? CustomMarkingArt.West : CustomMarkingArt.East;
    }

    /// <summary>
    /// Left click does what the tool does on the layer picked and right click takes it back. Alt+click picks a colour
    /// with any tool, and Shift+click starts the line where the last one ended.
    /// </summary>
    private void OnStroke(CustomMarkingStroke stroke)
    {
        var pixel = stroke.Pixel;
        if (stroke.Start)
        {
            StopPlaying();
            _picking = stroke.Alt || _tool == Tool.Picker && !stroke.Erase;
            if (_picking)
            {
                if (CustomMarkingArt.InFrame(pixel.X, pixel.Y) && Pick(pixel) && !stroke.Alt)
                    DrawAgain();

                return;
            }

            _sketch.Begin();
            _last = stroke.Shift ? _lineEnd : null;
        }

        if (_picking)
            return;

        var add = !stroke.Erase && _tool != Tool.Eraser;
        if (_tool == Tool.Fill)
        {
            if (stroke.Start)
                Fill(pixel, add);

            return;
        }

        // A fast drag skips pixels, so each move draws the line from the last one.
        Line(_last ?? pixel, pixel, add);
        _last = pixel;
        _lineEnd = pixel;
    }

    private void Line(Vector2i from, Vector2i to, bool add)
    {
        switch (_layer)
        {
            case Layer.Erase:
                _sketch.EraseLine(Facing, from, to, add);
                UpdateErase();
                break;
            case Layer.Glow:
                _sketch.GlowLine(_frame, Facing, from, to, add);
                break;
            default:
                _sketch.Line(_frame, Facing, from, to, add ? Ink() : default);
                break;
        }
    }

    private void Fill(Vector2i pixel, bool add)
    {
        if (_layer == Layer.Glow)
            _sketch.GlowFill(_frame, Facing, pixel.X, pixel.Y, add);
        else if (_layer == Layer.Drawing)
            _sketch.Fill(_frame, Facing, pixel.X, pixel.Y, add ? Ink() : default);
    }

    /// <summary>Takes the colour at a pixel: what is drawn there, or else the body showing through.</summary>
    private bool Pick(Vector2i pixel)
    {
        if (Art.GetPixel(_frame, Facing, pixel.X, pixel.Y) is { A: > 0 } drawn)
        {
            SetInk(drawn);
            return true;
        }

        if (!_sampler.TryGetColor(pixel.X, pixel.Y, out var body))
            return false;

        _colour.Color = body;
        _opacity.Value = _opacity.MaxValue;
        return true;
    }

    private void EndStroke()
    {
        _last = null;
        _picking = false;
        _sketch.End();
        Changed();
    }

    /// <summary>Runs one edit as a single undo step.</summary>
    private void Change(Action<CustomMarkingArt> edit)
    {
        _sketch.Change(edit);
        Changed();
    }

    private void Undo()
    {
        _sketch.Undo();
        Changed();
    }

    private void Redo()
    {
        _sketch.Redo();
        Changed();
    }

    /// <summary>Brings everything that follows the art up to date with it.</summary>
    private void Changed()
    {
        UpdateErase();
        UpdateButtons();
        UpdateUsedColours();
    }

    private void UpdateButtons()
    {
        _undoButton.Disabled = !_sketch.CanUndo;
        _redoButton.Disabled = !_sketch.CanRedo;
        UpdateFacingButtons();

        // Undoing can take away the frame that was shown.
        var frames = Art.Frames;
        if (_frame >= frames)
            ShowFrame(frames - 1);

        var animated = frames > 1;
        if (!animated)
            StopPlaying();

        var single = animated ? null : Loc.GetString("wg-custom-marking-editor-frame-needs-two");
        _frameLabel.Text = Loc.GetString("wg-custom-marking-editor-frame-count", ("frame", _frame + 1), ("frames", frames));
        Allow(_removeFrame, Loc.GetString("wg-custom-marking-editor-frame-remove"), single);
        Allow(_playButton, Loc.GetString("wg-custom-marking-editor-frame-play"), single);
        Allow(_reverseFrames, Loc.GetString("wg-custom-marking-editor-frame-reverse"), single);
        Allow(_timeAllButton, Loc.GetString("wg-custom-marking-editor-frame-time-all"), single);
        Allow(_addFrame, Loc.GetString("wg-custom-marking-editor-frame-add"),
            frames >= MaxFrames ? Loc.GetString("wg-custom-marking-editor-frame-max", ("max", MaxFrames)) : null);
        _pingPongButton.Disabled = frames < 3;
        _pingPongButton.Pressed = Art.PingPong;
        _onionSkin.Disabled = !animated;
        _frameTimeRow.Visible = animated;
        _frameStrip.Refresh(Art, Facing, _frame);
        FillFrameTime();
    }

    /// <summary>Offers the colours the marking is drawn in, the most used first, to pick again.</summary>
    private void UpdateUsedColours()
    {
        var counts = new Dictionary<Rgba32, int>();
        for (var frame = 0; frame < Art.Frames; frame++)
        {
            for (var facing = 0; facing < CustomMarkingRules.Facings; facing++)
            {
                for (var y = 0; y < CustomMarkingRules.FrameSize; y++)
                {
                    for (var x = 0; x < CustomMarkingRules.FrameSize; x++)
                    {
                        if (Art.GetPixel(frame, facing, x, y) is { A: > 0 } pixel)
                            counts[pixel] = counts.GetValueOrDefault(pixel) + 1;
                    }
                }
            }
        }

        var used = counts.OrderByDescending(pair => pair.Value).Take(MaxUsedColours).Select(pair => pair.Key).ToList();
        if (used.SequenceEqual(_usedColourList))
            return;

        _usedColourList.Clear();
        _usedColourList.AddRange(used);
        _usedColours.RemoveAllChildren();
        foreach (var pixel in used)
        {
            var colour = CustomMarkingArt.ToColor(pixel);
            var tooltip = Loc.GetString("wg-custom-marking-editor-used-colour",
                ("hex", colour.WithAlpha(1f).ToHex()), ("opacity", MathF.Round(pixel.A * 100f / byte.MaxValue)));
            _usedColours.AddChild(Swatch(colour, tooltip, () => SetInk(pixel)));
        }

        _noUsedColours.Visible = used.Count == 0;
    }

    private void FillFrameTime()
    {
        _fillingTime = true;
        _frameTime.Value = Art.GetFrameTime(_frame) / 1000f;
        _fillingTime = false;
    }

    /// <summary>Shows a frame on the canvas and the facings, without touching whether the frames are playing.</summary>
    private void ShowFrame(int frame)
    {
        _frame = Math.Clamp(frame, 0, Art.Frames - 1);
        _canvas.ArtFrame = _frame;
        foreach (var tile in _facings)
        {
            tile.ArtFrame = _frame;
        }
    }

    /// <summary>Goes to a frame to draw on it, round from the last to the first and back.</summary>
    private void SetFrame(int frame)
    {
        StopPlaying();
        var frames = Art.Frames;
        ShowFrame((frame % frames + frames) % frames);
        UpdateButtons();
    }

    /// <summary>Adds a frame after the one shown, as a copy of it, and goes to it.</summary>
    private void AddFrame()
    {
        StopPlaying();
        var added = -1;
        Change(art => added = art.AddFrame(_frame, MaxFrames));
        if (added >= 0)
            SetFrame(added);
    }

    private void RemoveFrame()
    {
        StopPlaying();
        Change(art => art.RemoveFrame(_frame));
    }

    private void SetFrameTime(float seconds)
    {
        if (_fillingTime)
            return;

        var time = (int) MathF.Round(seconds * 1000);
        Change(art => art.SetFrameTime(_frame, time));
    }

    private void SetPlaying(bool playing)
    {
        _playing = playing && Art.Frames > 1;
        _playTime = 0;
        _playStep = Math.Max(0, Array.IndexOf(Art.PlayOrder(), _frame));
        _playButton.Pressed = _playing;
    }

    private void StopPlaying()
    {
        if (_playing)
            SetPlaying(false);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // The body is read for Alt+click as well as for the picker, so it is kept fresh under the cursor.
        _sampler.Active = _tool == Tool.Picker || _canvas.Hover != null;

        if (!_playing || Art.Frames < 2)
            return;

        // At most one lap a frame: after a hitch the frames skip ahead rather than spin here.
        var order = Art.PlayOrder();
        _playStep %= order.Length;
        _playTime += args.DeltaSeconds;
        for (var i = 0; i < order.Length; i++)
        {
            var shown = Art.GetFrameTime(_frame) / 1000f;
            if (_playTime < shown)
                break;

            _playTime -= shown;
            _playStep = (_playStep + 1) % order.Length;
            ShowFrame(order[_playStep]);
            UpdateButtons();
        }
    }

    /// <summary>
    /// Keyboard shortcuts, taken while this is the window in front, the cursor isn't over something else and no
    /// text box or popup has the keyboard.
    /// </summary>
    private void OnKey(KeyEventArgs key, KeyEventType type)
    {
        if (type == KeyEventType.Up || !TakesShortcuts())
            return;

        switch (key.Key)
        {
            case Keyboard.Key.Z when key.Control:
                if (key.Shift)
                    Redo();
                else
                    Undo();
                break;
            case Keyboard.Key.Y when key.Control:
                Redo();
                break;
            case Keyboard.Key.LBracket when !key.Control && Art.Frames > 1:
                SetFrame(_frame - 1);
                break;
            case Keyboard.Key.RBracket when !key.Control && Art.Frames > 1:
                SetFrame(_frame + 1);
                break;
            default:
                if (type == KeyEventType.Repeat || key.Control || key.Alt || !Shortcut(key.Key))
                    return;
                break;
        }

        key.Handle();
    }

    /// <summary>Runs the single key shortcut for a key, if it has one.</summary>
    private bool Shortcut(Keyboard.Key key)
    {
        switch (key)
        {
            case Keyboard.Key.B:
                SetTool(Tool.Pencil);
                return true;
            case Keyboard.Key.E:
                SetTool(Tool.Eraser);
                return true;
            case Keyboard.Key.F:
                SetTool(Tool.Fill);
                return true;
            case Keyboard.Key.I:
                SetTool(Tool.Picker);
                return true;
            case Keyboard.Key.M:
                SetMirror(!_sketch.Mirror);
                return true;
            case Keyboard.Key.S:
                SetSideMirror(!_sketch.SideMirror);
                return true;
            case Keyboard.Key.Num1:
                SetLayer(Layer.Drawing);
                return true;
            case Keyboard.Key.Num2:
                SetLayer(Layer.Erase);
                return true;
            case Keyboard.Key.Num3:
                SetLayer(Layer.Glow);
                return true;
            case Keyboard.Key.Space when Art.Frames > 1:
                SetPlaying(!_playing);
                return true;
            default:
                return false;
        }
    }

    private bool TakesShortcuts()
    {
        if (!IsOpen || Parent is not { } parent || parent.GetChild(parent.ChildCount - 1) != this)
            return false;

        if (UserInterfaceManager.KeyboardFocused is LineEdit or TextEdit)
            return false;

        // Keys pressed over the game or another window are theirs: in a round these are movement and hotbar keys.
        if (UserInterfaceManager.CurrentlyHovered is { } hovered && !IsAncestorOf(hovered))
            return false;

        foreach (var popup in UserInterfaceManager.ModalRoot.Children)
        {
            if (popup.Visible)
                return false;
        }

        return true;
    }

    private bool IsAncestorOf(Control control)
    {
        for (var at = control; at != null; at = at.Parent)
        {
            if (at == this)
                return true;
        }

        return false;
    }

    /// <summary>The server turned the reach limit on or off while the editor is open.</summary>
    private void OnBodyReachChanged(bool _)
    {
        UpdatePlacement();
    }

    private void UpdatePlacement()
    {
        _canvas.Placement = Placement;
        var key = PlacementName(Placement);
        var hint = Loc.GetString(key + "-hint");
        if (_system.BodyReach)
            hint += "\n" + Loc.GetString("wg-custom-marking-editor-reach", ("margin", CustomMarkingSections.Margin));

        _placementSummary.Text = Loc.GetString(key + "-short");
        _placementSummary.ToolTip = hint;
        _placement.ToolTip = hint;
        UpdateSections();
    }

    /// <summary>
    /// Keeps drawing to the body shown and the margin around it, and darkens the rest of the canvas, while the
    /// server holds markings to the body.
    /// </summary>
    private void UpdateSections()
    {
        byte[]? sections = null;
        if (_system.BodyReach && _body is { } body && _entMan.TryGetComponent(body, out SpriteComponent? sprite))
            sections = _system.GetSections((body, sprite), Placement);

        _canvas.Sections = sections;
        foreach (var preview in Previews)
        {
            SetUpPreview(preview);
        }

        _sketch.Allowed = sections == null
            ? null
            : (facing, x, y) => sections[CustomMarkingSections.Index(facing, x, y)] != CustomMarkingSections.None;
    }

    /// <summary>
    /// Works out what is erased from the body shown, which the canvases draw it without, and says so when it is
    /// more than a body may lose.
    /// </summary>
    private void UpdateErase()
    {
        if (!_system.EraseEnabled)
            return;

        _othersErase.CopyTo(_erase, 0);
        CustomMarkingErase.Add(_erase, Art.Erase);

        var tooMuch = false;
        if (Art.HasErase()
            && _body is { } body
            && _entMan.TryGetComponent(body, out SpriteComponent? sprite)
            && _system.GetBodyMask((body, sprite)) is { } bodyMask)
        {
            var covered = Art.Solid();
            CustomMarkingErase.Add(covered, _othersSolid);
            tooMuch = !CustomMarkingErase.LeavesEnough(bodyMask, _erase, covered);
        }

        if (tooMuch == _erasedTooMuch)
            return;

        _erasedTooMuch = tooMuch;
        if (tooMuch)
            SetStatus(Loc.GetString("wg-custom-marking-editor-erase-too-much", ("percent", CustomMarkingErase.MinKeptPercent)), true);
        else
            SetStatus(string.Empty, false);
    }

    /// <summary>Remakes the doll drawn under the art, dressed for the character's job or not.</summary>
    private void ReloadBody()
    {
        DeleteBody();
        if (_profile != null)
            _body = _lobby.LoadProfileEntity(_profile, null, _showClothes.Pressed);

        UpdateBody();
        UpdateMirror();
        UpdateSections();
    }

    private void UpdateBody()
    {
        var shown = _showBody.Pressed ? _body : null;
        var erase = _system.EraseEnabled ? _erase : null;
        var hideHair = !_showHair.Pressed;
        _canvas.Body = shown;
        _canvas.Erase = erase;
        _canvas.HideHair = hideHair;
        _sampler.Body = shown;
        _sampler.Erase = erase;
        _sampler.HideHair = hideHair;
        foreach (var preview in Previews)
        {
            SetUpPreview(preview);
        }
    }

    /// <summary>Shows a small picture over the body as the canvas shows it.</summary>
    private void SetUpPreview(CustomMarkingCanvas preview)
    {
        preview.Body = _canvas.Body;
        preview.Erase = _canvas.Erase;
        preview.HideHair = _canvas.HideHair;
        preview.Placement = _canvas.Placement;
        preview.Sections = _canvas.Sections;
    }

    private void DeleteBody()
    {
        var body = _body;
        _body = null;
        UpdateBody();
        if (body is { } doll)
            _entMan.DeleteEntity(doll);
    }

    private void Save()
    {
        if (_request >= 0)
            return;

        if (Art.IsBlank() && !Art.HasErase())
        {
            SetStatus(Loc.GetString("wg-custom-marking-error-blank"), true);
            return;
        }

        // Unchanged art isn't sent again: the server keeps what the entry has.
        var changed = _entry == null || !Art.Same(_opened);
        _request = _system.Save(_entry?.Id ?? 0, CustomMarkingRules.CleanName(_name.Text), Placement, changed ? Art : null, Slot);
        _saveButton.Disabled = true;
        SetStatus(Loc.GetString("wg-custom-marking-editor-saving"), false);
    }

    private void OnSaveAnswered(CustomMarkingSaveResultEvent ev)
    {
        if (ev.Request != _request)
            return;

        _request = -1;
        _saveButton.Disabled = false;
        if (ev.Entry is not { } saved)
        {
            SetStatus(Loc.GetString(ev.Error ?? "wg-custom-marking-error-failed"), true);
            return;
        }

        _system.Remember(saved.Hash, Art);
        OnSaved?.Invoke(_entry, saved);
        _closeConfirmed = true;
        Close();
    }

    /// <summary>Whether the art, name or placement differ from what the editor opened with.</summary>
    private bool HasUnsavedChanges => !Art.Same(_opened) || CustomMarkingRules.CleanName(_name.Text) != _openedName ||
                                      Placement != _openedPlacement;

    /// <summary>Asks before closing over unsaved edits: save, discard, or keep editing.</summary>
    public override void Close()
    {
        if (_closeConfirmed || !HasUnsavedChanges)
        {
            base.Close();
            return;
        }

        if (_closePrompt is { IsOpen: true })
        {
            _closePrompt.MoveToFront();
            return;
        }

        var prompt = new CustomMarkingCloseWindow();
        prompt.OnSave += () =>
        {
            prompt.Close();
            Save();
        };
        prompt.OnDiscard += () =>
        {
            prompt.Close();
            _closeConfirmed = true;
            Close();
        };
        prompt.OnClose += () =>
        {
            if (_closePrompt == prompt)
                _closePrompt = null;
        };
        _closePrompt = prompt;
        prompt.OpenCentered();
    }

    private void SetStatus(string message, bool error)
    {
        _status.Text = message;
        if (error)
            _status.StyleClasses.Add("Danger");
        else
            _status.StyleClasses.Remove("Danger");
    }
}
