using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using gView.Pipe.Abstraction;

namespace gView.Pipe.Plugins.Scripts;

internal sealed record PipeProcessResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    bool Truncated,
    bool TimedOut,
    TimeSpan Duration);

/// <summary>Runs a process, captures its output (limited) and ends the whole process tree on timeout or cancel.</summary>
static internal class PipeProcessRunner
{
    public const int MaxOutputChars = 1024 * 1024;

    /// <param name="onLine">Every output line as it arrives; the flag is true for stderr.</param>
    static public async Task<PipeProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        Action<string, bool>? onLine,
        CancellationToken cancellationToken)
    {
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.RedirectStandardInput = true;
        startInfo.CreateNoWindow = true;
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;

        var stdout = new LimitedText();
        var stderr = new LimitedText();
        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => Receive(e.Data, stdout, false);
        process.ErrorDataReceived += (_, e) => Receive(e.Data, stderr, true);

        void Receive(string? line, LimitedText text, bool isError)
        {
            if (line is null)
            {
                return;
            }
            text.AppendLine(line);
            onLine?.Invoke(line, isError);
        }

        var watch = Stopwatch.StartNew();
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new PipeException($"Can't start '{startInfo.FileName}': {ex.Message}. Is it installed and on the PATH?", ex);
        }

        // scripts must not wait for input that never comes
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;
            Kill(process);
            if (!timedOut)
            {
                throw;
            }
        }

        // the async readers may still hold the last lines
        process.WaitForExit();
        watch.Stop();

        return new PipeProcessResult(
            timedOut ? -1 : process.ExitCode,
            stdout.ToString(),
            stderr.ToString(),
            stdout.Truncated || stderr.Truncated,
            timedOut,
            watch.Elapsed);
    }

    static private void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { } // already exited
    }

    /// <summary>Keeps the first <see cref="MaxOutputChars"/> characters.</summary>
    private sealed class LimitedText
    {
        private readonly StringBuilder _text = new();
        private readonly Lock _lock = new();

        public bool Truncated { get; private set; }

        public void AppendLine(string line)
        {
            lock (_lock)
            {
                if (_text.Length + line.Length + 1 > MaxOutputChars)
                {
                    Truncated = true;
                    return;
                }
                _text.Append(line).Append('\n');
            }
        }

        public override string ToString()
        {
            lock (_lock)
            {
                return Truncated ? _text + $"… (output truncated after {MaxOutputChars / 1024} KB)\n" : _text.ToString();
            }
        }
    }
}
