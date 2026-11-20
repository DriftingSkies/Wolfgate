#nullable enable
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
    /// For each checkbox on the named tab: it starts at its cvar's value, and flipping it then pressing Apply writes the
    /// flipped value. Runs on the client thread; every cvar is put back afterwards.
    /// </summary>
    public static void AssertCheckBoxesApply<TTab>(
        IConfigurationManager config,
        string tabName,
        params (string CheckBox, CVarDef<bool> CVar)[] boxes)
        where TTab : Control
    {
        var originals = boxes.Select(b => config.GetCVar(b.CVar)).ToArray();
        var menu = new OptionsMenu();
        try
        {
            var tab = menu.FindControl<TTab>(tabName);
            var row = tab.FindControl<OptionsTabControlRow>("Control");

            for (var i = 0; i < boxes.Length; i++)
            {
                var box = tab.FindControl<CheckBox>(boxes[i].CheckBox);
                Assert.That(box.Pressed, Is.EqualTo(originals[i]), $"{boxes[i].CheckBox} should start at its cvar.");
                box.Pressed = !originals[i];
            }

            row.ValueChanged();
            // Buttons only raise OnPressed from input, so call the row's own handler.
            typeof(OptionsTabControlRow).GetMethod("ApplyButtonPressed", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(row, new object?[] { null });

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
}
