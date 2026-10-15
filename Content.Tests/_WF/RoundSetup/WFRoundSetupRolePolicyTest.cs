using Content.Shared._WF.RoundSetup;
using NUnit.Framework;

namespace Content.Tests._WF.RoundSetup;

/// <summary>Checks capacity reservations and plain lobby role labels.</summary>
[TestFixture]
public sealed class WFRoundSetupRolePolicyTest
{
    /// <summary>A full role preserves its current member without admitting another player.</summary>
    [TestCase(1, 0, false, true)]
    [TestCase(1, 1, false, false)]
    [TestCase(1, 1, true, true)]
    [TestCase(1, 2, true, false)]
    [TestCase(16, 15, false, true)]
    [TestCase(16, 16, false, false)]
    [TestCase(16, 16, true, true)]
    [TestCase(0, 0, false, false)]
    [TestCase(-1, 0, false, false)]
    [TestCase(17, 0, false, false)]
    [TestCase(1, -1, false, false)]
    [TestCase(16, 17, true, false)]
    [TestCase(1, 0, true, false)]
    public void CapacityIncludesExistingReservations(int capacity, int occupied, bool selected, bool expected)
    {
        Assert.That(WFRoundSetupPolicy.CanSelectRole(capacity, occupied, selected), Is.EqualTo(expected));
    }

    /// <summary>Empty names use the default title and ordinary text is normalized predictably.</summary>
    [TestCase(null, "")]
    [TestCase("", "")]
    [TestCase("   ", "")]
    [TestCase("Salvage Crew", "Salvage Crew")]
    [TestCase("  Ship's   Medic  ", "Ship's Medic")]
    [TestCase("Engineer (Repairs)", "Engineer (Repairs)")]
    [TestCase("Cargo & Supply", "Cargo & Supply")]
    [TestCase("Team 2", "Team 2")]
    public void PlainRoleNamesAreAccepted(string input, string expected)
    {
        Assert.That(WFRoundSetupPolicy.TryNormalizeRoleName(input, out var result), Is.True);
        Assert.That(result, Is.EqualTo(expected));
    }

    /// <summary>Markup, controls and invisible directional text cannot become role labels.</summary>
    [TestCase("[color=red]Medic[/color]")]
    [TestCase("<b>Medic</b>")]
    [TestCase("{Medic}")]
    [TestCase("Medic\nOfficer")]
    [TestCase("Medic\rOfficer")]
    [TestCase("Medic\tOfficer")]
    [TestCase("Medic\0Officer")]
    [TestCase("Medic\u202E")]
    [TestCase("Medic\u200B")]
    [TestCase("---")]
    public void MalformedRoleNamesLeaveNoOutput(string input)
    {
        Assert.That(WFRoundSetupPolicy.TryNormalizeRoleName(input, out var result), Is.False);
        Assert.That(result, Is.Empty);
    }

    /// <summary>Role labels have a stable size limit before entering network snapshots.</summary>
    [Test]
    public void RoleNameLengthIsBounded()
    {
        Assert.That(WFRoundSetupPolicy.TryNormalizeRoleName(new string('A', 64), out _), Is.True);
        Assert.That(WFRoundSetupPolicy.TryNormalizeRoleName(new string('A', 65), out _), Is.False);
        Assert.That(WFRoundSetupPolicy.TryNormalizeRoleName(new string(' ', 1024), out _), Is.False);
    }
}
