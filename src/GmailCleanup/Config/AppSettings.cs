namespace GmailCleanup.Config;

public class AppSettings
{
    public GmailSettings Gmail { get; set; } = new();
    public MlSettings Ml { get; set; } = new();
    public ClassificationSettings Classification { get; set; } = new();
    public ActionSettings Actions { get; set; } = new();
}

public class GmailSettings
{
    public string CredentialsPath { get; set; } = "credentials.json";
    public string TokenPath { get; set; } = "token.json";
    public int BatchSize { get; set; } = 100;
    public int MaxConcurrentRequests { get; set; } = 5;
    public string[] Scopes { get; set; } = ["https://www.googleapis.com/auth/gmail.modify"];
    public int AuthCallbackPort { get; set; }
}

public class MlSettings
{
    public string? ModelPath { get; set; }
    public int BatchSize { get; set; } = 500;
    public double MinConfidence { get; set; } = 0.5;
}

public class ClassificationSettings
{
    public string RulesPath { get; set; } = "rules/default-rules.json";
    public int NotificationMaxAgeDays { get; set; } = 90;
    public int TransactionalMaxAgeDays { get; set; } = 365;
}

public class ActionSettings
{
    public int TrashBatchSize { get; set; } = 1000;
    public bool DryRunDefault { get; set; } = true;
}
