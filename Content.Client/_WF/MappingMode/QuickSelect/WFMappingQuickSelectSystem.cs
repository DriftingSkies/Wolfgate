using Content.Client._WF.MappingMode.Modes;
using Content.Client._WF.MappingMode.Palette;
using Content.Client._WF.MappingMode.Screen;
using Content.Client.Popups;
using Content.Client.UserInterface.Systems.Gameplay;
using Robust.Client.UserInterface;
using Robust.Shared.ContentPack;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.MappingMode.QuickSelect;

/// <summary>
/// Keeps the quick-select store in <c>/Mapping/quickselect.yml</c> of the client data folder, takes additions from the
/// palette and, when asked, the eyedropper, and puts <see cref="WFMappingQuickBar"/> on the mapping screen.
/// </summary>
public sealed partial class WFMappingQuickSelectSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private WFMappingBuildModeSystem _modes = default!;

    private static readonly ResPath StorePath = new("/Mapping/quickselect.yml");
    private static readonly ResPath BadPath = new("/Mapping/quickselect.bad.yml");
    private static readonly ResPath ActionsPath = new("/mapping_actions.yml");
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(1);

    private WFMappingQuickSelectStore? _store;
    private bool _dirty;
    private TimeSpan _nextSave;
    private WFMappingQuickBar? _bar;

    /// <summary>The store, read from disk on first use.</summary>
    public WFMappingQuickSelectStore Store => _store ??= Load();

    /// <summary>Raised after any change to the store.</summary>
    public event Action? Changed;

    public override void Initialize()
    {
        base.Initialize();

        _modes.QuickSelectRequested += OnRequested;
        _modes.PrototypePicked += OnPicked;

        var gameplay = _ui.GetUIController<GameplayStateLoadController>();
        gameplay.OnScreenLoad += OnScreenLoad;
        gameplay.OnScreenUnload += OnScreenUnload;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _modes.QuickSelectRequested -= OnRequested;
        _modes.PrototypePicked -= OnPicked;

        var gameplay = _ui.GetUIController<GameplayStateLoadController>();
        gameplay.OnScreenLoad -= OnScreenLoad;
        gameplay.OnScreenUnload -= OnScreenUnload;
        OnScreenUnload();

        if (_dirty)
            Save();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_dirty && _timing.RealTime >= _nextSave)
            Save();
    }

    /// <summary>Marks the store changed: redraws the bar and saves within a second.</summary>
    public void Modified()
    {
        _dirty = true;
        Changed?.Invoke();
    }

    /// <summary>Starts placing a slot's entry.</summary>
    public void Place(WFMappingPaletteEntry entry)
    {
        _modes.BeginPlacing(entry);
    }

    public void Popup(string locKey, params (string, object)[] args)
    {
        _popup.PopupCursor(Loc.GetString(locKey, args));
    }

    /// <summary>A store of the default set, from the placement actions of <c>/mapping_actions.yml</c>.</summary>
    public WFMappingQuickSelectStore Defaults()
    {
        var entries = new List<WFMappingPaletteEntry>();
        try
        {
            using var reader = _resources.ContentFileReadText(ActionsPath);
            entries = WFMappingQuickSelectStore.ParseActions(reader.ReadToEnd());
        }
        catch (Exception e)
        {
            Log.Warning($"Reading {ActionsPath} for the quick-select defaults failed: {e.Message}");
        }

        return WFMappingQuickSelectStore.Create(Loc.GetString("wf-mapping-quickselect-default-set"), entries);
    }

    private void OnRequested(WFMappingPaletteEntry entry)
    {
        var slot = Store.Add(entry, out var added);
        if (slot < 0)
        {
            Popup("wf-mapping-quickselect-page-full");
            return;
        }

        Popup(added ? "wf-mapping-quickselect-added" : "wf-mapping-quickselect-already", ("slot", slot + 1));
        if (added)
            Modified();
    }

    private void OnPicked(WFMappingPaletteEntry entry)
    {
        if (!Store.AddPicks)
            return;

        Store.Add(entry, out var added);
        if (added)
            Modified();
    }

    /// <summary>Reads the store; a missing file gives the defaults, a bad one is kept aside and gives them too.</summary>
    private WFMappingQuickSelectStore Load()
    {
        var data = _resources.UserData;
        try
        {
            if (!data.Exists(StorePath.Directory) || !data.Exists(StorePath) ||
                !data.TryReadAllText(StorePath, out var text))
            {
                return Defaults();
            }

            if (WFMappingQuickSelectStore.Read(text) is { } store)
                return store;

            if (data.Exists(BadPath))
                data.Delete(BadPath);

            data.Rename(StorePath, BadPath);
            Log.Info($"{StorePath} could not be read; kept it as {BadPath} and started from the defaults.");
        }
        catch (Exception e)
        {
            Log.Warning($"Reading {StorePath} failed: {e.Message}");
        }

        return Defaults();
    }

    private void Save()
    {
        _dirty = false;
        _nextSave = _timing.RealTime + SaveInterval;
        if (_store == null)
            return;

        try
        {
            var dir = StorePath.Directory;
            if (!_resources.UserData.Exists(dir))
                _resources.UserData.CreateDir(dir);

            _resources.UserData.WriteAllText(StorePath, _store.Write());
        }
        catch (Exception e)
        {
            Log.Warning($"Writing {StorePath} failed: {e.Message}");
        }
    }

    private void OnScreenLoad()
    {
        if (_ui.ActiveScreen is not WFMappingScreen screen)
            return;

        OnScreenUnload();
        _bar = new WFMappingQuickBar(this, _modes);
        screen.QuickBarHost.AddChild(_bar);
    }

    private void OnScreenUnload()
    {
        _bar?.Unbind();
        _bar?.Orphan();
        _bar = null;
    }
}
