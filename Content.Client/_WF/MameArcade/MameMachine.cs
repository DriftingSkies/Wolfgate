using System.IO;
using Content.Client._WF.MameArcade.Cps2;
using Content.Client._WF.MameArcade.Engine;
using Content.Client._WF.MameArcade.NeoGeo;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.MameArcade;

/// <summary>
/// Drives MAME.NET's emulator, which keeps all its state in statics: one machine per client, used only from the
/// session's worker thread.
/// </summary>
public static class MameMachine
{
    public const string NeoGeoBoard = "Neo Geo";
    public const string Cps2Board = "CPS2";

    // Neo Geo's visible area in MAME.NET's 384-pixel-wide frame buffer.
    private const int NeoGeoLeft = 30;
    private const int NeoGeoTop = 16;
    private const int NeoGeoWidth = 320;
    private const int NeoGeoHeight = 224;

    // CPS2's visible area in its 512-pixel-wide frame buffer, which MAME.NET draws 0x100 lines down.
    private const int Cps2Left = 64;
    private const int Cps2Top = 0x100 + 16;
    private const int Cps2Width = 384;
    private const int Cps2Height = 224;

    private static bool _running;

    public static int Width => Video.width;
    public static int Height => Video.height;

    /// <summary>Seconds of emulated time per frame.</summary>
    public static double FrameSeconds => Video.frame_update_time.attoseconds / 1e18;

    /// <summary>Loads a game's ROMs from the player's zips and boots it.</summary>
    /// <param name="nvram">Where battery RAM persists; null keeps none, as both players of a 2-player game must.</param>
    /// <param name="audio">Receives 16-bit stereo samples at 48 kHz.</param>
    public static void Start(IMameGame game, IMameChipSource chips, INvramStore? nvram, Action<byte[], int>? audio)
    {
        switch (game)
        {
            case NeoGeoSet neo:
                StartNeoGeo(neo, NeoGeoLoader.Load(neo, chips), nvram, audio);
                break;
            case Cps2Set cps2:
                StartCps2(cps2, Cps2Loader.Load(cps2, chips), audio);
                break;
            default:
                throw new InvalidOperationException($"No board runs {game.Name}.");
        }
    }

    /// <summary>Boots a Neo Geo game from region files built by <see cref="NeoGeoLoader"/>.</summary>
    public static void StartNeoGeo(NeoGeoSet set, NeoGeoImage image, INvramStore? nvram, Action<byte[], int>? audio)
    {
        Begin(set, NeoGeoBoard, image.Regions, image.Bios, nvram, audio);
        Frame(NeoGeoLeft, NeoGeoTop, NeoGeoWidth, NeoGeoHeight);

        Neogeo.NeogeoInit();
        Boot(set);
    }

    /// <summary>Boots a CPS2 game from region files built by <see cref="Cps2Loader"/>. CPS2 keeps no battery RAM.</summary>
    public static void StartCps2(Cps2Set set, Dictionary<string, byte[]> regions, Action<byte[], int>? audio)
    {
        Begin(set, Cps2Board, regions, new Dictionary<string, byte[]>(), null, audio);
        Frame(Cps2Left, Cps2Top, Cps2Width, Cps2Height);

        CPS.CPSInit();
        Boot(set);
    }

    private static void Begin(
        IMameGame game,
        string board,
        Dictionary<string, byte[]> regions,
        Dictionary<string, byte[]> bios,
        INvramStore? nvram,
        Action<byte[], int>? audio)
    {
        if (_running)
            Stop();

        ResetStaleState();
        Engine.Timer.lt = new List<Engine.Timer.emu_timer>();
        Machine.rom = new RomInfo
        {
            Name = game.Name,
            Board = board,
            Parent = game.Parent,
            Direction = string.Empty,
            Description = game.Title,
            Year = game.Year,
            Manufacturer = game.Manufacturer,
        };
        RomInfo.Rom = Machine.rom;
        Machine.sName = game.Name;
        Machine.sParent = game.Parent;
        Machine.sBoard = board;
        Machine.sDirection = string.Empty;
        Machine.sDescription = game.Title;
        Machine.sManufacturer = game.Manufacturer;
        Machine.lsParents = string.IsNullOrEmpty(game.Parent) ? new List<string> { game.Name } : new List<string> { game.Name, game.Parent };
        Machine.RomSource = name => regions.GetValueOrDefault(name);
        Machine.BiosSource = name => bios.GetValueOrDefault(name);
        Machine.Nvram = nvram;
        Engine.Sound.AudioSink = audio;
        Keyboard.Reset();
        Mame.is_foreground = true;
        Mame.paused = false;
        Mame.exit_pending = false;
    }

