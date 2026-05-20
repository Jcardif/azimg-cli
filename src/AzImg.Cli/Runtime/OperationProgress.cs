using System.Diagnostics;

namespace AzImg.Cli.Runtime;

/// <summary>
/// Writes low-volume progress messages for long-running operations without polluting structured stdout.
/// </summary>
public sealed class OperationProgress : IAsyncDisposable
{
    private static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(15);

    private readonly TextWriter _writer;
    private readonly string _heartbeatMessage;
    private readonly TimeSpan _heartbeatInterval;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly Task _heartbeatTask;
    private readonly object _writeLock = new();
    private bool _completed;

    private OperationProgress(TextWriter writer, string startedMessage, string heartbeatMessage, TimeSpan heartbeatInterval)
    {
        _writer = writer;
        _heartbeatMessage = heartbeatMessage;
        _heartbeatInterval = heartbeatInterval;
        WriteLine(startedMessage);
        _heartbeatTask = RunHeartbeatAsync();
    }

    /// <summary>Starts progress reporting immediately and then emits periodic heartbeat messages.</summary>
    public static OperationProgress Start(TextWriter writer, string startedMessage, string heartbeatMessage)
        => new(writer, startedMessage, heartbeatMessage, DefaultHeartbeatInterval);

    /// <summary>Writes an intermediate progress message.</summary>
    public void Report(string message)
        => WriteLine(message);

    /// <summary>Writes a completion message including elapsed time.</summary>
    public void Complete(string message)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _elapsed.Stop();
        WriteLine($"{message} Elapsed: {FormatElapsed(_elapsed.Elapsed)}.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);

        try
        {
            await _heartbeatTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
        finally
        {
            _stopping.Dispose();
        }
    }

    private async Task RunHeartbeatAsync()
    {
        while (true)
        {
            await Task.Delay(_heartbeatInterval, _stopping.Token).ConfigureAwait(false);
            if (_completed)
            {
                return;
            }

            WriteLine($"{_heartbeatMessage} Elapsed: {FormatElapsed(_elapsed.Elapsed)}.");
        }
    }

    private void WriteLine(string message)
    {
        try
        {
            lock (_writeLock)
            {
                _writer.WriteLine(message);
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static string FormatElapsed(TimeSpan elapsed)
        => elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes}:{elapsed.Seconds:00}";
}