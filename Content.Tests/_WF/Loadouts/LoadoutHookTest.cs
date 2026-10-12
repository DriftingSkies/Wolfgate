using System.Reflection;
using Content.Client._WF.Loadouts;
using Content.Client.Lobby.UI;
using NUnit.Framework;

namespace Content.Tests._WF.Loadouts;

/// <summary>The character editor opens the Wolfgate loadout window.</summary>
[TestFixture]
[TestOf(typeof(WolfgateLoadoutWindow))]
public sealed class LoadoutHookTest
{
    /// <summary>The editor reaches the window through a using alias; this fails if a merge drops that line.</summary>
    [Test]
    public void EditorOpensTheWolfgateWindow()
    {
        var field = typeof(HumanoidProfileEditor).GetField("_loadoutWindow", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(field, Is.Not.Null);
        Assert.That(field!.FieldType, Is.EqualTo(typeof(WolfgateLoadoutWindow)));
    }
}
