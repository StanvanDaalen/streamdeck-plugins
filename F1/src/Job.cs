namespace F1;

/// <summary>
/// The cancellation of the long job that is running (F1 Mode, Race, Quali). It flows with the job's
/// async calls only, so waits elsewhere (polling, best of the rest) are never cancelled by it.
/// Holding a key to close F1 cancels it: every wait in the job then stops right away.
/// </summary>
public static class Job
{
    private static readonly AsyncLocal<CancellationToken> Current = new();

    public static CancellationToken Token { get => Current.Value; set => Current.Value = value; }

    public static Task Delay(int ms) => Task.Delay(ms, Token);

    /// <summary>Stops the job here when it was cancelled.</summary>
    public static void Check() => Token.ThrowIfCancellationRequested();
}
