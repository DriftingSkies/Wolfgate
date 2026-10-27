using Content.Shared._WF.Roles;
using NUnit.Framework;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.Tests._WF.Roles;

/// <summary>Custom job title cleaning, look-alike folding, blocklists, job-name matches, allowed jobs and the length cap.</summary>
[TestFixture]
[TestOf(typeof(CustomJobTitleRules))]
public sealed class CustomJobTitleRulesTest : ContentUnitTest
{
    private const string RoleId = "WFTestTitleRole";

    private const string Prototypes = $@"
- type: playTimeTracker
  id: WFTestTitleTracker

- type: job
  id: WFTestTitleJob
  playTimeTracker: WFTestTitleTracker
  name: Pilot

- type: job
  id: WFTestTitleChef
  playTimeTracker: WFTestTitleTracker
  name: Chef

- type: job
  id: WFTestTitleStationChef
  playTimeTracker: WFTestTitleTracker
  name: Chef

- type: customJobTitle
  id: {RoleId}
  maxLength: 20
  allowedJobs:
  - WFTestTitleChef
  blockedTitles:
  - Staff
  - N/A
  blockedWords:
  - admin
  - head of
";

    private IPrototypeManager _proto = default!;
    private CustomJobTitlePrototype _rules = default!;

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        IoCManager.Resolve<ISerializationManager>().Initialize();
        _proto = IoCManager.Resolve<IPrototypeManager>();
        _proto.Initialize();
        _proto.LoadString(Prototypes);
        _proto.ResolveResults();
        _rules = _proto.Index<CustomJobTitlePrototype>(RoleId);
    }

    [Test]
    public void CleanTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CustomJobTitleRules.Clean("  Salvage   Diver  "), Is.EqualTo("Salvage Diver"));
            Assert.That(CustomJobTitleRules.Clean("   "), Is.Empty);
        });
    }

    [Test]
    public void NormalizeTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CustomJobTitleRules.Normalize("Adm1n"), Is.EqualTo(CustomJobTitleRules.Normalize("Admin")));
            Assert.That(CustomJobTitleRules.Normalize("P1lot"), Is.EqualTo(CustomJobTitleRules.Normalize("Pilot")));
            Assert.That(CustomJobTitleRules.Normalize("A.D M-I N"), Is.EqualTo("admin"));
        });
    }

    [TestCase("", true)]
    [TestCase("Salvage Diver", true)]
    [TestCase("Badminton Coach", true)]
    [TestCase("Staff Writer", true)]
    [TestCase("Adm1n", false)]
    [TestCase("A D M I N", false)]
    [TestCase("A.D.M.I.N", false)]
    [TestCase("Station Admin Aide", false)]
    [TestCase("Head of Mining", false)]
    [TestCase("Staff", false)]
    [TestCase("S t a f f", false)]
    [TestCase("N/A", false)]
    [TestCase("Pilot", false)]
    [TestCase("P1LOT", false)]
    [TestCase("Chef", true)]
    [TestCase("CH3F", true)]
    [TestCase("Twenty One Characters", false)]
    [TestCase("Café Owner", false)]
    [TestCase("Diver!", false)]
    [TestCase("1234", false)]
    public void IsValidTest(string title, bool valid)
    {
        Assert.That(CustomJobTitleRules.IsValid(title, _rules, _proto, out _), Is.EqualTo(valid));
    }

    [Test]
    public void SanitizeTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CustomJobTitleRules.Sanitize("  Salvage   Diver ", RoleId, _proto), Is.EqualTo("Salvage Diver"));
            Assert.That(CustomJobTitleRules.Sanitize("Adm1n", RoleId, _proto), Is.Null);
            Assert.That(CustomJobTitleRules.Sanitize("   ", RoleId, _proto), Is.Null);
            Assert.That(CustomJobTitleRules.Sanitize(null, RoleId, _proto), Is.Null);
            Assert.That(CustomJobTitleRules.Sanitize("Salvage Diver", "WFTestNoSuchRole", _proto), Is.Null);
        });
    }
}
