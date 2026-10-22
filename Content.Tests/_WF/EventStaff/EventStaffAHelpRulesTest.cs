using Content.Server._WF.EventStaff;
using Content.Shared._WF.EventStaff;
using Content.Shared.Administration;
using NUnit.Framework;

namespace Content.Tests._WF.EventStaff;

/// <summary>Admins get every ahelp; event staff get and answer only gameplay ones.</summary>
[TestFixture]
[TestOf(typeof(EventStaffAHelpRules))]
public sealed class EventStaffAHelpRulesTest
{
    [Test]
    public void AdminsReceiveBoth()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EventStaffAHelpRules.Receives(AdminFlags.Adminhelp, AHelpCategory.Player));
            Assert.That(EventStaffAHelpRules.Receives(AdminFlags.Adminhelp, AHelpCategory.Gameplay));
            Assert.That(EventStaffAHelpRules.CanAnswer(AdminFlags.Adminhelp, AHelpCategory.Player));
        });
    }

    [Test]
    public void EventStaffOnlyGameplay()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EventStaffAHelpRules.Receives(AdminFlags.Event, AHelpCategory.Gameplay));
            Assert.That(EventStaffAHelpRules.Receives(AdminFlags.Event, AHelpCategory.Player), Is.False);
            Assert.That(EventStaffAHelpRules.CanAnswer(AdminFlags.Event, AHelpCategory.Gameplay));
            Assert.That(EventStaffAHelpRules.CanAnswer(AdminFlags.Event, AHelpCategory.Player), Is.False);
            Assert.That(EventStaffAHelpRules.CanAnswer(AdminFlags.Event, null), "Event staff may open a new ahelp.");
        });
    }

    [Test]
    public void OthersReceiveNothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EventStaffAHelpRules.Receives(AdminFlags.Admin | AdminFlags.Fun, AHelpCategory.Gameplay), Is.False);
            Assert.That(EventStaffAHelpRules.CanAnswer(AdminFlags.None, null), Is.False);
        });
    }
}
