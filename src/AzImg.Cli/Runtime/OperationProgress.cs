using System.Diagnostics;
using Spectre.Console;

namespace AzImg.Cli.Runtime;

/// <summary>
/// Renders one-line progress for long-running operations without polluting structured stdout.
/// </summary>
public sealed class OperationProgress : IAsyncDisposable
{
    private static readonly TimeSpan LiveRefreshInterval = TimeSpan.FromMilliseconds(250);
    private static readonly string[] Frames = ["✦", "◆", "●", "◇"];

    private readonly TextWriter _writer;
    private readonly IAnsiConsole _console;
    private readonly bool _renderLive;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly Task _heartbeatTask;
    private readonly object _writeLock = new();
    private string _message;
    private int _frameIndex;
    private int _lastLineWidth;
    private bool _completed;

    private OperationProgress(TextWriter writer, string startedMessage)
    {
        _writer = writer;
        _message = startedMessage;
        AnsiConsoleOutput output = new(writer);
        _renderLive = ReferenceEquals(writer, Console.Error) && output.IsTerminal && !Console.IsErrorRedirected;
        _console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = _renderLive ? AnsiSupport.Yes : AnsiSupport.No,
            ColorSystem = _renderLive ? ColorSystemSupport.TrueColor : ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = output,
        });
        RenderCurrent();
        _heartbeatTask = _renderLive ? RunHeartbeatAsync() : Task.CompletedTask;
    }

    /// <summary>Starts progress reporting immediately.</summary>
    public static OperationProgress Start(TextWriter writer, string startedMessage)
        => new(writer, startedMessage);

    /// <summary>Writes an intermediate progress message.</summary>
    public void Report(string message)
    {
        _message = message;
        RenderCurrent();
    }

    /// <summary>Writes a completion message including elapsed time.</summary>
    public void Complete(string message)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _elapsed.Stop();
        RenderFinal($"{message} Elapsed: {FormatElapsed(_elapsed.Elapsed)}.");
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
            await Task.Delay(LiveRefreshInterval, _stopping.Token).ConfigureAwait(false);
            if (_completed)
            {
                return;
            }

            _frameIndex++;
            RenderCurrent();
        }
    }

    private void RenderCurrent()
        => RenderLine(_message, complete: false);

    private void RenderFinal(string message)
        => RenderLine(message, complete: true);

    private void RenderLine(string message, bool complete)
    {
        try
        {
            lock (_writeLock)
            {
                string markup = CreateMarkup(message, complete);
                if (_renderLive)
                {
                    int width = markup.RemoveMarkup().Length;
                    _writer.Write('\r');
                    _console.Markup(markup);
                    if (_lastLineWidth > width)
                    {
                        _writer.Write(new string(' ', _lastLineWidth - width));
                        _writer.Write('\r');
                        _console.Markup(markup);
                    }

                    _lastLineWidth = width;
                    if (complete)
                    {
                        _writer.WriteLine();
                    }

                    return;
                }

                if (!complete && _lastLineWidth > 0)
                {
                    return;
                }

                _console.MarkupLine(markup);
                _lastLineWidth = markup.RemoveMarkup().Length;
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private string CreateMarkup(string message, bool complete)
    {
        string icon = complete ? "✓" : Frames[_frameIndex % Frames.Length];
        string color = complete ? "green" : "deepskyblue1";
        string escaped = message.EscapeMarkup();
        return $"[{color} bold]{icon}[/] [bold]{escaped}[/] [grey58]·[/] [dim]{FormatElapsed(_elapsed.Elapsed)}[/]";
    }

    private static string FormatElapsed(TimeSpan elapsed)
        => elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes}:{elapsed.Seconds:00}";
}
