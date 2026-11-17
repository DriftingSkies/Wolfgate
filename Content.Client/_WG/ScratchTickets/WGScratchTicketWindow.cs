using System.Linq;
using System.Numerics;
using Content.Client.Resources;
using Content.Shared._NF.Bank;
using Content.Shared._WG.ScratchTickets;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Graphics;
using Robust.Shared.Input;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WG.ScratchTickets;

/// <summary>
/// A scratch ticket drawn as the card itself, with no window frame: drag it by the card, close it at the corner.
/// </summary>
public sealed partial class WGScratchTicketWindow : BaseWindow
{
    private const string TitleFontPath = "/Fonts/_Goobstation/ChelseaMarket/ChelseaMarket.ttf";
    private const string BoldFontPath = "/Fonts/NotoSans/NotoSans-Bold.ttf";
    private const string PlainFontPath = "/Fonts/NotoSans/NotoSans-Regular.ttf";
    private const float ScratchSoundGap = 0.3f;
    private const float LegendRow = 32f;
    private const float SpriteSize = 32f;

    // The strip over the player's symbols that holds the winning one, and the strip beside each game that names it.
    private const float KeyBand = 88f;
    private const float GameLabel = 60f;

    private static readonly SoundSpecifier ScratchSound = new SoundCollectionSpecifier("PaperScribbles", AudioParams.Default.WithVolume(-8f));

    private static readonly Color Gold = Color.FromHex("#FFD23E");
    private static readonly Color TitleEdge = Color.FromHex("#3A1F00");
    private static readonly Color Cream = Color.FromHex("#F6EFD8");
    private static readonly Color Ink = Color.FromHex("#2A2418");
    private static readonly Color PrizeInk = Color.FromHex("#9C1A12");
    private static readonly Color CellEdge = Color.FromHex("#C8BA98");

    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IResourceCache _resources = default!;

    private readonly WGScratchFoil _foil = new();
    private readonly Font _titleFont;
    private readonly Font _ribbonFont;
    private readonly Font _boldFont;
    private readonly Font _legendFont;
    private readonly Font _prizeFont;
    private readonly Font _taglineFont;
    private readonly Font _smallFont;

    private OwnedTexture? _art;
    private List<WGScratchSymbol> _symbols = new();
    private Texture[] _icons = Array.Empty<Texture>();
    private int[] _cells = Array.Empty<int>();
    private string _serial = string.Empty;
    private string _title = string.Empty;
    private string _tagline = string.Empty;
    private string _dud = string.Empty;
    private WGScratchCardArt.Palette _palette;
    private Color _shade = Color.Black;

    // Sprites are drawn at whole multiples of their size and may overhang their slot with their empty margin.
    private float _legendIcon = SpriteSize;
    private float _cellIcon = SpriteSize;
    private TimeSpan _nextScratchSound;
    private WGScratchFormat _format;
    private int _prize;
    private bool _stateSeen;
    private bool _scratched;

    /// <summary>The player scratched enough foil off.</summary>
    public event Action? OnRevealed;

    public WGScratchTicketWindow()
    {
        IoCManager.InjectDependencies(this);

        _titleFont = _resources.GetFont(TitleFontPath, 42);
        _ribbonFont = _resources.GetFont(BoldFontPath, 13);
        _boldFont = _resources.GetFont(BoldFontPath, 14);
        _legendFont = _resources.GetFont(BoldFontPath, 13);
        _prizeFont = _resources.GetFont(BoldFontPath, 12);
        _taglineFont = _resources.GetFont(BoldFontPath, 11);
        _smallFont = _resources.GetFont(PlainFontPath, 10);

        MouseFilter = MouseFilterMode.Stop;
        AddChild(_foil);

        _foil.Scratched += OnScratched;
        _foil.Revealed += () => OnRevealed?.Invoke();
    }

