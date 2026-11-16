using MouseBridge.App;
using Xunit;
namespace MouseBridge.Tests;
public class RfcommAdmissionPolicyTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    public void EnforcesOriginAndReportedProtection(bool secureListener, bool required,
        bool plain, bool expected) =>
        Assert.Equal(expected, RfcommAdmissionPolicy.CanAttach(secureListener, required, plain));
}
