using Microsoft.ML.Data;

namespace MailMopper.Models;

/// <summary>
/// ML.NET input model for email classification training and prediction.
/// Note: <see cref="GmailCategory"/> and <see cref="HasListUnsubscribe"/> are
/// Gmail-specific signals. They are tolerated as empty/false by other providers
/// (POP3/IMAP) — the model just learns less from them in that case.
/// </summary>
public class EmailFeature
{
    [LoadColumn(0)]
    public string From { get; set; } = string.Empty;

    [LoadColumn(1)]
    public string Subject { get; set; } = string.Empty;

    [LoadColumn(2)]
    public string Snippet { get; set; } = string.Empty;

    /// <summary>
    /// Sender domain (e.g. "gmail.com"). Pre-extracted from <see cref="From"/>
    /// so the model can learn from it as a categorical feature.
    /// </summary>
    [LoadColumn(3)]
    public string FromDomain { get; set; } = string.Empty;

    /// <summary>
    /// Gmail category label (e.g. "CATEGORY_PERSONAL"). Empty when the provider
    /// does not expose categories.
    /// </summary>
    [LoadColumn(4)]
    public string GmailCategory { get; set; } = string.Empty;

    /// <summary>
    /// Subject with leading Re:/Fwd:/FW: prefixes stripped, so the model focuses on
    /// the actual topic rather than the conversational marker.
    /// </summary>
    [LoadColumn(5)]
    public string CleanSubject { get; set; } = string.Empty;

    /// <summary>
    /// 1.0 when a List-Unsubscribe header is present (strong marketing/newsletter
    /// signal), 0.0 otherwise. Float for direct ML.NET feature concatenation.
    /// </summary>
    [LoadColumn(6)]
    public float HasListUnsubscribe { get; set; }

    /// <summary>
    /// 1.0 when the email is a forward or reply (subject prefix or forwarded-message
    /// body marker), 0.0 otherwise. Lets the model learn the conversational context
    /// explicitly.
    /// </summary>
    [LoadColumn(7)]
    public float IsForwardedOrReply { get; set; }

    /// <summary>
    /// Training-only label column. Not populated during prediction.
    /// </summary>
    [LoadColumn(8)]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Message ID for tracking — not used as a feature during training.
    /// </summary>
    public string MessageId { get; set; } = string.Empty;
}

/// <summary>
/// ML.NET output model for email classification prediction.
/// </summary>
public class EmailPrediction
{
    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; } = string.Empty;

    /// <summary>
    /// Per-category probability scores (order matches the label key mapping).
    /// </summary>
    [ColumnName("Score")]
    public float[] Score { get; set; } = [];
}