    /// <summary>Takes the ticket's symbols and serial number and puts its saved foil on.</summary>
    public void SetTicket(EntityUid uid, WGScratchTicketComponent ticket, byte[] alpha)
    {
        var sprites = _entMan.System<SpriteSystem>();
        _symbols = ticket.Symbols;
        _icons = _symbols.Select(symbol => sprites.Frame0(symbol.Icon)).ToArray();
        _serial = ((uint) uid.Id * 2654435761u % 1000000u).ToString("D6");
        _title = Loc.GetString(ticket.Title);
        _tagline = Loc.GetString(ticket.Tagline);
        _dud = Loc.GetString(ticket.Dud);
        _format = ticket.Format;
        _legendIcon = SpriteSize * ticket.LegendIconScale;
        _cellIcon = SpriteSize * ticket.CellIconScale;
        _palette = new WGScratchCardArt.Palette(ticket.CardLight, ticket.CardDark, ticket.CardGlow, ticket.Ribbon);
        _shade = new Color(ticket.CardDark.R * 0.4f, ticket.CardDark.G * 0.4f, ticket.CardDark.B * 0.4f);
        _foil.SetAlpha(alpha);
        PaintArt();
    }

    public void UpdateState(WGScratchTicketState state)
    {
        _cells = state.Cells;

        // A ticket opened already scratched shows no foil; one finished just now loses what is left of it.
        if (state.Scratched && !_stateSeen)
            _foil.Visible = false;
        else if (state.Scratched)
            _foil.Clear();

        _stateSeen = true;
        _scratched = state.Scratched;
        _prize = state.Prize;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _foil.Measure(WGScratchCardArt.Foil.Size);
        return new Vector2(WGScratchCardArt.Width, WGScratchCardArt.Height);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        _foil.Arrange(WGScratchCardArt.Foil);
        return finalSize;
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();

        var load = TextureLoadParameters.Default;
        load.SampleParameters = new TextureSampleParameters { Filter = true };
        _art = _clyde.CreateBlankTexture<Rgba32>((WGScratchCardArt.PixelWidth, WGScratchCardArt.PixelHeight), "wg-scratch-card", load);
        PaintArt();
    }

    private void PaintArt()
    {
        // Nothing to print until the ticket is known.
        if (_symbols.Count > 0)
            _art?.SetSubImage<Rgba32>(Vector2i.Zero, (WGScratchCardArt.PixelWidth, WGScratchCardArt.PixelHeight), WGScratchCardArt.Paint(_palette));
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();

        _art?.Dispose();
        _art = null;
    }

    protected override DragMode GetDragModeFor(Vector2 relativeMousePos)
    {
        return DragMode.Move;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        if (args.Function == EngineKeyFunctions.UIClick && WGScratchCardArt.Close.Contains(args.RelativePosition))
        {
            args.Handle();
            Close();
            return;
        }

        base.KeyBindDown(args);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_art != null)
            handle.DrawTextureRect(_art, PixelSizeBox);

        DrawCloseMark(handle);

        // The title sits on its own shadow, as if stamped in foil.
        var centre = WGScratchCardArt.Width / 2f;
        DrawText(handle, _titleFont, _title, centre, 15f, _shade, new TextOutline(3f * UIScale, _shade), 270f);
        DrawText(handle, _titleFont, _title, centre, 11f, Gold, new TextOutline(3f * UIScale, TitleEdge), 270f);

        var top = _symbols.Count == 0 ? 0 : _symbols.Max(symbol => symbol.Prize);
        DrawText(handle, _ribbonFont, Loc.GetString("wg-scratch-ticket-card-top-prize", ("amount", BankSystemExtensions.ToSpesoString(top))),
            centre, WGScratchCardArt.Ribbon.Top + 1f, Color.White, new TextOutline(1f * UIScale, TitleEdge), WGScratchCardArt.Ribbon.Width - 30f);

        DrawLegend(handle);
        DrawText(handle, _taglineFont, _tagline, centre, WGScratchCardArt.Legend.Bottom + 6f,
            Cream, new TextOutline(1f * UIScale, _shade), WGScratchCardArt.Width - 44f);

        DrawCells(handle);

        var bottom = WGScratchCardArt.Frame.Bottom;
        DrawText(handle, _prizeFont, Status(), centre, bottom + 5f, Gold, new TextOutline(1f * UIScale, _shade), WGScratchCardArt.Width - 30f);

