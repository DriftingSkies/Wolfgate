#nullable enable
using System.Linq;
using Content.Client._WF.MappingMode;
using Content.Client._WF.MappingMode.Template;
using Content.Server._WF.MappingMode;
using Content.Shared._WF.MappingMode;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// A template shared from the real client reaches the session and every present member, its placement is session
/// state that any member moves and the server clamps, bad or oversized PNGs are refused, it is saved with the map and
/// restored on open, and unsharing removes it for everyone.
/// </summary>
[TestFixture]
public sealed class WFMappingTemplateShareTest
{
    private const string SessionName = "Share Test";
    private const string SavedName = "Share Save";

    /// <summary>A whole 1×1 PNG: signature, IHDR, one IDAT and IEND.</summary>
    private static readonly byte[] Png =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0xDA, 0x63, 0x64, 0x60, 0xF8, 0x5F, 0x0F,
        0x00, 0x02, 0x87, 0x01, 0x80, 0xEB, 0x47, 0xBA, 0x92,
        0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    };

    private static readonly WFMappingTemplateState Wild =
        new(500000f, 3f, 1000f, 2f, 270f, 32, WFMappingTemplateLayer.Over, 5);

    [Test]
    public async Task ShareMoveSaveAndUnshare()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var share = entMan.System<WFMappingTemplateShareSystem>();
        var res = server.ResolveDependency<IResourceManager>();
        var clientSys = client.EntMan.System<WFMappingClientSystem>();
        var template = client.EntMan.System<WFMappingTemplateSystem>();
        await Enable(pair);
        var player = pair.Player!;
        var b = (await AddMappers(pair, 1))[0];

        await server.WaitPost(() => server.CfgMan.SetCVar(WFMappingTemplateCVars.TemplateCooldown, 0f));
        await client.WaitPost(() => clientSys.Send(new WFMappingCreateRequest(SessionName)));
        for (var i = 0; i < 200 && !(clientSys.SetUp && template.CanLoad); i++)
        {
            await pair.RunTicksSync(1);
        }

        WFMappingSession session = default!;
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryGetPresent(player, out var created), Is.True, "The client is in its session.");
            session = created!;
            Assert.That(mapping.TryJoinSession(b, session), Is.True);
        });
        await pair.RunTicksSync(5);

        // The client loads a PNG of its own and shares it.
        await client.WaitAssertion(() =>
        {
            Assert.That(template.CanLoad, Is.True, "The client's template follows the session.");
            Assert.That(template.LoadBytes(Png), Is.True);
            Assert.That(template.CanShare, Is.True);
            template.Share();
        });

        for (var i = 0; i < 200 && !(session.Template != null && template.SharedActive); i++)
        {
            await pair.RunTicksSync(1);
        }

        var id = 0;
        await server.WaitAssertion(() =>
        {
            Assert.That(session.Template, Is.Not.Null, "The server holds the shared template.");
            var shared = session.Template!;
            id = shared.Id;
            Assert.Multiple(() =>
            {
                Assert.That(shared.Png, Is.EqualTo(Png));
                Assert.That(shared.Width, Is.EqualTo(1));
                Assert.That(shared.Height, Is.EqualTo(1));
                Assert.That(shared.SharedBy, Is.EqualTo(player.UserId));
                Assert.That(share.LastOutcome(player.UserId), Is.EqualTo("wf-mapping-template-share-done"));
                Assert.That(share.ActiveDelivery(b.UserId), Is.EqualTo(shared.Id), "It is on its way to the member.");
                Assert.That(session.Dirty, Is.True, "Sharing is a change to save.");
            });
        });
        await client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(clientSys.Session?.Template?.Id, Is.EqualTo(id), "The session state carries it.");
                Assert.That(clientSys.Session?.Template?.SharedByName, Is.EqualTo(player.Name));
                Assert.That(template.SharedActive, Is.True, "The sharer shows the shared template.");
            });
        });

        // The other member moves it far past every limit; the server clamps and the client follows.
        await server.WaitAssertion(() =>
        {
            RaiseAs(entMan, b, new WFMappingTemplateTransformRequest(id, Wild));
            var state = session.Template!.State;
            Assert.Multiple(() =>
            {
                Assert.That(state.X, Is.EqualTo(WFMappingTemplateSettings.MaxOffset));
                Assert.That(state.Y, Is.EqualTo(3f));
                Assert.That(state.ScaleX, Is.EqualTo(WFMappingTemplateSettings.MaxScale));
                Assert.That(state.ScaleY, Is.EqualTo(2f));
                Assert.That(state.Rotation, Is.EqualTo(-90f).Within(1e-4f));
                Assert.That(state.Layer, Is.EqualTo(WFMappingTemplateLayer.Over));
                Assert.That(state.Depth, Is.EqualTo(0), "The session has one level.");
            });

            // A stale id moves nothing.
            RaiseAs(entMan, b, new WFMappingTemplateTransformRequest(id + 100, Wild with { X = 1f }));
            Assert.That(session.Template!.State.X, Is.EqualTo(WFMappingTemplateSettings.MaxOffset));
        });
        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(template.Settings.Position.X, Is.EqualTo(WFMappingTemplateSettings.MaxOffset));
                Assert.That(template.Settings.ScaleX, Is.EqualTo(WFMappingTemplateSettings.MaxScale));
                Assert.That(template.Settings.Layer, Is.EqualTo(WFMappingTemplateLayer.Over));
            });
        });

        // A file that isn't a PNG, an image over the caps and a file over the share cap are all refused.
        await server.WaitAssertion(() =>
        {
            var notPng = Png.ToArray();
            notPng[1] = (byte) 'X';
            Assert.That(Upload(entMan, share, b, notPng), Is.EqualTo("wf-mapping-template-not-png"));

            var huge = Png.ToArray();
            huge[18] = 0x23;
            huge[19] = 0x28;
            Assert.That(Upload(entMan, share, b, huge), Is.EqualTo("wf-mapping-template-image-too-large"),
                "9000 pixels wide.");

            var cap = share.MaxBytes;
            RaiseAs(entMan, b, new WFMappingTemplateUploadBeginRequest(cap + 1,
                WFMappingUploadAssembler.ChunksFor(cap + 1), Wild));
            Assert.Multiple(() =>
            {
                Assert.That(share.ActiveUpload(b.UserId), Is.Null);
                Assert.That(share.LastOutcome(b.UserId), Is.EqualTo("wf-mapping-template-share-too-large"));
                Assert.That(session.Template?.Id, Is.EqualTo(id), "The shared template is untouched.");
            });
        });

        // A save writes it beside the levels.
        await server.WaitAssertion(() =>
        {
            RaiseAs(entMan, b, new WFMappingSaveRequest(SavedName));
            var folder = WFMappingLibraryNames.MapFolder(b.UserId, SavedName);
            var versions = res.UserData.DirectoryEntries(folder).ToList();
            Assert.That(versions, Has.Count.EqualTo(1));
            var version = WFMappingLibraryNames.VersionFolder(b.UserId, SavedName, versions[0]);
            Assert.Multiple(() =>
            {
                Assert.That(res.UserData.Exists(version / WFMappingTemplateShareSystem.ImageFile), Is.True);
                Assert.That(res.UserData.Exists(version / WFMappingTemplateShareSystem.SettingsFile), Is.True);
            });
        });

        // The client unshares it: gone for everyone, and the client's own template shows again.
        await client.WaitPost(() => template.Unshare());
        for (var i = 0; i < 100 && (session.Template != null || template.SharedActive); i++)
        {
            await pair.RunTicksSync(1);
        }

        await server.WaitAssertion(() => Assert.That(session.Template, Is.Null, "Unsharing clears it on the server."));
        await client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(clientSys.Session?.Template, Is.Null);
                Assert.That(template.SharedActive, Is.False);
                Assert.That(template.HasTemplate, Is.True, "The client's own template is still there.");
                Assert.That(template.HasLocal, Is.True);
            });
        });

        // Opening the saved version restores the shared template with its placement.
        await server.WaitAssertion(() => mapping.LeaveSession(b.UserId, kicked: false));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            RaiseAs(entMan, b, new WFMappingOpenRequest(SavedName, null));
            Assert.That(mapping.TryGetPresent(b, out var opened), Is.True, "The saved version opens.");
            Assert.That(opened!.Template, Is.Not.Null, "The shared template comes back with the map.");
            var restored = opened.Template!;
            Assert.Multiple(() =>
            {
                Assert.That(restored.Png, Is.EqualTo(Png));
                Assert.That(restored.SharedBy, Is.EqualTo(b.UserId));
                Assert.That(restored.State.X, Is.EqualTo(WFMappingTemplateSettings.MaxOffset));
                Assert.That(restored.State.ScaleX, Is.EqualTo(WFMappingTemplateSettings.MaxScale));
                Assert.That(restored.State.Rotation, Is.EqualTo(-90f).Within(1e-4f));
                Assert.That(restored.State.Layer, Is.EqualTo(WFMappingTemplateLayer.Over));
                Assert.That(opened.Dirty, Is.False);
            });
        });
        await pair.RunTicksSync(15);
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryGetPresent(b, out var opened), Is.True);
            var restored = opened!.Template!;
            Assert.That(share.ActiveDelivery(b.UserId), Is.EqualTo(restored.Id), "The opener is sent the template.");

            RaiseAs(entMan, b, new WFMappingTemplateUnshareRequest(restored.Id));
            Assert.That(opened.Template, Is.Null, "Any member may unshare it.");
            Assert.That(share.ActiveDelivery(b.UserId), Is.Null, "Nothing is sent once it is gone.");
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            mapping.LeaveSession(b.UserId, kicked: false);
            mapping.LeaveSession(player.UserId, kicked: false);
            server.CfgMan.SetCVar(WFMappingTemplateCVars.TemplateCooldown,
                WFMappingTemplateCVars.TemplateCooldown.DefaultValue);
        });
        await pair.RunTicksSync(5);
        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    /// <summary>Sends a whole PNG as a share from <paramref name="member"/> and returns the outcome.</summary>
    private static string? Upload(IEntityManager entMan, WFMappingTemplateShareSystem share,
        Robust.Shared.Player.ICommonSession member, byte[] png)
    {
        RaiseAs(entMan, member, new WFMappingTemplateUploadBeginRequest(png.Length,
            WFMappingUploadAssembler.ChunksFor(png.Length), Wild));
        var transfer = share.ActiveUpload(member.UserId);
        Assert.That(transfer, Is.Not.Null, "The share should have started.");
        RaiseAs(entMan, member, new WFMappingTemplateUploadChunkEvent(transfer!.Value, 0, png));
        Assert.That(share.ActiveUpload(member.UserId), Is.Null, "A one-chunk share ends with its chunk.");
        return share.LastOutcome(member.UserId);
    }
}
