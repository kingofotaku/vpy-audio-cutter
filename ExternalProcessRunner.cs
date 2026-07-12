using System.Diagnostics;
using System.Text;

namespace VpyAudioCutter;

public sealed record ProcessExecutionResult(int ExitCode, string Output);

public static class ExternalProcessRunner
{
    public static async Task<ProcessExecutionResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var outputLock = new object();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            },
            EnableRaisingEvents = true
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.OutputDataReceived += (_, eventArgs) => AppendLine(eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => AppendLine(eventArgs.Data);

        if (!process.Start())
            throw new InvalidOperationException($"无法启动 {Path.GetFileName(executable)}。");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch
            {
            }

            throw;
        }

        lock (outputLock)
            return new ProcessExecutionResult(process.ExitCode, output.ToString());

        void AppendLine(string? line)
        {
            if (line is null)
                return;

            lock (outputLock)
                output.AppendLine(line);
            if (line.Contains("time=", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Progress", StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(line.Trim());
            }
        }
    }
}
