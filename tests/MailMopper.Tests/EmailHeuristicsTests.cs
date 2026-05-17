using MailMopper.Services;

namespace MailMopper.Tests;

public class EmailHeuristicsTests
{
    [Theory]
    [InlineData("Re: lunch?", true)]
    [InlineData("RE: lunch?", true)]
    [InlineData("Fwd: Your order has been shipped", true)]
    [InlineData("FWD: Your order has been shipped", true)]
    [InlineData("FW: Your order has been shipped", true)]
    [InlineData("Fw: Your order has been shipped", true)]
    [InlineData("Re: Re: Re: lunch?", true)]
    [InlineData("Re: Fwd: original subject", true)]
    [InlineData("  re:  lunch?", true)]
    [InlineData("Tr: déjeuner ?", true)] // French
    [InlineData("AW: Frage", true)]      // German
    [InlineData("Sv: hej", true)]        // Swedish/Danish
    [InlineData("Your weekly digest", false)]
    [InlineData("Hey, want lunch?", false)]
    [InlineData("", false)]
    public void IsForwardOrReply_DetectsSubjectPrefixes(string subject, bool expected)
    {
        Assert.Equal(expected, EmailHeuristics.IsForwardOrReply(subject, snippet: null));
    }

    [Theory]
    [InlineData("---------- Forwarded message ----------\nFrom: someone")]
    [InlineData("Begin forwarded message: blah blah")]
    public void IsForwardOrReply_DetectsBodyMarkers(string snippet)
    {
        Assert.True(EmailHeuristics.IsForwardOrReply(subject: "Hello", snippet: snippet));
    }

    [Fact]
    public void IsForwardOrReply_NullInputs_ReturnsFalse()
    {
        Assert.False(EmailHeuristics.IsForwardOrReply(null, null));
    }

    [Theory]
    [InlineData("Re: lunch?", "lunch?")]
    [InlineData("RE: RE: Re: lunch?", "lunch?")]
    [InlineData("Fwd: Your order shipped", "Your order shipped")]
    [InlineData("Re: Fwd: original", "original")]
    [InlineData("Hello", "Hello")]
    [InlineData("", "")]
    public void StripReplyForwardPrefixes_RemovesAllPrefixes(string input, string expected)
    {
        Assert.Equal(expected, EmailHeuristics.StripReplyForwardPrefixes(input));
    }

    [Fact]
    public void StripReplyForwardPrefixes_NullInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, EmailHeuristics.StripReplyForwardPrefixes(null));
    }
}
