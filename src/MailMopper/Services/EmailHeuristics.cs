using System.Text.RegularExpressions;

namespace MailMopper.Services;

/// <summary>
/// Provider-agnostic heuristics over email headers/body that are used by both the
/// rule classifier and the ML feature extractor.
/// Avoid Gmail-specific assumptions here so this stays useful for future POP3/IMAP backends.
/// </summary>
public static class EmailHeuristics
{
    // Matches one or more chained reply/forward prefixes at the start of a subject:
    //   "Re:", "RE:", "Fwd:", "FW:", "Fw:", "Tr:" (French), "[Fwd:" / "[Re:" etc.
    // The optional `[` `]` brackets are intentional: some clients wrap the prefix
    // itself ("[Re:] subject"). List-style tags like "[my-list] Re: subject" are
    // matched starting at "Re:" and the "[my-list] " portion is not stripped — the
    // regex is anchored to ^ but \[? consumes zero chars when the bracketed text
    // isn't a recognised prefix word.
    private static readonly Regex ReplyForwardPrefix = new(
        @"^\s*(\[?\s*(re|fwd?|fw|tr|aw|sv)\s*\]?\s*:\s*)+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Common forwarded-message body markers (snippet usually starts near the top of the body).
    private static readonly Regex ForwardedBodyMarker = new(
        @"-{2,}\s*Forwarded message\s*-{2,}|Begin forwarded message:|^From:\s.+\s+Sent:\s|^From:\s.+\s+Date:\s",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Returns true if the subject starts with a Re:/Fwd: prefix, or the snippet
    /// contains a recognisable "forwarded message" marker.
    /// </summary>
    public static bool IsForwardOrReply(string? subject, string? snippet)
    {
        if (!string.IsNullOrWhiteSpace(subject) && ReplyForwardPrefix.IsMatch(subject))
            return true;

        if (!string.IsNullOrWhiteSpace(snippet) && ForwardedBodyMarker.IsMatch(snippet))
            return true;

        return false;
    }

    /// <summary>
    /// Strips leading Re:/Fwd:/FW:/etc. prefixes (recursively) from a subject,
    /// returning the "real" topic. Returns the input unchanged if no prefix is present.
    /// </summary>
    public static string StripReplyForwardPrefixes(string? subject)
    {
        if (string.IsNullOrEmpty(subject))
            return string.Empty;

        return ReplyForwardPrefix.Replace(subject, string.Empty).Trim();
    }
}
