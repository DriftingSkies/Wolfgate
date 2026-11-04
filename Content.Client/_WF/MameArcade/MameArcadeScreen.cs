using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.MameArcade;

/// <summary>Shows the game's frames on a 4:3 screen and takes the keyboard while focused.</summary>
public sealed partial class MameArcadeScreen : Control
{
    private const float DisplayAspect = 4f / 3f;

    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IInputManager _input = default!;

    private readonly Dictionary<Keyboard.Key, MameControls> _held = new();
    private OwnedTexture? _texture;
    private Vector2i _size;

    /// <summary>The controls the local player is holding.</summary>
    public MameControls Held
    {
        get
        {
            var held = MameControls.None;
            foreach (var control in _held.Values)
            {
                held |= control;
            }

            return held;
        }
    }

    public MameArcadeScreen()
    {
        IoCManager.InjectDependencies(this);

        CanKeyboardFocus = true;
        KeyboardFocusOnClick = true;
        MouseFilter = MouseFilterMode.Stop;
        HorizontalExpand = true;
        VerticalExpand = true;
    }

    public void SetFrame(Rgba32[] pixels, int width, int height)
    {
        if (_texture == null || _size != (width, height))
        {
            _texture?.Dispose();
            _size = (width, height);
            _texture = _clyde.CreateBlankTexture<Rgba32>(_size, "mame-arcade");
        }

        _texture.SetSubImage<Rgba32>(Vector2i.Zero, _size, pixels);
    }

    public void Clear()
    {
        _texture?.Dispose();
        _texture = null;
        _held.Clear();
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        _input.FirstChanceOnKeyEvent += OnKeyEvent;
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        _input.FirstChanceOnKeyEvent -= OnKeyEvent;
        Clear();
    }

    protected override void KeyboardFocusExited()
    {
        base.KeyboardFocusExited();
        _held.Clear();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        handle.DrawRect(PixelSizeBox, Color.Black);
        if (_texture == null)
            return;

        var width = Math.Min(PixelWidth, PixelHeight * DisplayAspect);
        var height = width / DisplayAspect;
        var left = (PixelWidth - width) / 2f;
        var top = (PixelHeight - height) / 2f;
        handle.DrawTextureRect(_texture, new UIBox2(left, top, left + width, top + height));
    }

    private void OnKeyEvent(KeyEventArgs args, KeyEventType type)
    {
        if (!Visible)
            return;

        if (type == KeyEventType.Up)
        {
            if (_held.Remove(args.Key))
                args.Handle();
            return;
        }

        if (!HasKeyboardFocus())
            return;

        var control = MameArcadeKeys.ToControl(args.Key);
        if (control == MameControls.None)
            return;

        args.Handle();
        _held[args.Key] = control;
    }
}
