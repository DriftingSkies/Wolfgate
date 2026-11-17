#pragma warning disable RA0039 // These checks run without a prototype manager.
using System.Collections.Generic;
using Content.Server._WG.Genitals;
using Content.Shared._WG.Genitals;
using Content.Shared._WG.Genitals.Profile;
using Content.Shared._WG.Genitals.Prototypes;
using Content.Shared._WG.Genitals.Systems;
using Content.Shared.Preferences;
using Moq;
using NUnit.Framework;
using Robust.Shared.Log;
using Robust.Shared.Maths;

namespace Content.Tests._WG.Genitals;

/// <summary>Profile transformations, serialization and display calculations need no running game.</summary>
[TestFixture]
public sealed class GenitalLogicTest
{
    /// <summary>Threshold boundaries use the supplied settings, without loading prototypes.</summary>
    [Test]
    public void ThresholdsTest()
    {
        var settings = new GenitalSettingsPrototype { PartialArousal = 30, FullArousal = 70 };

        Assert.Multiple(() =>
        {
            Assert.That(SharedArousalSystem.ToState(0, settings), Is.EqualTo(ArousalState.None));
            Assert.That(SharedArousalSystem.ToState(29, settings), Is.EqualTo(ArousalState.None));
            Assert.That(SharedArousalSystem.ToState(30, settings), Is.EqualTo(ArousalState.Partial));
            Assert.That(SharedArousalSystem.ToState(69, settings), Is.EqualTo(ArousalState.Partial));
            Assert.That(SharedArousalSystem.ToState(70, settings), Is.EqualTo(ArousalState.Full));
            Assert.That(SharedArousalSystem.ToState(100, settings), Is.EqualTo(ArousalState.Full));
        });
    }

    [Test]
    public void StepMappingTest()
    {
        var settings = new GenitalSettingsPrototype { LengthStepMaxCm = new List<int> { 17, 24, 32, 44 } };
        var lengths = new (int Cm, int Step)[] { (5, 1), (17, 1), (18, 2), (24, 2), (25, 3), (32, 3), (33, 4), (44, 4), (45, 5), (60, 5) };

        Assert.Multiple(() =>
        {
            // Missing art steps: the highest available not above the request, else the lowest.
            Assert.That(GenitalSpriteResolver.ResolveStep(new[] { 1, 2, 3, 5 }, 4), Is.EqualTo(3));
            Assert.That(GenitalSpriteResolver.ResolveStep(new[] { 1, 2, 3, 5 }, 9), Is.EqualTo(5));
            Assert.That(GenitalSpriteResolver.ResolveStep(new[] { 4, 5, 6 }, 3), Is.EqualTo(4));
            Assert.That(GenitalSpriteResolver.ResolveStep(System.Array.Empty<int>(), 2), Is.EqualTo(2));

            // Length to sprite step and the migration inverse.
            foreach (var (cm, step) in lengths)
            {
                Assert.That(GenitalStateBuilder.LengthToStep(cm, settings), Is.EqualTo(step), $"{cm} cm");
            }

            for (var step = 1; step <= 5; step++)
            {
                Assert.That(GenitalStateBuilder.LengthToStep(GenitalStateBuilder.StepToLength(step), settings), Is.EqualTo(step));
            }
        });
    }

