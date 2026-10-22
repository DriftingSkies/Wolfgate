using Content.Client.Decals.UI;
using Content.Client.Gameplay;
using Content.Client.Sandbox;
using Content.Client._WF.MappingMode; // WOLFGATE(MappingMode)
using Content.Shared.Decals;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.DecalPlacer;

public sealed partial class DecalPlacerUIController : UIController, IOnStateExited<GameplayState>, IOnSystemChanged<SandboxSystem>
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [UISystemDependency] private readonly SandboxSystem _sandbox = default!;
    [UISystemDependency] private readonly WFMappingClientSystem _wfMapping = default!; // WOLFGATE(MappingMode)

    private DecalPlacerWindow? _window;

    public void ToggleWindow()
    {
        EnsureWindow();

        if (_window!.IsOpen)
        {
            _window.Close();
        }
        // WOLFGATE(MappingMode) START: mappers open the decal placer
        // else if(_sandbox.SandboxAllowed)
        else if (_sandbox.SandboxAllowed || _wfMapping.InSession)
        // WOLFGATE END
        {
            _window.Open();
        }
    }

    public void OnStateExited(GameplayState state)
    {
        if (_window == null)
            return;
        _window.Dispose();
        _window = null;
    }

    public void OnSystemLoaded(SandboxSystem system)
    {
        _sandbox.SandboxDisabled += CloseWindow;
        _prototypes.PrototypesReloaded += OnPrototypesReloaded;
    }

    public void OnSystemUnloaded(SandboxSystem system)
    {
        _sandbox.SandboxDisabled -= CloseWindow;
        _prototypes.PrototypesReloaded -= OnPrototypesReloaded;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs obj)
    {
        if (obj.WasModified<DecalPrototype>())
            ReloadPrototypes();
    }

    private void ReloadPrototypes()
    {
        if (_window == null || _window.Disposed)
            return;

        var prototypes = _prototypes.EnumeratePrototypes<DecalPrototype>();
        _window.Populate(prototypes);
    }

    private void EnsureWindow()
    {
        if (_window is { Disposed: false })
            return;
        _window = UIManager.CreateWindow<DecalPlacerWindow>();
        LayoutContainer.SetAnchorPreset(_window, LayoutContainer.LayoutPreset.CenterLeft);
        ReloadPrototypes();
    }

    private void CloseWindow()
    {
        if (_window == null || _window.Disposed)
            return;

        _window.Close();
    }
}