        var right = (WGScratchCardArt.Barcode.Right + WGScratchCardArt.Width - 14f) / 2f;
        DrawText(handle, _prizeFont, Loc.GetString("wg-scratch-ticket-card-serial", ("serial", _serial)), right, bottom + 27f, Cream, null, 180f);
        DrawText(handle, _smallFont, Loc.GetString("wg-scratch-ticket-card-small-print-issuer"), right, bottom + 44f, Cream, null, 180f);
        DrawText(handle, _smallFont, Loc.GetString("wg-scratch-ticket-card-small-print-redeem"), centre, bottom + 66f, Cream.WithAlpha(0.8f), null, WGScratchCardArt.Width - 30f);
    }

    private void DrawCloseMark(DrawingHandleScreen handle)
    {
        var box = WGScratchCardArt.Close;
        var inset = new Vector2(6f, 6f);
        for (var offset = -1; offset <= 1; offset++)
        {
            var nudge = new Vector2(offset, 0f);
            handle.DrawLine((box.TopLeft + inset) * UIScale + nudge, (box.BottomRight - inset) * UIScale + nudge, Cream);
            handle.DrawLine((box.TopRight + inset * new Vector2(-1f, 1f)) * UIScale + nudge, (box.BottomLeft + inset * new Vector2(1f, -1f)) * UIScale + nudge, Cream);
        }
    }

    /// <summary>The prize table: best prizes first, down the left column then the right.</summary>
    private void DrawLegend(DrawingHandleScreen handle)
    {
        var box = WGScratchCardArt.Legend;
        var prizes = Enumerable.Range(0, _symbols.Count)
            .Where(index => _symbols[index].Prize > 0)
            .OrderByDescending(index => _symbols[index].Prize)
            .ToList();
        var rows = Math.Max(1, (prizes.Count + 1) / 2);
        var top = box.Top + (box.Height - rows * LegendRow) / 2f;

        for (var i = 0; i < prizes.Count; i++)
        {
            var symbol = prizes[i];
            var corner = new Vector2(box.Left + 8f + (i / rows) * box.Width / 2f, top + i % rows * LegendRow);
            var overhang = new Vector2(_legendIcon - LegendRow, _legendIcon - LegendRow) / 2f;
            handle.DrawTextureRect(_icons[symbol], UIBox2.FromDimensions(Snap((corner - overhang) * UIScale), new Vector2(_legendIcon, _legendIcon) * UIScale));

            // A ticket that wins on a single find prints the prize alone.
            var at = Snap((corner + new Vector2(LegendRow + 8f, 7f)) * UIScale);
            var count = WGScratchTicketRules.Match(_format);
            if (count > 1)
            {
                var advance = handle.DrawString(_legendFont, at, Loc.GetString("wg-scratch-ticket-card-match", ("count", count)), UIScale, Ink);
                at = Snap(at + new Vector2(advance.X + 4f * UIScale, 0f));
            }

            handle.DrawString(_legendFont, at, BankSystemExtensions.ToSpesoString(_symbols[symbol].Prize), UIScale, PrizeInk);
        }
    }

    /// <summary>What the foil hides: each cell's symbol over its prize, the winning match on gold.</summary>
    private void DrawCells(DrawingHandleScreen handle)
    {
        DrawCellLabels(handle);

        for (var i = 0; i < _cells.Length && i < WGScratchTicketRules.CellCount(_format); i++)
        {
            var symbol = _cells[i];
            if (symbol < 0 || symbol >= _symbols.Count)
                continue;

            var box = CellBox(i);
            var cell = UIBox2.FromDimensions(box.TopLeft * UIScale, box.Size * UIScale);
            if (_scratched && WGScratchTicketRules.Wins(_format, _cells, i))
                handle.DrawRect(cell, Gold);
            else if (_format == WGScratchFormat.WinningSymbol && i == 0)
                handle.DrawRect(cell, CellEdge.WithAlpha(0.45f));

            handle.DrawRect(cell, CellEdge, false);
            handle.DrawTextureRect(_icons[symbol],
                UIBox2.FromDimensions(Snap((box.TopLeft + new Vector2((box.Width - _cellIcon) / 2f, (box.Height - _cellIcon) / 2f - 7f)) * UIScale), new Vector2(_cellIcon, _cellIcon) * UIScale));

            var prize = _symbols[symbol].Prize;
            DrawText(handle, _prizeFont, prize > 0 ? BankSystemExtensions.ToSpesoString(prize) : _dud,
                box.Center.X, box.Bottom - 20f, prize > 0 ? PrizeInk : Ink, null, box.Width - 6f);
        }
    }

    /// <summary>Where a cell sits on the card, for the ticket's format.</summary>
    private UIBox2 CellBox(int index)
    {
        var foil = WGScratchCardArt.Foil;
        switch (_format)
        {
            case WGScratchFormat.WinningSymbol:
            {
                var size = new Vector2(foil.Width / WGScratchTicketRules.Columns, KeyBand);
                if (index == 0)
                    return UIBox2.FromDimensions(foil.TopLeft + new Vector2((foil.Width - size.X) / 2f, 0f), size);

                var own = index - 1;
                size.Y = (foil.Height - KeyBand) / (WGScratchTicketRules.OwnSymbols / WGScratchTicketRules.Columns);
                var at = new Vector2(own % WGScratchTicketRules.Columns * size.X, KeyBand + own / WGScratchTicketRules.Columns * size.Y);
                return UIBox2.FromDimensions(foil.TopLeft + at, size);
            }
            case WGScratchFormat.Pairs:
            {
                var size = new Vector2((foil.Width - GameLabel) / 2f, foil.Height / WGScratchTicketRules.Games);
                return UIBox2.FromDimensions(foil.TopLeft + new Vector2(GameLabel + index % 2 * size.X, index / 2 * size.Y), size);
            }
            default:
            {
                var size = new Vector2(foil.Width / WGScratchTicketRules.Columns, foil.Height / WGScratchTicketRules.Columns);
                return UIBox2.FromDimensions(foil.TopLeft + new Vector2(index % WGScratchTicketRules.Columns, index / WGScratchTicketRules.Columns) * size, size);
            }
        }
    }

    /// <summary>The words printed between the cells: beside the winning symbol, or naming each game.</summary>
    private void DrawCellLabels(DrawingHandleScreen handle)
    {
        var foil = WGScratchCardArt.Foil;
        switch (_format)
        {
            case WGScratchFormat.WinningSymbol:
                var side = foil.Width / WGScratchTicketRules.Columns;
                var top = foil.Top + KeyBand / 2f - 10f;
                DrawText(handle, _boldFont, Loc.GetString("wg-scratch-ticket-card-key-left"), foil.Left + side / 2f, top, Ink, null, side - 8f);
                DrawText(handle, _boldFont, Loc.GetString("wg-scratch-ticket-card-key-right"), foil.Right - side / 2f, top, Ink, null, side - 8f);
                break;

            case WGScratchFormat.Pairs:
                var row = foil.Height / WGScratchTicketRules.Games;
                for (var game = 0; game < WGScratchTicketRules.Games; game++)
                {
                    DrawText(handle, _prizeFont, Loc.GetString("wg-scratch-ticket-card-game", ("number", game + 1)),
                        foil.Left + GameLabel / 2f, foil.Top + game * row + row / 2f - 9f, Ink, null, GameLabel - 8f);
                }

                break;
        }
    }

    /// <summary>Draws a line of text centred on <paramref name="centre"/>, shrunk to fit <paramref name="maxWidth"/>.</summary>
    private void DrawText(DrawingHandleScreen handle, Font font, string text, float centre, float top, Color colour, TextOutline? outline, float maxWidth)
    {
        var scale = UIScale;
        var width = handle.GetDimensions(font, text, scale).X;
        if (width > maxWidth * UIScale)
        {
            scale *= maxWidth * UIScale / width;
            width = maxWidth * UIScale;
        }

        handle.DrawString(font, Snap(new Vector2(centre * UIScale - width / 2f, top * UIScale)), text, scale, colour, outline);
    }

    /// <summary>Whole pixels: text and pixel art drawn between pixels come out uneven.</summary>
    private static Vector2 Snap(Vector2 position)
    {
        return new Vector2(MathF.Round(position.X), MathF.Round(position.Y));
    }

    private string Status()
    {
        if (!_scratched)
            return string.Empty;

        return _prize > 0
            ? Loc.GetString("wg-scratch-ticket-card-won", ("amount", BankSystemExtensions.ToSpesoString(_prize)))
            : Loc.GetString("wg-scratch-ticket-card-lost");
    }

    private void OnScratched()
    {
        if (_timing.RealTime < _nextScratchSound)
            return;

        _nextScratchSound = _timing.RealTime + TimeSpan.FromSeconds(ScratchSoundGap);
        _entMan.System<SharedAudioSystem>().PlayGlobal(ScratchSound, Filter.Local(), false);
    }
}
