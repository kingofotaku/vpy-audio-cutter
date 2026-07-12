using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace VpyAudioCutter;

public static class BeSplitLocator
{
    public static string? FindAutomatically(string? savedPath)
    {
        foreach (var candidate in GetCandidates(savedPath))
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch
            {
            }
        }

        return null;
    }

    private static IEnumerable<string?> GetCandidates(string? savedPath)
    {
        var candidates = new List<string?>
        {
            savedPath,
            Environment.GetEnvironmentVariable("VPY_AUDIO_CUTTER_BESPLIT")
        };

        var baseDirectory = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDirectory, "besplit.exe"));
        candidates.Add(Path.Combine(baseDirectory, "tools", "besplit", "besplit.exe"));
        candidates.Add(Path.GetFullPath(Path.Combine(baseDirectory, "..", "tools", "besplit", "besplit.exe")));

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!process.ProcessName.Contains("megui", StringComparison.OrdinalIgnoreCase))
                    continue;

                var executable = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(executable))
                    candidates.Add(Path.Combine(Path.GetDirectoryName(executable)!, "tools", "besplit", "besplit.exe"));
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
            return candidates;

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            candidates.Add(Path.Combine(directory, "besplit.exe"));

        return candidates;
    }
}

public readonly record struct FrameTimeRange(double StartSeconds, double EndSeconds);

public static class BeSplitAudioCutter
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".aac",
        ".ac3",
        ".dts",
        ".mp2",
        ".mp3",
        ".pcm",
        ".wav"
    };

    public static bool IsSupportedAudioPath(string path)
    {
        return SupportedExtensions.Contains(Path.GetExtension(path));
    }

    public static IReadOnlyList<TrimSection> NormalizeSections(IReadOnlyList<TrimSection> sections)
    {
        var ordered = sections.OrderBy(section => section.StartFrame).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].StartFrame <= ordered[i - 1].EndFrame)
                throw new InvalidOperationException("Trim 片段存在重叠，无法按 MeGUI Audio Cutter 的规则处理。");
        }

        return ordered;
    }

    public static string BuildSplitArguments(
        string input,
        string prefix,
        string extension,
        double framerate,
        IReadOnlyList<TrimSection> sections)
    {
        var type = extension.TrimStart('.');
        var builder = new StringBuilder();
        builder.AppendFormat(
            CultureInfo.InvariantCulture,
            "-core( -input \"{0}\" -prefix \"{1}\" -type {2} -a ) -split( ",
            input,
            prefix,
            type);

        foreach (var section in sections)
        {
            var timeRange = ToTimeRange(section, framerate);
            builder.Append(timeRange.StartSeconds.ToString("R", CultureInfo.InvariantCulture));
            builder.Append(' ');
            builder.Append(timeRange.EndSeconds.ToString("R", CultureInfo.InvariantCulture));
            builder.Append(' ');
        }

        builder.Append(')');
        return builder.ToString();
    }

    // VPY Trim is inclusive at both frame boundaries. BeSplit receives a half-open
    // time interval, so the end is advanced by exactly one frame.
    public static FrameTimeRange ToTimeRange(TrimSection section, double framerate)
    {
        if (framerate <= 0)
            throw new ArgumentOutOfRangeException(nameof(framerate));

        return new FrameTimeRange(
            section.StartFrame / framerate,
            (section.EndFrame + 1D) / framerate);
    }

    public static string BuildJoinArguments(string listPath, string outputPath)
    {
        return $"-core ( -input \"{listPath}\" -prefix \"{outputPath}\" -type lst -join )";
    }

    public static string[] GenerateNumberedFilenames(string prefix, string extension, int count)
    {
        return Enumerable.Range(1, count)
            .Select(index => prefix + index.ToString("00", CultureInfo.InvariantCulture) + extension)
            .ToArray();
    }

    public static async Task CutAsync(
        string beSplitPath,
        string inputPath,
        string outputPath,
        double framerate,
        IReadOnlyList<TrimSection> sections,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeSections(sections);
        if (normalized.Count == 0)
            throw new InvalidOperationException("没有可切割的 Trim 片段。");

        var extension = Path.GetExtension(inputPath);
        if (!IsSupportedAudioPath(inputPath))
            throw new InvalidOperationException($"MeGUI Audio Cutter 不支持输入格式：{extension}");

        if (!string.Equals(extension, Path.GetExtension(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("输入音频和输出音频的扩展名必须一致。");

        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "VAC",
            Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var prefix = Path.Combine(temporaryDirectory, "p_");
            var numberedFiles = GenerateNumberedFilenames(prefix, extension, normalized.Count * 2);
            var keptFiles = numberedFiles.Where((_, index) => index % 2 == 0).ToArray();

            progress?.Report("BeSplit 正在切割音频...");
            var splitArguments = BuildSplitArguments(inputPath, prefix, extension, framerate, normalized);
            var splitResult = await RunBeSplitAsync(beSplitPath, splitArguments, progress, cancellationToken);
            if (splitResult.Contains("Usage", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("BeSplit 拒绝了切割命令，请检查输入格式和路径。");

            var missingFile = keptFiles.FirstOrDefault(file => !File.Exists(file));
            if (missingFile is not null)
                throw new InvalidOperationException($"BeSplit 没有生成预期的音频片段：{Path.GetFileName(missingFile)}");

            var listPath = Path.Combine(temporaryDirectory, "join.lst");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var ansiEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
            await File.WriteAllLinesAsync(listPath, keptFiles, ansiEncoding, cancellationToken);

            progress?.Report("BeSplit 正在拼接保留片段...");
            var temporaryOutputPath = Path.Combine(temporaryDirectory, "joined" + extension);
            var joinArguments = BuildJoinArguments(listPath, temporaryOutputPath);
            var joinResult = await RunBeSplitAsync(beSplitPath, joinArguments, progress, cancellationToken);
            if (joinResult.Contains("Usage", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("BeSplit 拒绝了拼接命令。");

            if (!File.Exists(temporaryOutputPath) || new FileInfo(temporaryOutputPath).Length == 0)
            {
                throw new InvalidOperationException(
                    "BeSplit 没有生成有效的输出音频。" +
                    Environment.NewLine +
                    SummarizeProcessOutput(joinResult));
            }

            progress?.Report("正在写入输出音频...");
            await CopyFileAsync(temporaryOutputPath, outputPath, cancellationToken);
            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
                throw new InvalidOperationException("无法写入最终输出音频。");
        }
        finally
        {
            TryDeleteTemporaryDirectory(temporaryDirectory);
        }
    }

    private static async Task<string> RunBeSplitAsync(
        string executable,
        string arguments,
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
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            },
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
                return;

            lock (outputLock)
                output.AppendLine(eventArgs.Data);
            if (eventArgs.Data.Contains("Writing", StringComparison.OrdinalIgnoreCase) ||
                eventArgs.Data.Contains("Seeking", StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(eventArgs.Data.Trim());
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                lock (outputLock)
                    output.AppendLine(eventArgs.Data);
            }
        };

        if (!process.Start())
            throw new InvalidOperationException("无法启动 BeSplit。");

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
            return output.ToString();
    }

    private static string SummarizeProcessOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return "BeSplit 没有返回诊断信息。";

        var lines = output
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeLast(12);
        return string.Join(Environment.NewLine, lines);
    }

    private static async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, 1024 * 1024, cancellationToken);
    }

    private static void TryDeleteTemporaryDirectory(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var expectedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "VAC"));
            if (fullPath.StartsWith(expectedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, true);
            }
        }
        catch
        {
        }
    }
}
