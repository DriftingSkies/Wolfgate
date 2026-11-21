#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.IntegrationTests.Tests._WG.EngineCompat;
using Content.Client.Options.UI;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;

namespace Content.IntegrationTests.Tests._WG.Options;

/// <summary>Checks that options-window checkboxes are bound to their cvars and written by Apply.</summary>
public static class OptionsTabTestHelper
{
    /// <summary>
    /// For each checkbox on the window's one tab of type <typeparamref name="TTab"/>: it starts at its cvar's value,
    /// and flipping it then pressing Apply writes the flipped value. Runs on the client thread; every cvar is put back
    /// afterwards.
    /// </summary>
    public static void AssertCheckBoxesApply<TTab>(
        IConfigurationManager config,
        params (string CheckBox, CVarDef<bool> CVar)[] boxes)
        where TTab : Control
    {
        var originals = boxes.Select(b => config.GetCVar(b.CVar)).ToArray();
        var menu = new OptionsMenu();
        try
        {
            var tab = Find<TTab>(menu);
            var row = tab.FindControl<OptionsTabControlRow>("Control");

            for (var i = 0; i < boxes.Length; i++)
            {
                var box = tab.FindControl<CheckBox>(boxes[i].CheckBox);
                Assert.That(box.Pressed, Is.EqualTo(originals[i]), $"{boxes[i].CheckBox} should start at its cvar.");
                box.Pressed = !originals[i];
            }

            row.ValueChanged();
            Apply(row);

            for (var i = 0; i < boxes.Length; i++)
            {
                Assert.That(config.GetCVar(boxes[i].CVar), Is.EqualTo(!originals[i]), $"Apply should write {boxes[i].CVar.Name}.");
            }
        }
        finally
        {
            for (var i = 0; i < boxes.Length; i++)
            {
                config.SetCVar(boxes[i].CVar, originals[i]);
            }

            menu.Teardown();
        }
    }

    /// <summary>The one control of type <typeparamref name="T"/> under <paramref name="root"/>, at any depth.</summary>
    public static T Find<T>(Control root) where T : Control
    {
        return Descendants(root).OfType<T>().Single();
    }

    /// <summary>Presses a row's Apply button. Buttons only raise OnPressed from input, so this calls its handler.</summary>
    public static void Apply(OptionsTabControlRow row)
    {
        typeof(OptionsTabControlRow).GetMethod("ApplyButtonPressed", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(row, new object?[] { null });
    }

    public static IEnumerable<Control> Descendants(Control control)
    {
        yield return control;
        foreach (var child in control.Children)
        {
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
