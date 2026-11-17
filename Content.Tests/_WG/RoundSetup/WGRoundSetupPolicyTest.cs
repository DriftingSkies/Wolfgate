using Content.Shared._WG.RoundSetup;
using NUnit.Framework;

namespace Content.Tests._WG.RoundSetup;

/// <summary>Exercises every access mode against invitations, codes, capacity and round state.</summary>
[TestFixture]
public sealed class WGRoundSetupPolicyTest
{
    /// <summary>All access modes obey capacity and late-join restrictions.</summary>
    [TestCase(WGLobbyAccess.Public, false, false, WGLobbyJoinDecision.Join)]
    [TestCase(WGLobbyAccess.Public, false, true, WGLobbyJoinDecision.Join)]
    [TestCase(WGLobbyAccess.Public, true, false, WGLobbyJoinDecision.Join)]
    [TestCase(WGLobbyAccess.Public, true, true, WGLobbyJoinDecision.Join)]
    [TestCase(WGLobbyAccess.Requests, false, false, WGLobbyJoinDecision.Request)]
    [TestCase(WGLobbyAccess.Requests, false, true, WGLobbyJoinDecision.Request)]
    [TestCase(WGLobbyAccess.Requests, true, false, WGLobbyJoinDecision.Join)]
    [TestCase(WGLobbyAccess.Requests, true, true, WGLobbyJoinDecision.Join)]
    [TestCase(WGLobbyAccess.Closed, false, false, WGLobbyJoinDecision.Denied)]
    [TestCase(WGLobbyAccess.Closed, false, true, WGLobbyJoinDecision.Denied)]
    [TestCase(WGLobbyAccess.Closed, true, false, WGLobbyJoinDecision.Join)]
    [TestCase(WGLobbyAccess.Closed, true, true, WGLobbyJoinDecision.Join)]
    [TestCase(WGLobbyAccess.Code, false, false, WGLobbyJoinDecision.Denied)]
    [TestCase(WGLobbyAccess.Code, false, true, WGLobbyJoinDecision.Join)]
    [TestCase(WGLobbyAccess.Code, true, false, WGLobbyJoinDecision.Join)]
    [TestCase(WGLobbyAccess.Code, true, true, WGLobbyJoinDecision.Join)]
    [TestCase(WGLobbyAccess.Invite, false, false, WGLobbyJoinDecision.Denied)]
    [TestCase(WGLobbyAccess.Invite, false, true, WGLobbyJoinDecision.Denied)]
    [TestCase(WGLobbyAccess.Invite, true, false, WGLobbyJoinDecision.Join)]
    [TestCase(WGLobbyAccess.Invite, true, true, WGLobbyJoinDecision.Join)]
    public void AccessMatrix(
        WGLobbyAccess access,
        bool invited,
        bool validCode,
        WGLobbyJoinDecision expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Evaluate(false, false, false), Is.EqualTo(expected), "Pregame does not require late joining.");
            Assert.That(Evaluate(false, true, false), Is.EqualTo(expected), "Pregame with late joining enabled.");
            Assert.That(Evaluate(true, true, false), Is.EqualTo(expected), "A started lobby permits late joining.");
            Assert.That(Evaluate(true, false, false), Is.EqualTo(WGLobbyJoinDecision.Denied), "Late joining disabled.");
            Assert.That(Evaluate(false, false, true), Is.EqualTo(WGLobbyJoinDecision.Denied), "Full pregame lobby.");
            Assert.That(Evaluate(false, true, true), Is.EqualTo(WGLobbyJoinDecision.Denied), "Full pregame lobby with late joining enabled.");
            Assert.That(Evaluate(true, false, true), Is.EqualTo(WGLobbyJoinDecision.Denied), "Full started lobby with late joining disabled.");
            Assert.That(Evaluate(true, true, true), Is.EqualTo(WGLobbyJoinDecision.Denied), "Full started lobby.");
        });

        WGLobbyJoinDecision Evaluate(bool started, bool allowLateJoin, bool full)
        {
            return WGRoundSetupPolicy.EvaluateJoin(access, started, allowLateJoin, full, invited, validCode);
        }
    }

    /// <summary>Company settings let members walk on; outsiders ask or are turned away.</summary>
    [TestCase(WGLobbyAccess.CompanyOnly, true, false, WGLobbyJoinDecision.Join)]
    [TestCase(WGLobbyAccess.CompanyOnly, false, false, WGLobbyJoinDecision.Denied)]
    [TestCase(WGLobbyAccess.CompanyOnly, false, true, WGLobbyJoinDecision.Join)]
    [TestCase(WGLobbyAccess.CompanyFirst, true, false, WGLobbyJoinDecision.Join)]
    [TestCase(WGLobbyAccess.CompanyFirst, false, false, WGLobbyJoinDecision.Request)]
    [TestCase(WGLobbyAccess.CompanyFirst, false, true, WGLobbyJoinDecision.Join)]
    public void CompanyAccess(WGLobbyAccess access, bool companyMember, bool invited, WGLobbyJoinDecision expected)
    {
        Assert.That(WGRoundSetupPolicy.EvaluateJoin(access, false, false, false, invited, false, companyMember), Is.EqualTo(expected));
    }

    /// <summary>Malformed access values fail closed even with both credentials.</summary>
    [Test]
    public void UnknownAccessNeverGrantsEntry([Range(7, 255)] int value)
    {
        Assert.That(
            WGRoundSetupPolicy.EvaluateJoin((WGLobbyAccess) value, false, true, false, true, true),
            Is.EqualTo(WGLobbyJoinDecision.Denied));
    }

    /// <summary>Code entry accepts lowercase letters and surrounding whitespace.</summary>
    [TestCase("ABC234", "ABC234")]
    [TestCase("ABCD234", "ABCD234")]
    [TestCase("ABCD2345", "ABCD2345")]
    [TestCase("abcd2345", "ABCD2345")]
    [TestCase("  aBcD2345  ", "ABCD2345")]
    [TestCase("\tABCD2345\r\n", "ABCD2345")]
    [TestCase("Zyxw9876", "ZYXW9876")]
    public void CodesAcceptAsciiCaseAndOuterWhitespace(string input, string expected)
    {
        Assert.That(WGRoundSetupPolicy.TryNormalizeCode(input, out var normalized), Is.True);
        Assert.That(normalized, Is.EqualTo(expected));
    }

    /// <summary>Malformed codes and Unicode lookalikes never become valid credentials.</summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("     ")]
    [TestCase("ABC23")]
    [TestCase("ABCDE2345")]
    [TestCase("ABCD2340")]
    [TestCase("ABCD2341")]
    [TestCase("ABCD234I")]
    [TestCase("ABCD234O")]
    [TestCase("ABCD234i")]
    [TestCase("ABCD234o")]
    [TestCase("AB CD234")]
    [TestCase("AB-CD234")]
    [TestCase("AB\nCD234")]
    [TestCase("AB\0CD234")]
    [TestCase("ABCD234\u017f")]
    [TestCase("ABCD234\u212a")]
    [TestCase("ABCD234\uFF21")]
    [TestCase("ABCD234\u0391")]
    [TestCase("ABCD234\u0410")]
    [TestCase("ABCD234\u200B")]
    public void InvalidCodesReturnNoNormalizedValue(string input)
    {
        Assert.That(WGRoundSetupPolicy.TryNormalizeCode(input, out var normalized), Is.False);
        Assert.That(normalized, Is.Empty);
    }

    [TestCase("Rescue\u202Ecrew")]
    [TestCase("Rescue\u2066crew")]
    [TestCase("Rescue\u200Bcrew")]
    [TestCase("Rescue\u200Dcrew")]
    [TestCase("Rescue\uFEFFcrew")]
    [TestCase("Rescue\U000E0001crew")]
    [TestCase("Rescue\tcrew")]
    public void DescriptionsRejectInvisibleControls(string input)
    {
        Assert.That(WGRoundSetupPolicy.TryNormalizeDescription(input, out var description), Is.False);
        Assert.That(description, Is.Empty);
    }

    [Test]
    public void DescriptionsPreservePlainUnicodeAndParagraphs()
    {
        Assert.That(WGRoundSetupPolicy.TryNormalizeDescription("  Café rescue\r\nMedical crew welcome.  ", out var description), Is.True);
        Assert.That(description, Is.EqualTo("Café rescue\nMedical crew welcome."));
    }

    /// <summary>Every configured generation character is accepted by validation.</summary>
    [Test]
    public void CodeAlphabetCanGenerateAcceptedCodes()
    {
        foreach (var character in WGRoundSetupPolicy.CodeAlphabet)
        {
            var code = new string(character, WGRoundSetupPolicy.MaximumCodeLength);
            Assert.That(WGRoundSetupPolicy.TryNormalizeCode(code, out var normalized), Is.True);
            Assert.That(normalized, Is.EqualTo(code));
        }
    }
}
