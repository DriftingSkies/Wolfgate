using System.Text;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._WF.Cockpit;

/// <summary>Remembers how each cockpit pan surface answered the last right-press, for the wf_cockpitpan command.</summary>
public sealed class WFCockpitPanDiagnosticSystem : EntitySystem
{
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    private readonly List<string> _verdicts = new();
    private KeyEventArgs? _press;
    private TimeSpan _pressedAt;
    private ScreenCoordinates _mouse;
    private string _hit = string.Empty;
    private string _pressModifiers = string.Empty;

    /// <summary>True while the engine's own key state has Shift, Ctrl or Alt held.</summary>
    public static bool ModifierHeld(IInputManager input) =>
        input.IsKeyDown(Keyboard.Key.Shift) || input.IsKeyDown(Keyboard.Key.Control) || input.IsKeyDown(Keyboard.Key.Alt);

    /// <summary>Records a pan surface's answer to a right-press.</summary>
    public void Record(KeyEventArgs press, ScreenCoordinates mouse, string source, string verdict, params (string, object)[] args)
    {
        if (!ReferenceEquals(press, _press))
        {
            _press = press;
            _pressedAt = _timing.RealTime;
            _mouse = mouse;
            _hit = Describe(mouse.IsValid ? _ui.MouseGetControl(mouse) : null);
            _pressModifiers = Modifiers(press.Shift, press.Control, press.Alt);
            _verdicts.Clear();
        }

        var line = new StringBuilder(Loc.GetString("wf-cockpit-pan-verdict", ("source", source), ("verdict", Loc.GetString(verdict, args))));
        if (verdict == "wf-cockpit-pan-started")
        {
            // Both gates were dropped from the press; these notes show whether either would have refused it.
            if (!_clyde.IsFocused)
                line.Append(Loc.GetString("wf-cockpit-pan-note-unfocused"));
            if (_pressModifiers.Length > 0)
                line.Append(Loc.GetString("wf-cockpit-pan-note-window-modifiers", ("keys", _pressModifiers)));
        }
        _verdicts.Add(line.ToString());
    }

    /// <summary>The report printed by the wf_cockpitpan command.</summary>
    public IEnumerable<string> Report()
    {
        yield return Loc.GetString("wf-cockpit-pan-report-state",
            ("focused", _clyde.IsFocused.ToString()),
            ("modifiers", Or(Modifiers(_input.IsKeyDown(Keyboard.Key.Shift), _input.IsKeyDown(Keyboard.Key.Control), _input.IsKeyDown(Keyboard.Key.Alt)))));

        if (_press == null)
        {
            yield return Loc.GetString("wf-cockpit-pan-report-none");
            yield break;
        }

        yield return Loc.GetString("wf-cockpit-pan-report-press",
            ("seconds", $"{(_timing.RealTime - _pressedAt).TotalSeconds:0.0}"),
            ("x", $"{_mouse.Position.X:0}"),
            ("y", $"{_mouse.Position.Y:0}"),
            ("modifiers", Or(_pressModifiers)));
        yield return Loc.GetString("wf-cockpit-pan-report-hit", ("control", _hit));

        if (_verdicts.Count == 0)
            yield return Loc.GetString("wf-cockpit-pan-report-no-surface");
        foreach (var verdict in _verdicts)
            yield return verdict;
    }

    /// <summary>Names a control and its nearest ancestors, innermost first.</summary>
    public static string Describe(Control? control)
    {
        if (control == null)
            return "-";

        var text = new StringBuilder();
        for (var depth = 0; control != null && depth < 4; depth++, control = control.Parent)
        {
            if (depth > 0)
                text.Append(" < ");
            text.Append(control.GetType().Name);
            if (control.Name != null)
                text.Append(" '").Append(control.Name).Append('\'');
        }
        return text.ToString();
    }

    private static string Modifiers(bool shift, bool control, bool alt)
    {
        var keys = new List<string>();
        if (shift)
            keys.Add("Shift");
        if (control)
            keys.Add("Ctrl");
        if (alt)
            keys.Add("Alt");
        return string.Join("+", keys);
    }

    private string Or(string modifiers) => modifiers.Length > 0 ? modifiers : Loc.GetString("wf-cockpit-pan-none");
}