    /// <summary>The visible part of the board's frame buffer.</summary>
    private static void Frame(int left, int top, int width, int height)
    {
        Video.iMode = 0;
        Video.offsetx = left;
        Video.offsety = top;
        Video.width = width;
        Video.height = height;
    }

    private static void Boot(IMameGame game)
    {
        if (!Machine.bRom)
            throw new InvalidOperationException($"{game.Name} is missing a region MAME.NET needs.");

        Mame.init_machine();
        Generic.nvram_load();
        Mame.soft_reset();
        _running = true;
    }

    /// <summary>Emulates one frame with both seats' controls held.</summary>
    public static void RunFrame(MameControls playerOne, MameControls playerTwo)
    {
        MameArcadeKeys.Apply(playerOne, playerTwo, Keyboard.Held);
        Mame.run_frame();
    }

    /// <summary>Copies the visible area of the last frame as RGBA.</summary>
    public static void CopyFrame(Rgba32[] pixels)
    {
        var source = Video.bitmapcolor;
        var stride = Video.fullwidth;
        for (var y = 0; y < Video.height; y++)
        {
            var from = (Video.offsety + y) * stride + Video.offsetx;
            var to = y * Video.width;
            for (var x = 0; x < Video.width; x++)
            {
                var argb = source[from + x];
                pixels[to + x] = new Rgba32((byte) (argb >> 16), (byte) (argb >> 8), (byte) argb, 255);
            }
        }
    }

    /// <summary>A hash of the work RAM, which drifts at once if two 2-player games stop matching.</summary>
    public static uint Checksum()
    {
        var hash = 2166136261u;
        hash = Hash(hash, Memory.mainram);
        hash = Hash(hash, Memory.audioram);
        return hash;
    }

    public static byte[] SaveState()
    {
        using var stream = new MemoryStream();
        State.savestate_callback(new Engine.BinaryWriter(stream));
        return stream.ToArray();
    }

    public static void LoadState(byte[] state)
    {
        using var stream = new MemoryStream(state);
        State.loadstate_callback(new BinaryReader(stream));
        Mame.postload();
    }

    /// <summary>Saves battery RAM and detaches the frontend.</summary>
    public static void Stop()
    {
        if (_running)
            Generic.nvram_save();

        _running = false;
        Engine.Sound.AudioSink = null;
        Machine.RomSource = null;
        Machine.BiosSource = null;
        Machine.Nvram = null;

        // The engine keeps the last game's ROMs in statics; the biggest Neo Geo sets hold hundreds of MB.
        Memory.mainrom = null;
        Memory.audiorom = null;
        Neogeo.spritesrom = null;
        Neogeo.sprite_gfx = null;
        Neogeo.fixedrom = null;
        FM.ymsndrom = null;
        YMDeltat.ymsnddeltatrom = null;
        YM2610.F2610.pcmbuf = null;
        CPS.gfxrom = null;
        CPS.gfx1rom = null;
        CPS.mainromop = null;
        CPS.audioromop = null;
        QSound.qsoundrom = null;
    }

    /// <summary>
    /// MAME.NET leaves some state from the last game in place, and video_init schedules the first vblank from it.
    /// Two players' emulators must boot identically, so clear it first.
    /// </summary>
    private static void ResetStaleState()
    {
        Video.screenstate = default;
        Video.new_clip = default;
        Video.frame_completed = false;
        Video.curbitmap = 0;
        Pd4900a.shiftlo = 0;
        Pd4900a.shifthi = 0;
        Pd4900a.retraces = 0;
        Pd4900a.testwaits = 0;
        Pd4900a.testbit = 0;
        Pd4900a.outputbit = 0;
        Pd4900a.bitno = 0;
        Pd4900a.reading = 0;
        Pd4900a.writting = 0;
        Pd4900a.clock_line = 0;
        Pd4900a.command_line = 0;
    }

    private static uint Hash(uint hash, byte[]? data)
    {
        if (data == null)
            return hash;

        foreach (var value in data)
        {
            hash = (hash ^ value) * 16777619u;
        }

        return hash;
    }
}
