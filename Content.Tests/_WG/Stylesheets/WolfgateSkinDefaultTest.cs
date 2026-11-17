using Content.Client._WG.Stylesheets;
using Content.Shared._WG.CCVar;
using NUnit.Framework;

namespace Content.Tests._WG.Stylesheets;

/// <summary>The wg.ui_style default names the default skin, which is listed first; unknown ids fall back to it.</summary>
[TestFixture]
public sealed class WolfgateSkinDefaultTest
{
    [Test]
    public void StyleCVarDefaultIsTheDefaultSkin()
    {
        Assert.That(WolfgateSkins.Default, Is.SameAs(WolfgateSkins.Retro));
        Assert.That(WolfgateCVars.UiStyle.DefaultValue, Is.EqualTo(WolfgateSkins.Default.Id));
        Assert.That(WolfgateSkins.Get("no such skin"), Is.SameAs(WolfgateSkins.Default));
        Assert.That(WolfgateSkins.All[0], Is.SameAs(WolfgateSkins.Default));
    }
}
