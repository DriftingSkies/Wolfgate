using Content.Server._WG.Genitals;
using Content.Shared._WG.Genitals.Profile;
using Moq;
using NUnit.Framework;
using Robust.Shared.Log;

namespace Content.Tests._WG.Genitals;

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

    [Test]
    public void HeadshotSurvivesEditsAndStorage()
    {
        const string url = "https://example.com/adult.png";
        var log = new Mock<ISawmill>().Object;
        var profile = GenitalProfile.Empty.WithNsfwHeadshotUrl(url);

        var json = GenitalProfileJson.Serialize(profile);
        var back = GenitalProfileJson.Deserialize(json, log, 0);

        Assert.Multiple(() =>
        {
            Assert.That(profile.IsEmpty, Is.True, "a headshot alone adds no organ");
            Assert.That(profile.WithNsfwText("Freckles.").NsfwHeadshotUrl, Is.EqualTo(url), "other edits keep it");
            Assert.That(profile.Clone().MemberwiseEquals(profile), Is.True);
            Assert.That(profile.MemberwiseEquals(GenitalProfile.Empty), Is.False);
            Assert.That(json, Does.Contain("\"headshot\""), "a profile with only a headshot is still written");
            Assert.That(back?.NsfwHeadshotUrl, Is.EqualTo(url));
            Assert.That(back?.LoadFailed, Is.False);
            Assert.That(GenitalProfileJson.Serialize(GenitalProfile.Empty), Does.Not.Contain("headshot"));
            Assert.That(GenitalProfileJson.Deserialize("{\"v\":1,\"headshot\":5}", log, 0)?.LoadFailed, Is.True,
                "a headshot that isn't text marks the column unreadable");
        });
    }
}
