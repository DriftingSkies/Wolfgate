using System.Linq;
using Content.Shared.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client.Options.UI.Tabs;

/// <summary>Splits the Controls tab: stock SS14 binds on the SS14 side, every other bind on the Wolfgate side.</summary>
public sealed partial class KeyRebindTab
{
    /// <summary>The binds stock SS14 lists on its Controls tab. Anything not here belongs to a fork.</summary>
    private static readonly HashSet<BoundKeyFunction> WgStockFunctions = new HashSet<BoundKeyFunction>
    {
        EngineKeyFunctions.MoveUp, EngineKeyFunctions.MoveLeft, EngineKeyFunctions.MoveDown, EngineKeyFunctions.MoveRight,
        EngineKeyFunctions.Walk,
        EngineKeyFunctions.CameraRotateLeft, EngineKeyFunctions.CameraRotateRight, EngineKeyFunctions.CameraReset,
        ContentKeyFunctions.ZoomIn, ContentKeyFunctions.ZoomOut, ContentKeyFunctions.ResetZoom,
        EngineKeyFunctions.Use, EngineKeyFunctions.UseSecondary,
        ContentKeyFunctions.UseItemInHand, ContentKeyFunctions.AltUseItemInHand,
        ContentKeyFunctions.ActivateItemInWorld, ContentKeyFunctions.AltActivateItemInWorld,
        ContentKeyFunctions.Drop, ContentKeyFunctions.ExamineEntity, ContentKeyFunctions.SwapHands,
        ContentKeyFunctions.MoveStoredItem, ContentKeyFunctions.RotateStoredItem, ContentKeyFunctions.SaveItemLocation,
        ContentKeyFunctions.SmartEquipBackpack, ContentKeyFunctions.SmartEquipBelt, ContentKeyFunctions.SmartEquipPocket1,
        ContentKeyFunctions.SmartEquipPocket2, ContentKeyFunctions.SmartEquipSuitStorage,
        ContentKeyFunctions.OpenBackpack, ContentKeyFunctions.OpenBelt,
        ContentKeyFunctions.ThrowItemInHand, ContentKeyFunctions.TryPullObject, ContentKeyFunctions.MovePulledObject,
        ContentKeyFunctions.ReleasePulledObject, ContentKeyFunctions.Point,
        ContentKeyFunctions.RotateObjectClockwise, ContentKeyFunctions.RotateObjectCounterclockwise, ContentKeyFunctions.FlipObject,
        ContentKeyFunctions.FocusChat, ContentKeyFunctions.FocusLocalChat, ContentKeyFunctions.FocusEmote,
        ContentKeyFunctions.FocusWhisperChat, ContentKeyFunctions.FocusRadio, ContentKeyFunctions.FocusLOOC,
        ContentKeyFunctions.FocusOOC, ContentKeyFunctions.FocusAdminChat, ContentKeyFunctions.FocusDeadChat,
        ContentKeyFunctions.FocusConsoleChat, ContentKeyFunctions.CycleChatChannelForward, ContentKeyFunctions.CycleChatChannelBackward,
        ContentKeyFunctions.OpenCharacterMenu, ContentKeyFunctions.OpenCraftingMenu, ContentKeyFunctions.OpenGuidebook,
        ContentKeyFunctions.OpenInventoryMenu, ContentKeyFunctions.OpenAHelp, ContentKeyFunctions.OpenActionsMenu,
        ContentKeyFunctions.OpenEmotesMenu, ContentKeyFunctions.ToggleRoundEndSummaryWindow,
        ContentKeyFunctions.OpenEntitySpawnWindow, ContentKeyFunctions.OpenSandboxWindow, ContentKeyFunctions.OpenTileSpawnWindow,
        ContentKeyFunctions.OpenDecalSpawnWindow, ContentKeyFunctions.OpenAdminMenu,
        EngineKeyFunctions.WindowCloseAll, EngineKeyFunctions.WindowCloseRecent, EngineKeyFunctions.EscapeMenu,
        ContentKeyFunctions.EscapeContext,
        ContentKeyFunctions.TakeScreenshot, ContentKeyFunctions.TakeScreenshotNoUI, ContentKeyFunctions.ToggleFullscreen,
        ContentKeyFunctions.ShuttleStrafeUp, ContentKeyFunctions.ShuttleStrafeRight, ContentKeyFunctions.ShuttleStrafeLeft,
        ContentKeyFunctions.ShuttleStrafeDown, ContentKeyFunctions.ShuttleRotateLeft, ContentKeyFunctions.ShuttleRotateRight,
        ContentKeyFunctions.ShuttleBrake,
        EngineKeyFunctions.EditorPlaceObject, EngineKeyFunctions.EditorCancelPlace, EngineKeyFunctions.EditorGridPlace,
        EngineKeyFunctions.EditorLinePlace, EngineKeyFunctions.EditorRotateObject,
        ContentKeyFunctions.EditorFlipObject, ContentKeyFunctions.EditorCopyObject,
        EngineKeyFunctions.ShowDebugConsole, EngineKeyFunctions.ShowDebugMonitors, EngineKeyFunctions.HideUI,
        ContentKeyFunctions.InspectEntity,
    };

    /// <summary>The checkboxes stock SS14 lists on its Controls tab, by their Fluent ID.</summary>
    private static readonly HashSet<string> WgStockCheckBoxes = new HashSet<string>
    {
        "ui-options-hotkey-keymap",
        "ui-options-hotkey-toggle-walk",
    };

    private readonly bool _wgForkOnly;

    /// <summary>Whether this tab lists only the binds stock SS14 lacks.</summary>
    public bool WgForkOnly => _wgForkOnly;

    /// <summary>Whether a bind belongs on this tab.</summary>
    private bool WgShows(BoundKeyFunction function)
    {
        var stock = WgStockFunctions.Contains(function) || ContentKeyFunctions.GetHotbarBoundKeys().Contains(function);
        return stock != _wgForkOnly;
    }

    /// <summary>Whether a checkbox, by its Fluent ID, belongs on this tab.</summary>
    private bool WgShows(string checkBoxName)
    {
        return WgStockCheckBoxes.Contains(checkBoxName) != _wgForkOnly;
    }

    /// <summary>
    /// The SS13 preset is a Wolfgate feature, so it sits on the Wolfgate side; Reset all stays on the SS14 side, as it
    /// resets every bind.
    /// </summary>
    private void WgSetUpButtons()
    {
        Ss13DefaultsButton.Visible = _wgForkOnly;
        ResetAllButton.Visible = !_wgForkOnly;
    }

    /// <summary>Removes the headers, and the spacers before them, of sections left with no rows.</summary>
    private void WgDropEmptySections()
    {
        var children = KeybindsContainer.Children.ToList();
        var drop = new List<Control>();

        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is not Label)
                continue;

            var next = i + 1;
            if (next < children.Count && children[next] is not Label && !WgIsSpacer(children[next]))
                continue;

            drop.Add(children[i]);
            if (i > 0 && WgIsSpacer(children[i - 1]))
                drop.Add(children[i - 1]);
        }

        foreach (var control in drop)
        {
            KeybindsContainer.RemoveChild(control);
        }

        if (KeybindsContainer.ChildCount > 0 && WgIsSpacer(KeybindsContainer.GetChild(0)))
            KeybindsContainer.RemoveChild(KeybindsContainer.GetChild(0));
    }

    private static bool WgIsSpacer(Control control)
    {
        return control.GetType() == typeof(Control) && control.ChildCount == 0;
    }
}
