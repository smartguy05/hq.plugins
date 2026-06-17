using System.Net.Sockets;
using HQ.Plugins.Email;
using MailKit.Security;

namespace HQ.Plugins.Tests.Email;

public class EmailConnectionTestTests
{
    [Fact]
    public void DescribeConnectionError_AuthFailure_MentionsCredentials()
    {
        var msg = EmailService.DescribeConnectionError(new AuthenticationException("bad creds"));
        Assert.Contains("Authentication failed", msg);
    }

    [Fact]
    public void DescribeConnectionError_SocketError_MentionsUnreachableServer()
    {
        var msg = EmailService.DescribeConnectionError(new SocketException());
        Assert.Contains("Could not reach", msg);
    }

    [Fact]
    public void DescribeConnectionError_Timeout_MentionsTimedOut()
    {
        var msg = EmailService.DescribeConnectionError(new TimeoutException());
        Assert.Contains("timed out", msg);
    }

    [Fact]
    public void DescribeConnectionError_Unknown_FallsBackToMessage()
    {
        var msg = EmailService.DescribeConnectionError(new InvalidOperationException("something odd"));
        Assert.Equal("something odd", msg);
    }
}
