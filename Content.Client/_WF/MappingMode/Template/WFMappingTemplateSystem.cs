using System.IO;
using System.Numerics;
using Content.Client._WF.MappingMode.Modes;
using Content.Client._WF.MappingMode.Screen;
using Content.Client._WF.MappingTools;
using Content.Client.Popups;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Shared._WF.MappingMode;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.ContentPack;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.MappingMode.Template;

/// <summary>
/// The mapping screen's PNG template: one image at a time, loaded through the file dialog, drawn by
/// <see cref="WFMappingTemplateOverlay"/> and moved by <see cref="WFMappingTemplateMode"/>. The player's own image and
/// its settings are kept per session name in the client data folder and come back when a session of that name is
/// entered. A template shared with the session (<c>WFMappingTemplateSystem.Share.cs</c>) is shown in its place.
/// </summary>
public sealed partial class WFMappingTemplateSystem : EntitySystem
{
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IFileDialogManager _dialogs = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private MappingZLevelsSystem _zLevels = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private WFMappingBuildModeSystem _modes = default!;
    [Dependency] private WFMappingClientSystem _client = default!;

    private static readonly ResPath Root = new("/Mapping/Templates");
    private const string ImageFile = "template.png";
    private const string SettingsFile = "settings.yml";
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(1);

    private readonly List<int> _depths = new();
    private WFMappingTemplateMode _mode = default!;
    private string? _sessionName;
    private ResPath? _folder;
    private bool _dirty;
    private TimeSpan _nextSave;
    private bool _picking;
    private WFMappingTemplatePanel? _panel;
    private WFMappingDockSlot? _slot;

    private Texture? _localTexture;
    private byte[]? _localBytes;
    private readonly WFMappingTemplateSettings _localSettings = new();

    /// <summary>The decoded image shown: the shared one when there is one, else the player's own, or null.</summary>
    public Texture? Texture => SharedActive ? _sharedTexture : _localTexture;

    /// <summary>Where and how the shown image is drawn; call <see cref="Modified"/> after changing it.</summary>
    public WFMappingTemplateSettings Settings => SharedActive ? _sharedSettings : _localSettings;

    public bool HasTemplate => Texture != null;

    /// <summary>Whether the player has an image of their own loaded, shown or behind a shared one.</summary>
    public bool HasLocal => _localTexture != null;

    /// <summary>Whether a session is entered, so a template can be loaded.</summary>
    public bool CanLoad => _folder != null && !_picking;

    /// <summary>Raised when the image or a setting changes.</summary>
    public event Action? Changed;

    public override void Initialize()
    {
        base.Initialize();

        _mode = new WFMappingTemplateMode(_modes, this);
        _modes.RegisterHandler(_mode);
        _client.SessionEnded += OnSessionEnded;
        InitializeShare();

        var gameplay = _ui.GetUIController<GameplayStateLoadController>();
        gameplay.OnScreenLoad += OnScreenLoad;
        gameplay.OnScreenUnload += OnScreenUnload;

        CommandBinds.Builder
            .Bind(WFMappingKeyFunctions.WFMappingTemplateNudgeUp, Nudge(0, 1))
            .Bind(WFMappingKeyFunctions.WFMappingTemplateNudgeDown, Nudge(0, -1))
            .Bind(WFMappingKeyFunctions.WFMappingTemplateNudgeLeft, Nudge(-1, 0))
            .Bind(WFMappingKeyFunctions.WFMappingTemplateNudgeRight, Nudge(1, 0))
            .Register<WFMappingTemplateSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        CommandBinds.Unregister<WFMappingTemplateSystem>();
        _client.SessionEnded -= OnSessionEnded;
        ShutdownShare();
        var gameplay = _ui.GetUIController<GameplayStateLoadController>();
        gameplay.OnScreenLoad -= OnScreenLoad;
        gameplay.OnScreenUnload -= OnScreenUnload;
        OnScreenUnload();
        LeaveSession();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // Entering a session, or its name changing, switches to that name's template.
        if (_client.SetUp && _client.Session is { } session && session.Name != _sessionName)
        {
            LeaveSession();
            _sessionName = session.Name;
            _folder = Root / FolderKey(session.Name);
            Restore();
        }

        if (_mode.Dragging)
            _mode.UpdateDrag();

        if (_dirty && _timing.RealTime >= _nextSave)
            SaveSettings();

        UpdateShare();
    }

    /// <summary>
    /// Marks the shown settings changed: redraws the panel and saves the player's own within a second, or sends a
    /// shared template's new placement to the session.
    /// </summary>
    public void Modified()
    {
        if (SharedActive)
            _sharedModified = true;
        else
            _dirty = true;

        Changed?.Invoke();
    }

