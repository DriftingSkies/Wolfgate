using Content.Shared._WG.Companies;
using NUnit.Framework;

namespace Content.Tests._WG.Companies;

/// <summary>Covers payroll shares, cuts, rank order and name rules.</summary>
[TestFixture]
public sealed class WGCompanyPolicyTest
{
    [TestCase(1000, 600, 100)]
    [TestCase(600, 600, 100)]
    [TestCase(300, 600, 50)]
    [TestCase(0, 600, 0)]
    [TestCase(100, 0, 100)]
    public void PayrollSharesShortfallsEvenly(long balance, long due, int expected)
    {
        Assert.That(WGCompanyPolicy.PayrollShare(balance, due), Is.EqualTo(expected));
    }

    [TestCase(1000, 10, 100)]
    [TestCase(999, 10, 99)]
    [TestCase(1000, 0, 0)]
    [TestCase(1000, 90, 500)]
    [TestCase(-5, 10, 0)]
    public void CutsRoundDownAndCap(int amount, int cut, int expected)
    {
        Assert.That(WGCompanyPolicy.CutOf(amount, cut), Is.EqualTo(expected));
    }

    [Test]
    public void OnlyHigherRanksActOnLowerOnes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WGCompanyPolicy.Outranks(null, 0), Is.True, "The owner outranks every rank.");
            Assert.That(WGCompanyPolicy.Outranks(0, null), Is.False, "Nobody outranks the owner.");
            Assert.That(WGCompanyPolicy.Outranks(0, 1), Is.True);
            Assert.That(WGCompanyPolicy.Outranks(1, 1), Is.False);
            Assert.That(WGCompanyPolicy.Outranks(2, 1), Is.False);
        });
    }

    [Test]
    public void NamesAndTagsAreNormalized()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WGCompanyPolicy.TryNormalizeName("  Hawkline   Salvage ", out var name), Is.True);
            Assert.That(name, Is.EqualTo("Hawkline Salvage"));
            Assert.That(WGCompanyPolicy.TryNormalizeName("<b>bad</b>", out _), Is.False);
            Assert.That(WGCompanyPolicy.TryNormalizeTag(" hawk ", out var tag), Is.True);
            Assert.That(tag, Is.EqualTo("HAWK"));
            Assert.That(WGCompanyPolicy.TryNormalizeTag("H", out _), Is.False);
            Assert.That(WGCompanyPolicy.TryNormalizeTag("HAWKEYE", out _), Is.False);
            Assert.That(WGCompanyPolicy.WithinWithdrawCap(false, 300, 200, 100), Is.True);
            Assert.That(WGCompanyPolicy.WithinWithdrawCap(false, 300, 200, 101), Is.False);
            Assert.That(WGCompanyPolicy.WithinWithdrawCap(true, 0, 0, 1000000), Is.True);
        });
    }

    [Test]
    public void ApplicationsArePlainLines()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WGCompanyPolicy.TryNormalizeApplication(" I fly.\r\nI fix. ", out var message), Is.True);
            Assert.That(message, Is.EqualTo("I fly.\nI fix."));
            Assert.That(WGCompanyPolicy.TryNormalizeApplication(null, out message), Is.True);
            Assert.That(message, Is.Empty);
            Assert.That(WGCompanyPolicy.TryNormalizeApplication(new string('a', WGCompanyPolicy.MaximumApplicationLength + 1), out _), Is.False);
            Assert.That(WGCompanyPolicy.TryNormalizeApplication("bell\u0007", out _), Is.False);
        });
    }
}
