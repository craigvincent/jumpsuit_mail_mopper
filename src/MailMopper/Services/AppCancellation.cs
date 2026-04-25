namespace MailMopper.Services;

public class AppCancellation
{
    public CancellationTokenSource Source { get; } = new();
    public CancellationToken Token => Source.Token;
}
