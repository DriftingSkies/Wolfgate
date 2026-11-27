using System.Numerics;
using Content.Client.Camera;
using Content.Shared._WG.ShipModules;
using Content.Shared.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Random;

namespace Content.Client._WG.ShipModules;

/// <summary>A fired module's jolt: the crew's view kicks, and the helm screens showing the ship shake.</summary>
public sealed partial class WGShipModuleHelmSystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private CameraRecoilSystem _recoil = default!;

    /// <summary>Camera kick per unit of jolt; past the recoil cap it turns into a short shake.</summary>
    private const float CameraKick = 1.2f;

    /// <summary>Screen shake per unit of jolt, in UI pixels.</summary>
    private const float ShakePixels = 6f;

    private const float ShakeTime = 0.35f;
    private const float ShakeFade = 0.15f;
    private const float ShakeFrequency = 30f;

    private sealed class Shake
    {
        public float Strength;
        public float Left;
        public float Timer;
        public Vector2 Applied;
    }

    private readonly Dictionary<Control, Shake> _shakes = new();
    private readonly List<Control> _settled = new();
    private readonly HashSet<Control> _jolted = new();
    private float _intensity;

    private void InitializeJolt()
    {
        SubscribeNetworkEvent<WGShipModuleFiredEvent>(OnFired);
        Subs.CVar(_cfg, CCVars.ScreenShakeIntensity, value => _intensity = value, true);
    }

    private void OnFired(WGShipModuleFiredEvent ev)
    {
        if (!TryGetEntity(ev.Ship, out var ship))
            return;

        if (ev.Jolt > 0f && _player.LocalEntity is { } local && Transform(local).GridUid == ship)
            _recoil.KickCamera(local, _random.NextAngle().ToVec() * CameraKick * ev.Jolt);

        _jolted.Clear();
        foreach (var bank in _banks)
        {
            if (!bank.Flash(ship.Value, ev.Slot) || ev.Jolt <= 0f || bank.ShakeRoot() is not { } root || !_jolted.Add(root))
                continue;

            if (!_shakes.TryGetValue(root, out var shake))
                _shakes[root] = shake = new Shake();

            shake.Strength = Math.Max(shake.Strength, ev.Jolt);
            shake.Left = ShakeTime;
        }
    }

    /// <summary>Moves each shaking screen to a new offset at the shake's rate and puts it back when the shake ends.</summary>
    private void UpdateShakes(float frameTime)
    {
        foreach (var (control, shake) in _shakes)
        {
            shake.Left -= frameTime;
            if (shake.Left <= 0f || _intensity <= 0f)
            {
                Offset(control, -shake.Applied);
                _settled.Add(control);
                continue;
            }

            shake.Timer -= frameTime;
            if (shake.Timer > 0f)
                continue;

            shake.Timer += 1f / ShakeFrequency;
            var amplitude = ShakePixels * shake.Strength * _intensity * Math.Min(1f, shake.Left / ShakeFade);
            var target = _random.NextAngle().ToVec() * amplitude;
            Offset(control, target - shake.Applied);
            shake.Applied = target;
        }

        foreach (var control in _settled)
        {
            _shakes.Remove(control);
        }

        _settled.Clear();
    }

    /// <summary>Shifts a control placed by its parent's layout margins without resizing it.</summary>
    private static void Offset(Control control, Vector2 by)
    {
        if (by == Vector2.Zero || control.Disposed)
            return;

        LayoutContainer.SetMarginLeft(control, control.GetValue<float>(LayoutContainer.MarginLeftProperty) + by.X);
        LayoutContainer.SetMarginRight(control, control.GetValue<float>(LayoutContainer.MarginRightProperty) + by.X);
        LayoutContainer.SetMarginTop(control, control.GetValue<float>(LayoutContainer.MarginTopProperty) + by.Y);
        LayoutContainer.SetMarginBottom(control, control.GetValue<float>(LayoutContainer.MarginBottomProperty) + by.Y);
    }
}
