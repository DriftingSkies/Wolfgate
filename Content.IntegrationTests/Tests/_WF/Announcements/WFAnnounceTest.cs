using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.Announcements;
using Content.Server._WF.Announcements;
using Content.Shared._WF.Announcements;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Announcements;

/// <summary>Tests the roleplay announcement checks, chat text, chip delivery and window.</summary>
[TestFixture]
public sealed class WFAnnounceTest
{
    /// <summary>A packaged server has only this metadata for a sound, not the file.</summary>
    private const string PackagedOnlySound = "/Audio/_WF/Announcements/packaged-only.ogg";

    [TestPrototypes]
    private const string Prototypes = $@"
- type: audioMetadata
  id: {PackagedOnlySound}
  length: 1
";

    [Test]
    public async Task ChecksRejectBadRequests()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var announce = pair.Server.System<WFAnnounceSystem>();

        await pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(announce.IsValidSound(WFAnnounceFormat.DefaultSound));
                Assert.That(announce.IsValidSound("/Audio/Announcements/nope.ogg"), Is.False);
                Assert.That(announce.IsValidSound(PackagedOnlySound));
                Assert.That(announce.IsValidSound("/Textures/Logo/icon.png"), Is.False);
                Assert.That(announce.IsValidSound("/Audio/../Textures/Logo/icon.png"), Is.False);

                var player = pair.Player!;
                Assert.That(announce.TrySend(player, new WFAnnounceRequest { Body = "" }), Is.EqualTo("wf-announce-error-empty-body"));
                Assert.That(announce.TrySend(player, new WFAnnounceRequest
                {
                    PostToChat = false,
                    SoundMode = WFAnnounceSoundMode.None,
                }), Is.EqualTo("wf-announce-error-nothing"));
                Assert.That(announce.TrySend(player, new WFAnnounceRequest
                {
                    Body = "hi",
                    SoundMode = WFAnnounceSoundMode.Custom,
                    SoundPath = "/Audio/Announcements/nope.ogg",
                }), Is.EqualTo("wf-announce-error-bad-sound"));
                Assert.That(announce.TrySend(player, new WFAnnounceRequest { Body = "hi", ShowChip = true }),
                    Is.EqualTo("wf-announce-error-empty-chip"));
                Assert.That(announce.TrySend(player, new WFAnnounceRequest { Body = new string('a', WFAnnounceLimits.MaxBody + 1) }),
                    Is.EqualTo("wf-announce-error-too-long"));
                Assert.That(announce.TrySend(player, new WFAnnounceRequest
                {
                    Body = "hi",
                    SoundMode = WFAnnounceSoundMode.Custom,
                    SoundPath = "/Audio/Announcements/attention.ogg",
                }), Is.Null);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ChatTextIsValidMarkup()
    {
        await using var pair = await PoolManager.GetServerClient();

        await pair.Server.WaitAssertion(() =>
        {
            var titled = WFAnnounceFormat.ChatMarkup(new WFAnnounceRequest
            {
                Sender = "Sector [Control]",
                Title = "TRAFFIC [NOTICE",
                Body = "line one\nline [two",
                Accent = Color.Red,
            });
            var markup = WFAnnounceFormat.ChatMarkup(new WFAnnounceRequest { Body = "[bold]loud[/bold]", AllowMarkup = true });
            var escaped = WFAnnounceFormat.ChatMarkup(new WFAnnounceRequest { Body = "[bold]loud[/bold]" });

            Assert.Multiple(() =>
            {
                Assert.That(FormattedMessage.TryFromMarkup(titled, out var message), titled);
                Assert.That(message!.ToString(), Does.Contain("TRAFFIC [NOTICE").And.Contain("Sector [Control]").And.Contain("line [two"));
                Assert.That(FormattedMessage.TryFromMarkup(markup, out var bold), markup);
                Assert.That(bold!.ToString(), Does.Contain("loud").And.Not.Contain("[bold]"));
                Assert.That(FormattedMessage.TryFromMarkup(escaped, out var plain), escaped);
                Assert.That(plain!.ToString(), Does.Contain("[bold]loud[/bold]"));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ChipReachesClientAndWindowOpens()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var announce = pair.Server.System<WFAnnounceSystem>();
        var chips = pair.Client.System<WFAnnouncementChipSystem>();

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(announce.TrySend(pair.Player!, new WFAnnounceRequest
            {
                PostToChat = false,
                SoundMode = WFAnnounceSoundMode.None,
                ShowChip = true,
                ChipLevel = WFAnnounceChipLevel.Warning,
                ChipTitle = "TRAFFIC",
                ChipText = "Climb, climb",
                ChipDuration = 30f,
                ChipFlash = true,
            }), Is.Null);
        });

        await pair.RunTicksSync(10);
        Assert.That(chips.ChipCount, Is.EqualTo(1));

        var sounds = new List<string>();
        await pair.Client.WaitAssertion(() =>
        {
            var window = new WFAnnounceWindow();
            window.OpenCentered();
            sounds.AddRange(window.Sounds);
            window.Close();

            var res = pair.Client.ResolveDependency<Robust.Shared.ContentPack.IResourceManager>();
            var missing = sounds.Where(sound => !res.ContentFileExists(new ResPath(sound))).ToList();
            Assert.That(missing, Is.Empty, "client: " + string.Join(", ", missing.Take(10)));
        });

        // Every sound the picker lists must pass the server's check.
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(sounds, Is.Not.Empty);
            var bad = sounds.Where(sound => !announce.IsValidSound(sound)).ToList();
            Assert.That(bad, Is.Empty, string.Join(", ", bad.Take(10)));
        });

        await pair.CleanReturnAsync();
    }
}
