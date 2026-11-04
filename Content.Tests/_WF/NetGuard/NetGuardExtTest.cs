using Content.Shared._WF.NetGuard;
using NUnit.Framework;

namespace Content.Tests._WF.NetGuard;

[TestFixture]
public sealed class NetGuardExtTest
{
    [TestCase(0, 0, true)]
    [TestCase(10, 10, true)]
    [TestCase(5, 10, true)]
    [TestCase(11, 10, false)]
    [TestCase(-1, 10, false)]
    [TestCase(int.MaxValue, 64, false)]
    [TestCase(int.MinValue, 64, false)]
    public void LengthChecked(int length, int remaining, bool valid)
    {
        Assert.That(NetGuardExt.IsLengthValid(length, remaining), Is.EqualTo(valid));
    }
}