    /// <summary>A session's folder name: the name itself when it is a valid library name, else its UTF-8 hex.</summary>
    public static string FolderKey(string sessionName)
    {
        var trimmed = sessionName.Trim();
        return WFMappingModeLimits.IsValidName(trimmed)
            ? trimmed
            : Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(sessionName));
    }

    /// <summary>The map position at the centre of the view, rounded to whole tiles.</summary>
    public Vector2 ViewCentre()
    {
        var centre = _eye.CurrentEye.Position.Position;
        return new Vector2(MathF.Round(centre.X), MathF.Round(centre.Y));
    }

    /// <summary>Opens the file dialog and loads the picked PNG as the template, replacing the old one.</summary>
    public async void Load()
    {
        if (_picking || _folder is not { } folder)
            return;

        _picking = true;
        Changed?.Invoke();
        try
        {
            var filters = new FileDialogFilters(new FileDialogFilters.Group("png"));
            var (stream, _) = await _dialogs.GetFileAndName(filters, FileAccess.Read);
            if (stream == null)
                return;

            byte[]? data;
            await using (stream)
            {
                data = ReadCapped(stream, WFMappingPngHeader.MaxFileBytes);
            }

            // Left the session, or entered another, while the dialog was open.
            if (_folder != folder)
                return;

            if (data == null)
            {
                Popup("wf-mapping-template-file-too-large", ("max", WFMappingPngHeader.MaxFileBytes / (1024 * 1024)));
                return;
            }

            if (!LoadBytes(data))
                return;

            WriteImage(folder, data);
            SaveSettings();
            Changed?.Invoke();
        }
        catch (Exception e)
        {
            Log.Error($"Loading a mapping template failed: {e}");
            Popup("wf-mapping-template-read-failed");
        }
        finally
        {
            _picking = false;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Makes PNG bytes the player's own template, centred on the view at the current z-level, without saving it; false
    /// with a popup when they can't be used.
    /// </summary>
    public bool LoadBytes(byte[] data)
    {
        if (!CheckPng(data) || Decode(data, true) is not { } texture)
            return false;

        if (!SharedActive)
            _mode.CancelDrag();

        SetLocalTexture(texture, data);
        _zLevels.GetStack(_depths, out var depth);
        _localSettings.Reset(ViewCentre());
        _localSettings.Depth = depth;
        if (SharedActive)
            Popup("wf-mapping-template-own-hidden");

        Changed?.Invoke();
        return true;
    }

    /// <summary>Drops the player's own template and deletes its saved copy; a shared one stays.</summary>
    public void Clear()
    {
        if (!SharedActive)
            _mode.CancelDrag();

        SetLocalTexture(null, null);
        _localSettings.Reset(Vector2.Zero);
        _dirty = false;
        if (_folder is { } folder)
        {
            try
            {
                foreach (var file in new[] { folder / ImageFile, folder / SettingsFile })
                {
                    if (_resources.UserData.Exists(file))
                        _resources.UserData.Delete(file);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Deleting the mapping template in {folder} failed: {e.Message}");
            }
        }

        Changed?.Invoke();
    }

    /// <summary>Back to the defaults, centred on the view; a shared template keeps its z-level and layer.</summary>
    public void Reset()
    {
        _mode.CancelDrag();
        var settings = Settings;
        var layer = settings.Layer;
        var opacity = settings.Opacity;
        var visible = settings.Visible;
        var locked = settings.Locked;
        settings.Reset(ViewCentre());
        if (SharedActive)
        {
            settings.Layer = layer;
            settings.Opacity = opacity;
            settings.Visible = visible;
            settings.Locked = locked;
        }

        Modified();
    }

    public void Popup(string locKey, params (string, object)[] args)
    {
        _popup.PopupCursor(Loc.GetString(locKey, args));
    }

    private void OnSessionEnded()
    {
        DropShared(true);
        LeaveSession();
    }

    /// <summary>Saves pending settings and forgets the player's own template, keeping its files.</summary>
    private void LeaveSession()
    {
        if (_dirty)
            SaveSettings();

        if (!SharedActive)
            _mode.CancelDrag();

        SetLocalTexture(null, null);
        _localSettings.Reset(Vector2.Zero);
        _sessionName = null;
        _folder = null;
        Changed?.Invoke();
    }

    /// <summary>Loads the session's saved template, if it has one.</summary>
    private void Restore()
    {
        if (_folder is not { } folder)
            return;

        var image = folder / ImageFile;
        var settings = folder / SettingsFile;
        try
        {
            if (!_resources.UserData.Exists(image))
                return;

            var data = _resources.UserData.ReadAllBytes(image);
            if (WFMappingPngHeader.Check(data, out _, out _) != WFMappingPngCheck.Ok ||
                Decode(data, false) is not { } texture)
            {
                return;
            }

            SetLocalTexture(texture, data);
            if (_resources.UserData.Exists(settings) && _resources.UserData.TryReadAllText(settings, out var text))
                _localSettings.CopyFrom(WFMappingTemplateSettings.Read(text));
            else
                _localSettings.Reset(ViewCentre());
        }
        catch (Exception e)
        {
            Log.Warning($"Reading the mapping template in {folder} failed: {e.Message}");
        }
        finally
        {
            Changed?.Invoke();
        }
    }

    private bool CheckPng(byte[] data)
    {
        switch (WFMappingPngHeader.Check(data, out _, out _))
        {
            case WFMappingPngCheck.Ok:
                return true;
            case WFMappingPngCheck.FileTooLarge:
                Popup("wf-mapping-template-file-too-large", ("max", WFMappingPngHeader.MaxFileBytes / (1024 * 1024)));
                return false;
            case WFMappingPngCheck.ImageTooLarge:
                Popup("wf-mapping-template-image-too-large", ("side", WFMappingPngHeader.MaxSide));
                return false;
            default:
                Popup("wf-mapping-template-not-png");
                return false;
        }
    }

    /// <summary>Decodes PNG bytes; null when unreadable, with a popup when <paramref name="report"/> is set.</summary>
    private Texture? Decode(byte[] data, bool report)
    {
        try
        {
            return Texture.LoadFromPNGStream(new MemoryStream(data, false), "wf-mapping-template");
        }
        catch (Exception e)
        {
            Log.Debug($"Unreadable mapping template: {e.Message}");
            if (report)
                Popup("wf-mapping-template-not-png");

            return null;
        }
    }

    private void SetLocalTexture(Texture? texture, byte[]? data)
    {
        _localBytes = data;
        if (ReferenceEquals(_localTexture, texture))
            return;

        if (_localTexture is OwnedTexture owned)
            owned.Dispose();

        _localTexture = texture;
        UpdateOverlay();
    }

    /// <summary>Adds the overlay while an image is shown and removes it when none is.</summary>
    private void UpdateOverlay()
    {
        if (Texture != null && !_overlays.HasOverlay<WFMappingTemplateOverlay>())
            _overlays.AddOverlay(new WFMappingTemplateOverlay(this, _modes));
        else if (Texture == null)
            _overlays.RemoveOverlay<WFMappingTemplateOverlay>();
    }

    private void WriteImage(ResPath folder, byte[] data)
    {
        try
        {
            if (!_resources.UserData.Exists(folder))
                _resources.UserData.CreateDir(folder);

            _resources.UserData.WriteAllBytes(folder / ImageFile, data);
        }
        catch (Exception e)
        {
            Log.Warning($"Writing the mapping template in {folder} failed: {e.Message}");
        }
    }

    private void SaveSettings()
    {
        _dirty = false;
        _nextSave = _timing.RealTime + SaveInterval;
        if (_folder is not { } folder || !HasLocal)
            return;

        try
        {
            if (!_resources.UserData.Exists(folder))
                _resources.UserData.CreateDir(folder);

            _resources.UserData.WriteAllText(folder / SettingsFile, _localSettings.Write());
        }
        catch (Exception e)
        {
            Log.Warning($"Writing the mapping template settings in {folder} failed: {e.Message}");
        }
    }

    /// <summary>Reads a stream whole; null when it is longer than <paramref name="cap"/> bytes.</summary>
    private static byte[]? ReadCapped(Stream stream, int cap)
    {
        if (stream.CanSeek && stream.Length > cap)
            return null;

        using var memory = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (memory.Length + read > cap)
                return null;

            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }

    private PointerInputCmdHandler Nudge(int x, int y)
    {
        return new PointerInputCmdHandler((ICommonSession? _, EntityCoordinates _, EntityUid _) => _mode.Nudge(x, y),
            outsidePrediction: true);
    }

    private void OnScreenLoad()
    {
        if (_ui.ActiveScreen is not WFMappingScreen screen)
            return;

        OnScreenUnload();
        _panel = new WFMappingTemplatePanel(this);
        _slot = _ui.GetUIController<WFMappingScreenUIController>()
            .AddDocked(screen.TemplateHost, _panel, "wf-mapping-template-title");
    }

    private void OnScreenUnload()
    {
        _panel?.Unbind();
        _panel = null;
        if (_slot is { } slot)
        {
            slot.Dock();
            slot.Orphan();
        }

        _slot = null;
    }
}
