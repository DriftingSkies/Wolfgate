using System.Reflection;
using Content.Client._WF.UserInterface.Controls;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.UserInterface;

/// <summary>
/// Typing into the colour popup keeps what was typed: the field being edited is not rewritten under the caret, and an
/// RGB or hex colour comes out exactly, not a step off from a round trip through HSV.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfgateColorPopup))]
public sealed class WolfgateColorPopupTest
{
    [Test]
    public async Task TypedValuesStickTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var popup = new WolfgateColorPopup { Color = Color.FromHex("#4080C0") };
            var picked = Color.White;
            popup.OnColorChanged += color => picked = color;

            LineEdit Field(string name) => (LineEdit) typeof(WolfgateColorPopup)
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(popup)!;

            // Digit by digit, as a player types them.
            void Type(LineEdit field, string text)
            {
                field.SetText("");
                foreach (var digit in text)
                    field.InsertAtCursor(digit.ToString());
            }

            foreach (var (name, value) in new[] { ("_r", 20), ("_g", 21), ("_b", 9), ("_r", 1), ("_g", 255), ("_b", 128) })
            {
                Type(Field(name), value.ToString());
                Assert.That(Field(name).Text, Is.EqualTo(value.ToString()), $"{name} was rewritten while typing.");
            }
            Assert.That((picked.RByte, picked.GByte, picked.BByte), Is.EqualTo(((byte) 1, (byte) 255, (byte) 128)));

            foreach (var (name, value) in new[] { ("_sField", 20), ("_vField", 21), ("_hField", 200) })
            {
                Type(Field(name), value.ToString());
                Assert.That(Field(name).Text, Is.EqualTo(value.ToString()), $"{name} was rewritten while typing.");
            }

            // Out of range while typing, then shown as the clamped value once entered.
            Type(Field("_hField"), "500");
            Assert.That(Field("_hField").Text, Is.EqualTo("500"));
            Field("_hField").ForceSubmitText();
            Assert.That(Field("_hField").Text, Is.EqualTo("360"));

            foreach (var hex in new[] { "#3A7BD5", "#010203", "#FEFDFC", "#808080" })
            {
                Field("_hex").SetText(hex, true);
                Assert.That(picked.ToHexNoAlpha(), Is.EqualTo(hex), "A pasted hex colour came out different.");
                Assert.That(Field("_r").Text, Is.EqualTo(picked.RByte.ToString()));
            }

            popup.Dispose();
        });

        await pair.CleanReturnAsync();
    }
}
