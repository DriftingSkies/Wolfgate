using System.Linq;
using Content.Client._WF.ChatTabs;
using Content.Shared.Chat;
using NUnit.Framework;

namespace Content.Tests._WF.ChatTabs;

/// <summary>Chat tabs survive being saved to the CVar and read back.</summary>
[TestFixture]
[TestOf(typeof(ChatTab))]
public sealed class ChatTabFormatTest
{
    [Test]
    public void TabsRoundTrip()
    {
        var tabs = new[]
        {
            new ChatTab("All", ChatTab.AllChannels),
            new ChatTab("Close by", ChatChannel.Local | ChatChannel.Whisper | ChatChannel.Emotes),
            new ChatTab("Quiet", ChatChannel.None),
        };

        var saved = ChatTab.Format(tabs);
        Assert.That(saved, Is.EqualTo("All=*|Close by=Local,Whisper,Emotes|Quiet="));

        var read = ChatTab.Parse(saved);
        Assert.That(read, Has.Count.EqualTo(3));
        for (var i = 0; i < tabs.Length; i++)
        {
            Assert.That(read[i].Name, Is.EqualTo(tabs[i].Name));
            Assert.That(read[i].Channels, Is.EqualTo(tabs[i].Channels));
        }
    }

    /// <summary>A hand-edited or outdated value loses only the parts that can't be read.</summary>
    [Test]
    public void BadEntriesAreSkipped()
    {
        var read = ChatTab.Parse("=Local|NoChannels|Radio = Radio , Bogus|  |Ghosts=Dead");

        Assert.That(read, Has.Count.EqualTo(2));
        Assert.That(read[0].Name, Is.EqualTo("Radio"));
        Assert.That(read[0].Channels, Is.EqualTo(ChatChannel.Radio));
        Assert.That(read[1].Channels, Is.EqualTo(ChatChannel.Dead));
    }

    [Test]
    public void TabCountIsCapped()
    {
        var saved = string.Join('|', new string[ChatTab.MaxTabs + 5].Select((_, i) => $"Tab{i}=OOC"));
        Assert.That(ChatTab.Parse(saved), Has.Count.EqualTo(ChatTab.MaxTabs));
    }

    /// <summary>A name can't hold the save format's separators or run past the length cap.</summary>
    [Test]
    public void NamesAreSanitized()
    {
        Assert.That(ChatTab.SanitizeName("  a|b=c\n "), Is.EqualTo("abc"));
        Assert.That(ChatTab.SanitizeName(new string('x', 40)), Has.Length.EqualTo(ChatTab.MaxNameLength));
        Assert.That(ChatTab.SanitizeName(" | "), Is.Empty);
    }
}
