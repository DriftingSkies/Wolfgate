using Content.Server._WF.Genitals;
using Content.Shared._WF.Genitals.Profile;
using Moq;
using NUnit.Framework;
using Robust.Shared.Log;

namespace Content.Tests._WF.Genitals;

/// <summary>The adult description on the anatomy profile: copies, equality and the stored JSON.</summary>
[TestFixture]
[TestOf(typeof(GenitalProfile))]
public sealed class GenitalNsfwTextTest
{
    [Test]
    public void TextSurvivesEditsAndStorage()
    {
        var log = new Mock<ISawmill>().Object;
        var profile = GenitalProfile.Empty.WithNsfwText("Freckles everywhere.");

        var json = GenitalProfileJson.Serialize(profile);
        var back = GenitalProfileJson.Deserialize(json, log, 0);

        Assert.Multiple(() =>
        {
            Assert.That(profile.IsEmpty, Is.True, "text alone adds no organ");
            Assert.That(profile.WithWomb(false).NsfwText, Is.EqualTo("Freckles everywhere."), "other edits keep it");
            Assert.That(profile.Clone().MemberwiseEquals(profile), Is.True);
            Assert.That(profile.MemberwiseEquals(GenitalProfile.Empty), Is.False);
            Assert.That(json, Does.Contain("\"nsfw\""), "a profile with only text is still written");
            Assert.That(back?.NsfwText, Is.EqualTo("Freckles everywhere."));
            Assert.That(GenitalProfileJson.Serialize(GenitalProfile.Empty), Does.Not.Contain("nsfw"));
        });
    }
}
