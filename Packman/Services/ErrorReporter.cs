namespace Packman.Services;

/// <summary>
/// Single channel for failures that surface outside a normal call stack: awaited command
/// bodies and event handlers. Both would otherwise be "async void", where an unexpected
/// throw kills the process with no window and no log. App subscribes and shows the error.
/// </summary>
public static class ErrorReporter
{
    public static event Action<Exception>? Reported;

    public static void Report(Exception ex) => Reported?.Invoke(ex);

    /// <summary>
    /// Runs awaited work started from a void-returning event handler. Cancellation is a
    /// normal outcome; anything else is reported rather than left to tear the app down.
    /// </summary>
    public static async void FireAndForget(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }
}