    [Test]
    public void ProfileEditsTest()
    {
        var full = FullProfile();
        var noPenis = full.WithPenis(null);
        var vaginaAdded = GenitalProfile.Empty.WithVagina(new VaginaProfile(VaginaHuman));
        var wombRemoved = vaginaAdded.WithWomb(false);
        var vaginaChanged = wombRemoved.WithVagina(new VaginaProfile(VaginaSlit));
        var failed = GenitalProfile.Failed(full);
        var clone = full.Clone();
        var first = GenitalProfile.Empty;
        var second = GenitalProfile.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(GenitalProfile.Empty.Version, Is.EqualTo(GenitalProfile.CurrentVersion));
            Assert.That(GenitalProfile.Unmigrated.Version, Is.EqualTo(0));
            Assert.That(first, Is.Not.SameAs(second), "Empty must return a fresh instance.");
            Assert.That(GenitalProfile.Empty.IsEmpty, Is.True);

            // Removing the penis removes its linked testicles.
            Assert.That(noPenis.Penis, Is.Null);
            Assert.That(noPenis.Testicles, Is.Null);

            // Adding a vagina adds the womb; changing it keeps the womb choice; removing it removes the womb.
            Assert.That(vaginaAdded.Womb, Is.True);
            Assert.That(vaginaChanged.Womb, Is.False);
            Assert.That(vaginaAdded.WithVagina(null).Womb, Is.False);

            // Every edit clears LoadFailed.
            Assert.That(failed.LoadFailed, Is.True);
            Assert.That(failed.WithWomb(true).LoadFailed, Is.False);

            // Clone is deep and equal.
            Assert.That(clone, Is.Not.SameAs(full));
            Assert.That(clone.Penis, Is.Not.SameAs(full.Penis));
            Assert.That(clone.MemberwiseEquals(full), Is.True);
            Assert.That(full.MemberwiseEquals(noPenis), Is.False);
        });
    }

    [Test]
    public void JsonTest()
    {
        var log = new Mock<ISawmill>().Object;

        var full = FullProfile().AsMigrated(new List<string> { "Genital-Penis-Knotted-3-0@#FFFFFFFF" });
        var fullJson = GenitalProfileJson.Serialize(full);
        var fullBack = GenitalProfileJson.Deserialize(fullJson, log, 1);

        var custom = FullProfile()
            .WithRevealMode(GenitalRevealMode.ClothingRemoval)
            .WithPenis(new PenisProfile(PenisHemi, 33, SheathType.Slit, false, Color.FromHex("#3366CC"), false,
                Color.FromHex("#112233"), GenitalVisibility.ShowThroughClothing))
            .WithTesticles(new TesticlesProfile(TesticleType.Internal, 2, false, Color.FromHex("#445566")))
            .WithWomb(false);
        var customBack = GenitalProfileJson.Deserialize(GenitalProfileJson.Serialize(custom), log, 2);
        var emptyBack = GenitalProfileJson.Deserialize(GenitalProfileJson.Serialize(GenitalProfile.Empty), log, 3);

        var notJson = GenitalProfileJson.Deserialize("{not json", log, 4);
        var notObject = GenitalProfileJson.Deserialize("[1, 2]", log, 5);
        var badOrgan = GenitalProfileJson.Deserialize(
            "{\"v\":1,\"penis\":{\"shape\":\"WGGenitalShapePenisKnotted\",\"lengthCm\":\"long\"},\"vagina\":{\"shape\":\"WGGenitalShapeVaginaHuman\"},\"womb\":true}",
            log, 6);
        var badEnum = GenitalProfileJson.Deserialize(
            "{\"v\":1,\"breasts\":{\"shape\":\"WGGenitalShapeBreastsPair\",\"visibility\":\"Sideways\"},\"testicles\":{\"type\":\"External\"}}",
            log, 7);
        var extraProperty = GenitalProfileJson.Deserialize("{\"v\":1,\"reveal\":\"ClothingRemoval\",\"future\":{\"x\":1},\"womb\":false}", log, 8);
        var noVersion = GenitalProfileJson.Deserialize("{\"reveal\":\"UndergarmentRemoval\"}", log, 9);
        var newer = GenitalProfileJson.Deserialize(
            "{\"v\":2,\"reveal\":\"ClothingRemoval\",\"breasts\":{\"shape\":\"WGGenitalShapeBreastsPair\",\"cup\":4}}", log, 11);

        // An edited unmigrated profile is written as the current schema, so the edit is not lost.
        var editedUnmigrated = GenitalProfile.Unmigrated.WithPenis(new PenisProfile(PenisKnotted));
        var editedJson = GenitalProfileJson.Serialize(editedUnmigrated);
        var editedBack = GenitalProfileJson.Deserialize(editedJson, log, 10);
        var revealOnlyJson = GenitalProfileJson.Serialize(GenitalProfile.Unmigrated.WithRevealMode(GenitalRevealMode.ClothingRemoval));

        Assert.Multiple(() =>
        {
            // Unmigrated profiles keep the column empty, so the bank write-back never marks them migrated.
            Assert.That(GenitalProfileJson.Serialize(GenitalProfile.Unmigrated), Is.Empty);
            Assert.That(GenitalProfileJson.Serialize(GenitalProfile.Failed(GenitalProfile.Unmigrated)), Is.Empty);
            Assert.That(GenitalProfileJson.Deserialize("", log, 0), Is.Null);
            Assert.That(GenitalProfileJson.Deserialize("   ", log, 0), Is.Null);
            Assert.That(GenitalProfileJson.Deserialize(null, log, 0), Is.Null);

            // Round trips, with enum names and the legacy strings.
            Assert.That(fullJson, Does.Contain("\"v\":1"));
            Assert.That(fullJson, Does.Contain("\"sheath\":\"Sheath\""));
            Assert.That(fullBack?.MemberwiseEquals(full), Is.True);
            Assert.That(fullBack?.LegacyMarkings, Is.EqualTo(full.LegacyMarkings));
            Assert.That(fullBack?.LoadFailed, Is.False);
            Assert.That(customBack?.MemberwiseEquals(custom), Is.True);
            Assert.That(emptyBack?.MemberwiseEquals(GenitalProfile.Empty), Is.True);

            // Not JSON, or not an object: nothing readable.
            Assert.That(notJson?.LoadFailed, Is.True);
            Assert.That(notJson?.IsEmpty, Is.True);
            Assert.That(notObject?.LoadFailed, Is.True);

            // One unreadable organ never discards the others.
            Assert.That(badOrgan?.LoadFailed, Is.True);
            Assert.That(badOrgan?.Penis, Is.Null);
            Assert.That(badOrgan?.Vagina?.Shape, Is.EqualTo(VaginaHuman));
            Assert.That(badOrgan?.Womb, Is.True);
            Assert.That(badEnum?.LoadFailed, Is.True);
            Assert.That(badEnum?.Breasts, Is.Null);
            Assert.That(badEnum?.Testicles?.Type, Is.EqualTo(TesticleType.External));

            // Unknown properties are ignored; a missing version is schema 1, whatever CurrentVersion is.
            Assert.That(extraProperty?.LoadFailed, Is.False);
            Assert.That(extraProperty?.RevealMode, Is.EqualTo(GenitalRevealMode.ClothingRemoval));
            Assert.That(noVersion?.Version, Is.EqualTo(1));
            Assert.That(noVersion?.LoadFailed, Is.False);

            // A newer schema is read, but flagged, so saves keep the fields this build does not know.
            Assert.That(newer?.LoadFailed, Is.True);
            Assert.That(newer?.RevealMode, Is.EqualTo(GenitalRevealMode.ClothingRemoval));
            Assert.That(newer?.Breasts?.Cup, Is.EqualTo(4));

            // Only an untouched unmigrated profile stays "".
            Assert.That(editedJson, Does.Contain($"\"v\":{GenitalProfile.CurrentVersion}"));
            Assert.That(editedBack?.Version, Is.EqualTo(GenitalProfile.CurrentVersion));
            Assert.That(editedBack?.Penis?.Shape, Is.EqualTo(PenisKnotted));
            Assert.That(revealOnlyJson, Does.Contain("\"reveal\":\"ClothingRemoval\""));
        });
    }

    [Test]
    public void ProfileEqualityTest()
    {
        var first = MakeProfile().WithGenitals(CustomProfile());
        var second = MakeProfile().WithGenitals(CustomProfile());
        var otherAnatomy = MakeProfile().WithGenitals(CustomProfile().WithWomb(true));
        var otherName = second.WithName("Another Name");

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.Equals(second), Is.True, "Independently built identical profiles must be equal.");
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()), "Equal profiles must hash alike.");
            Assert.That(first.Equals(otherAnatomy), Is.False, "Different anatomy must not be equal.");
            Assert.That(first.Equals(otherName), Is.False, "A different name must not be equal.");
        });
    }

    private const string PenisKnotted = "WGGenitalShapePenisKnotted";
    private const string PenisHemi = "WGGenitalShapePenisHemi";
    private const string VaginaHuman = "WGGenitalShapeVaginaHuman";
    private const string VaginaSlit = "WGGenitalShapeVaginaSlit";
    private const string BreastsPair = "WGGenitalShapeBreastsPair";

    private static GenitalProfile FullProfile()
    {
        return GenitalProfile.Empty
            .WithPenis(new PenisProfile(PenisKnotted, 15, SheathType.Sheath))
            .WithTesticles(new TesticlesProfile(TesticleType.External, 2))
            .WithVagina(new VaginaProfile(VaginaHuman))
            .WithBreasts(new BreastsProfile(BreastsPair, 3));
    }

    private static HumanoidCharacterProfile MakeProfile()
    {
        return HumanoidCharacterProfile.DefaultWithSpecies("Human").WithAge(30);
    }

    private static GenitalProfile CustomProfile()
    {
        return GenitalProfile.Empty
            .WithRevealMode(GenitalRevealMode.ClothingRemoval)
            .WithPenis(new PenisProfile(PenisKnotted, 27, SheathType.Sheath, false, Color.FromHex("#AA5566"), true, null,
                GenitalVisibility.ShowThroughClothing))
            .WithTesticles(new TesticlesProfile(TesticleType.External, 4, false, Color.FromHex("#886655")))
            .WithVagina(new VaginaProfile(VaginaHuman, true, null, GenitalVisibility.AlwaysHidden))
            .WithWomb(false)
            .WithBreasts(new BreastsProfile(BreastsPair, 7, true));
    }
}

