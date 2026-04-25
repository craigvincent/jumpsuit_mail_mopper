using Microsoft.ML.Data;

namespace MailMopper.Models;

/// <summary>
/// ML.NET input model for email classification training and prediction.
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
    /// Training-only label column. Not populated during prediction.
    /// </summary>
    [LoadColumn(3)]
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
