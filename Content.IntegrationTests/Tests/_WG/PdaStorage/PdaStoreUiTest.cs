#nullable enable
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._WG.PdaStorage;
using Content.Client._WG.PdaUi;
using Content.Shared._WG.PdaStorage;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.PdaStorage;

/// <summary>
/// The store fragment loads its XAML, renders catalogue rows, download progress and errors.
/// </summary>
[TestFixture]
[TestOf(typeof(PdaStoreUiFragment))]
public sealed class PdaStoreUiTest
{
    [Test]
    public async Task FragmentRendersStoreStateTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });

        await pair.Client.WaitAssertion(() =>
        {
            var fragment = new PdaStoreUiFragment();

            var state = new PdaStoreUiState
            {
                Capacity = 128,
                Used = 80,
                Apps =
                {
                    new PdaStoreAppEntry
                    {
                        Id = "Notekeeper",
                        Name = "notekeeper-program-name",
                        Description = "pda-store-app-notekeeper-desc",
                        Category = "pdaUtility",
                        CategoryName = "pda-store-category-utility",
                        Size = 8,
                        Compatible = true,
                        HasAccess = true,
                        Verified = true,
                    },
                    new PdaStoreAppEntry
                    {
                        Id = "LogProbe",
                        Name = "log-probe-program-name",
                        Description = "pda-store-app-logprobe-desc",
                        Category = "pdaSecurity",
                        CategoryName = "pda-store-category-security",
                        Size = 16,
                        Compatible = true,
                        HasAccess = false,
                        Verified = true,
                    },
                },
                Installed =
                {
                    new PdaStoreProgramEntry
                    {
                        Program = new NetEntity(1),
                        Name = "nano-chat-program-name",
                        AppId = "NanoChat",
                        Size = 8,
                        Readonly = true,
                    },
                },
            };

            fragment.UpdateState(state);

            var appList = Field<BoxContainer>(fragment, "AppList");
            var installedList = Field<BoxContainer>(fragment, "InstalledList");
            Assert.That(appList.ChildCount, Is.EqualTo(2), "one row per catalogue entry");
            Assert.That(installedList.ChildCount, Is.EqualTo(1), "one row per installed program");

            state.Error = PdaStoreError.NoAccess;
            state.Downloading = true;
            state.DownloadAppId = "Notekeeper";
            state.DownloadSize = 8;
            state.DownloadProgress = 4;
            fragment.UpdateState(state);

            Assert.That(Field<Label>(fragment, "ErrorLabel").Visible, Is.True);
            Assert.That(IsCardShowingProgress(fragment, "Notekeeper"), Is.True,
                "the downloading app shows its inline percentage box");
            Assert.That(IsCardShowingProgress(fragment, "LogProbe"), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    private static bool IsCardShowingProgress(PdaStoreUiFragment fragment, string appId)
    {
        var cards = (System.Collections.IEnumerable) typeof(PdaStoreUiFragment)
            .GetField("_appCards", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(fragment)!;

        foreach (var item in cards)
        {
            var (entry, card) = ((PdaStoreAppEntry, PdaStoreAppCard)) item;
            if (entry.Id == appId)
                return card.ShowingProgress;
        }

        return false;
    }

    private static T Field<T>(PdaStoreUiFragment fragment, string name) where T : class
    {
        return (T) typeof(PdaStoreUiFragment)
            .GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(fragment)!;
    }
}


