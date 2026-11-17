#nullable enable
using Content.Client._WG.Administration.UI.SpawnOutfit;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.Inventory;
using Robust.Client.Console;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WG.Administration;

/// <summary>
/// <c>setoutfit</c> with only an entity opens the Spawn as Outfit picker in set mode, and confirming an outfit dresses
/// that entity.
/// </summary>
[TestOf(typeof(SpawnOutfitMenu))]
public sealed class SetOutfitPickerTest : InteractionTest
{
    private const string Gear = "WGSetOutfitTestGear";
    private const string Jumpsuit = "ClothingUniformJumpsuitColorGrey";

    [TestPrototypes]
    private const string Prototypes = @"
- type: startingGear
  id: WGSetOutfitTestGear
  equipment:
    jumpsuit: ClothingUniformJumpsuitColorGrey
";

    [Test]
    public async Task SetOutfitUsesPicker()
    {
        var target = await SpawnTarget("MobHuman");

        var admins = Server.ResolveDependency<IAdminManager>();
        await Server.WaitPost(() => admins.PromoteHost(ServerSession));
        for (var i = 0; i < 60 && !admins.HasAdminFlag(ServerSession, AdminFlags.Admin); i++)
        {
            await RunTicks(1);
        }

        Assert.That(admins.HasAdminFlag(ServerSession, AdminFlags.Admin), "The player never became an admin.");

        await Client.WaitPost(() => Client.ResolveDependency<IClientConsoleHost>().ExecuteCommand($"setoutfit {target}"));
        await RunTicks(5);

        var window = GetWindow<SpawnOutfitMenu>();
        var title = Client.ResolveDependency<ILocalizationManager>().GetString("wg-spawn-outfit-set-title");
        Assert.Multiple(() =>
        {
            Assert.That(window.Target, Is.EqualTo(target), "The picker should target the setoutfit entity.");
            Assert.That(window.Title, Is.EqualTo(title), "The picker should open in set mode.");
        });

        var row = GetControlFromChildren<ContainerButton>(
            button => TryGetControlFromChildren<Label>(label => label.Text == Gear, button, out _),
            window);
        await ClickControl(row);
        await ClickControl<SpawnOutfitMenu>("ConfirmButton");
        await RunTicks(5);

        Assert.That(TryFindWindow<SpawnOutfitMenu>(out _), Is.False, "Confirming should close the picker.");
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<InventorySystem>().TryGetSlotEntity(STarget!.Value, "jumpsuit", out var jumpsuit),
                "The target should wear the outfit's jumpsuit.");
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(jumpsuit!.Value).EntityPrototype?.ID, Is.EqualTo(Jumpsuit));
        });
    }
}
