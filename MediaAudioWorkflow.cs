using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VpyAudioCutter;

public sealed record AudioTrackInfo(
    int AudioIndex,
    int StreamIndex,
    int? Eac3toTrackNumber,
    string Codec,
    string Description,
    string PreferredExtension,
    bool UseBeSplit,
    int? BitrateKbps)
{
    public override string ToString()
    {
        var route = UseBeSplit ? "BeSplit" : "ffmpeg 自适应";
        return $"{AudioIndex + 1}: {Description} [{route}]";
    }
}

public sealed record AudioCutWorkflowResult(
    string OutputPath,
    bool UsedBeSplit,
    bool PacketBoundaryCut,
    bool Reencoded);

public sealed record PacketBoundaryAnalysis(
    bool AllBoundariesAligned,
    int SampleRate,
    IReadOnlyList<double> MisalignedBoundaries);

public static class MediaToolLocator
{
    public static string? FindFfmpeg(string? savedPath, string? beSplitPath)
    {
        return FindExecutable(
            "ffmpeg.exe",
            savedPath,
            Environment.GetEnvironmentVariable("VPY_AUDIO_CUTTER_FFMPEG"),
            beSplitPath,
            "ffmpeg");
    }

    public static string? FindEac3to(string? savedPath, string? beSplitPath)
    {
        return FindExecutable(
            "eac3to.exe",
            savedPath,
            Environment.GetEnvironmentVariable("VPY_AUDIO_CUTTER_EAC3TO"),
            beSplitPath,
            "eac3to");
    }

    private static string? FindExecutable(
        string executableName,
        string? savedPath,
        string? environmentPath,
        string? beSplitPath,
        string meGuiToolDirectory)
    {
        foreach (var candidate in GetCandidates(
                     executableName,
                     savedPath,
                     environmentPath,
                     beSplitPath,
                     meGuiToolDirectory))
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

    private static IEnumerable<string?> GetCandidates(
        string executableName,
        string? savedPath,
        string? environmentPath,
        string? beSplitPath,
        string meGuiToolDirectory)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new List<string?>
        {
            savedPath,
            environmentPath,
            Path.Combine(baseDirectory, executableName),
            Path.Combine(baseDirectory, "tools", meGuiToolDirectory, executableName),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "tools", meGuiToolDirectory, executableName))
        };

        var inferredMeGuiRoot = InferMeGuiRoot(beSplitPath);
        if (inferredMeGuiRoot is not null)
            candidates.Add(Path.Combine(inferredMeGuiRoot, "tools", meGuiToolDirectory, executableName));

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!process.ProcessName.Contains("megui", StringComparison.OrdinalIgnoreCase))
                    continue;

                var executable = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(executable))
                {
                    candidates.Add(
                        Path.Combine(
                            Path.GetDirectoryName(executable)!,
                            "tools",
                            meGuiToolDirectory,
                            executableName));
                }
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
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (var directory in pathValue.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                candidates.Add(Path.Combine(directory, executableName));
            }
        }

        return candidates;
    }

    private static string? InferMeGuiRoot(string? beSplitPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(beSplitPath))
                return null;

            var directory = Directory.GetParent(Path.GetFullPath(beSplitPath));
            if (directory?.Name.Equals("besplit", StringComparison.OrdinalIgnoreCase) != true)
                return null;

            var toolsDirectory = directory.Parent;
            return toolsDirectory?.Name.Equals("tools", StringComparison.OrdinalIgnoreCase) == true
                ? toolsDirectory.Parent?.FullName
                : null;
        }
        catch
        {
            return null;
        }
    }
}

public static partial class MediaAudioProbe
{
    [GeneratedRegex(
        @"Stream #\d+:(?<stream>\d+)(?:\[[^\]]+\])?(?:\((?<language>[^)]*)\))?:\s*Audio:\s*(?<codec>[^,\s]+)(?<details>.*)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex FfmpegAudioStreamRegex();

    [GeneratedRegex(
        @"(?m)^\s*(?<track>\d+):\s*(?<description>(?:RAW/PCM|AAC|AC3|E-AC3|DTS|TrueHD|MP2|MP3|FLAC|PCM|WAV|Opus|Vorbis).*)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex Eac3toAudioTrackRegex();

    public static async Task<IReadOnlyList<AudioTrackInfo>> ProbeAsync(
        string ffmpegPath,
        string? eac3toPath,
        string inputPath,
        CancellationToken cancellationToken)
    {
        var result = await ExternalProcessRunner.RunAsync(
            ffmpegPath,
            ["-hide_banner", "-i", inputPath],
            progress: null,
            cancellationToken);

        var tracks = ParseFfmpegOutput(result.Output);
        if (tracks.Count == 0)
            throw new InvalidOperationException("ffmpeg 没有在输入文件中找到音轨。");

        if (!string.IsNullOrWhiteSpace(eac3toPath))
        {
            try
            {
                var eac3toResult = await ExternalProcessRunner.RunAsync(
                    eac3toPath,
                    [inputPath],
                    progress: null,
                    cancellationToken);
                tracks = ApplyEac3toTrackNumbers(tracks, eac3toResult.Output);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
        }

        return tracks;
    }

    public static List<AudioTrackInfo> ParseFfmpegOutput(string output)
    {
        var tracks = new List<AudioTrackInfo>();
        foreach (var rawLine in output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = FfmpegAudioStreamRegex().Match(rawLine);
            if (!match.Success)
                continue;

            var codec = match.Groups["codec"].Value.Trim().ToLowerInvariant();
            var details = match.Groups["details"].Value.Trim();
            var language = match.Groups["language"].Value.Trim();
            var description = BuildDescription(codec, language, details);
            var preferredExtension = MediaCodecPolicy.GetPreferredExtension(codec, details);
            tracks.Add(
                new AudioTrackInfo(
                    tracks.Count,
                    int.Parse(match.Groups["stream"].Value, CultureInfo.InvariantCulture),
                    null,
                    codec,
                    description,
                    preferredExtension,
                    MediaCodecPolicy.CanUseBeSplit(codec, details),
                    ParseBitrate(details)));
        }

        return tracks;
    }

    public static List<AudioTrackInfo> ApplyEac3toTrackNumbers(
        IReadOnlyList<AudioTrackInfo> tracks,
        string eac3toOutput)
    {
        var cleaned = new string(
            eac3toOutput
                .Where(character => character == '\r' || character == '\n' || character == '\t' || !char.IsControl(character))
                .ToArray());
        var trackNumbers = Eac3toAudioTrackRegex()
            .Matches(cleaned)
            .Select(match => int.Parse(match.Groups["track"].Value, CultureInfo.InvariantCulture))
            .ToArray();

        var result = tracks.ToList();
        for (var index = 0; index < result.Count && index < trackNumbers.Length; index++)
            result[index] = result[index] with { Eac3toTrackNumber = trackNumbers[index] };

        return result;
    }

    private static string BuildDescription(string codec, string language, string details)
    {
        var label = MediaCodecPolicy.GetCodecLabel(codec, details);
        var detailText = details.TrimStart(' ', ',');
        if (!string.IsNullOrWhiteSpace(language))
            label += $" ({language})";
        if (!string.IsNullOrWhiteSpace(detailText))
            label += $", {detailText}";
        return label;
    }

    private static int? ParseBitrate(string details)
    {
        var match = Regex.Match(details, @"(?<bitrate>\d+)\s*kb/s", RegexOptions.IgnoreCase);
        return match.Success
            ? int.Parse(match.Groups["bitrate"].Value, CultureInfo.InvariantCulture)
            : null;
    }
}

public static class MediaCodecPolicy
{
    public static AudioTrackInfo? CreateDirectTrack(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var codec = extension switch
        {
            ".aac" => "aac",
            ".ac3" => "ac3",
            ".dts" => "dts",
            ".mp2" => "mp2",
            ".mp3" => "mp3",
            ".pcm" or ".wav" => "pcm_s16le",
            _ => null
        };
        if (codec is null)
            return null;

        return new AudioTrackInfo(
            0,
            0,
            null,
            codec,
            GetCodecLabel(codec, string.Empty),
            extension,
            UseBeSplit: true,
            BitrateKbps: null);
    }

    public static bool CanUseBeSplit(string codec, string details)
    {
        if (codec.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase))
            return true;

        if (codec.Equals("dts", StringComparison.OrdinalIgnoreCase) &&
            details.Contains("DTS-HD", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return codec is "aac" or "ac3" or "dts" or "mp2" or "mp3";
    }

    public static string GetPreferredExtension(string codec, string details)
    {
        if (codec.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase))
            return ".wav";

        return codec switch
        {
            "aac" => ".aac",
            "ac3" => ".ac3",
            "dts" => ".dts",
            "mp2" => ".mp2",
            "mp3" => ".mp3",
            "flac" => ".flac",
            "eac3" => ".eac3",
            "truehd" => ".thd",
            "opus" => ".opus",
            "vorbis" => ".ogg",
            "alac" => ".m4a",
            "wavpack" => ".wv",
            _ => ".mka"
        };
    }

    public static string GetCodecLabel(string codec, string details)
    {
        if (codec.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase))
            return "PCM";

        if (codec.Equals("dts", StringComparison.OrdinalIgnoreCase) &&
            details.Contains("DTS-HD", StringComparison.OrdinalIgnoreCase))
        {
            return "DTS-HD";
        }

        return codec switch
        {
            "aac" => "AAC",
            "ac3" => "AC3",
            "dts" => "DTS",
            "mp2" => "MP2",
            "mp3" => "MP3",
            "flac" => "FLAC",
            "eac3" => "E-AC3",
            "truehd" => "TrueHD",
            "opus" => "Opus",
            "vorbis" => "Vorbis",
            "alac" => "ALAC",
            "wavpack" => "WavPack",
            _ => codec.ToUpperInvariant()
        };
    }
}

public static class MediaAudioDemuxer
{
    public static async Task<string> DemuxAsync(
        string inputPath,
        AudioTrackInfo track,
        string temporaryDirectory,
        string ffmpegPath,
        string? eac3toPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(temporaryDirectory, "source" + track.PreferredExtension);

        if (track.UseBeSplit &&
            !string.IsNullOrWhiteSpace(eac3toPath) &&
            track.Eac3toTrackNumber is int eac3toTrackNumber)
        {
            progress?.Report("eac3to 正在无转码抽取音轨...");
            try
            {
                var result = await ExternalProcessRunner.RunAsync(
                    eac3toPath,
                    [inputPath, $"{eac3toTrackNumber}:", outputPath],
                    progress,
                    cancellationToken);
                if (IsValidOutput(outputPath))
                    return outputPath;

                progress?.Report("eac3to 抽取失败，改用 ffmpeg 无转码抽取...");
                TryDelete(outputPath);
                _ = result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                progress?.Report("eac3to 抽取失败，改用 ffmpeg 无转码抽取...");
                TryDelete(outputPath);
            }
        }

        progress?.Report("ffmpeg 正在无转码抽取音轨...");
        var ffmpegResult = await ExternalProcessRunner.RunAsync(
            ffmpegPath,
            BuildFfmpegDemuxArguments(inputPath, outputPath, track),
            progress,
            cancellationToken);

        if (IsValidOutput(outputPath))
            return outputPath;

        if (!track.PreferredExtension.Equals(".mka", StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(outputPath);
            outputPath = Path.Combine(temporaryDirectory, "source.mka");
            ffmpegResult = await ExternalProcessRunner.RunAsync(
                ffmpegPath,
                [
                    "-hide_banner",
                    "-nostdin",
                    "-y",
                    "-i",
                    inputPath,
                    "-map",
                    $"0:a:{track.AudioIndex}",
                    "-vn",
                    "-sn",
                    "-dn",
                    "-c:a",
                    "copy",
                    outputPath
                ],
                progress,
                cancellationToken);
        }

        if (!IsValidOutput(outputPath))
            throw BuildFfmpegException("ffmpeg 没有生成有效的抽取音频。", ffmpegResult.Output);

        return outputPath;
    }

    private static bool IsValidOutput(string path)
    {
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    public static IReadOnlyList<string> BuildFfmpegDemuxArguments(
        string inputPath,
        string outputPath,
        AudioTrackInfo track)
    {
        var codec = track.Codec.Equals("pcm_bluray", StringComparison.OrdinalIgnoreCase)
            ? track.Description.Contains("24 bit", StringComparison.OrdinalIgnoreCase)
                ? "pcm_s24le"
                : "pcm_s16le"
            : "copy";
        return
        [
            "-hide_banner",
            "-nostdin",
            "-y",
            "-i",
            inputPath,
            "-map",
            $"0:a:{track.AudioIndex}",
            "-vn",
            "-sn",
            "-dn",
            "-c:a",
            codec,
            outputPath
        ];
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    internal static InvalidOperationException BuildFfmpegException(string message, string output)
    {
        var details = output
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeLast(12);
        return new InvalidOperationException(message + Environment.NewLine + string.Join(Environment.NewLine, details));
    }
}

public static class FfmpegStreamCopyCutter
{
    public static IReadOnlyList<string> BuildSegmentArguments(
        string inputPath,
        string outputPath,
        FrameTimeRange range)
    {
        return
        [
            "-hide_banner",
            "-nostdin",
            "-y",
            "-i",
            inputPath,
            "-ss",
            range.StartSeconds.ToString("R", CultureInfo.InvariantCulture),
            "-t",
            (range.EndSeconds - range.StartSeconds).ToString("R", CultureInfo.InvariantCulture),
            "-map",
            "0:a:0",
            "-vn",
            "-sn",
            "-dn",
            "-c:a",
            "copy",
            "-avoid_negative_ts",
            "make_zero",
            outputPath
        ];
    }

    public static IReadOnlyList<string> BuildConcatArguments(string listPath, string outputPath)
    {
        return
        [
            "-hide_banner",
            "-nostdin",
            "-y",
            "-f",
            "concat",
            "-safe",
            "0",
            "-i",
            listPath,
            "-map",
            "0:a:0",
            "-c:a",
            "copy",
            outputPath
        ];
    }

    public static async Task CutAsync(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        double framerate,
        IReadOnlyList<TrimSection> sections,
        string temporaryDirectory,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var normalized = BeSplitAudioCutter.NormalizeSections(sections);
        if (normalized.Count == 0)
            throw new InvalidOperationException("没有可切割的 Trim 片段。");

        var segmentExtension = Path.GetExtension(inputPath);
        var segmentPaths = new List<string>(normalized.Count);
        for (var index = 0; index < normalized.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"ffmpeg 正在无转码切割片段 {index + 1}/{normalized.Count}...");
            var segmentPath = Path.Combine(
                temporaryDirectory,
                $"s_{index + 1:00}{segmentExtension}");
            var range = BeSplitAudioCutter.ToTimeRange(normalized[index], framerate);
            var result = await ExternalProcessRunner.RunAsync(
                ffmpegPath,
                BuildSegmentArguments(inputPath, segmentPath, range),
                progress,
                cancellationToken);
            if (!File.Exists(segmentPath) || new FileInfo(segmentPath).Length == 0)
                throw MediaAudioDemuxer.BuildFfmpegException("ffmpeg 没有生成有效的音频片段。", result.Output);

            segmentPaths.Add(segmentPath);
        }

        if (segmentPaths.Count == 1)
        {
            progress?.Report("只有一个保留片段，正在写入输出音频...");
            await CopyFileAsync(segmentPaths[0], outputPath, cancellationToken);
            return;
        }

        var listPath = Path.Combine(temporaryDirectory, "join.ffconcat");
        var listLines = new List<string> { "ffconcat version 1.0" };
        listLines.AddRange(segmentPaths.Select(path => $"file '{path.Replace('\\', '/')}'"));
        await File.WriteAllLinesAsync(listPath, listLines, new UTF8Encoding(false), cancellationToken);

        progress?.Report("ffmpeg 正在无转码拼接保留片段...");
        var temporaryOutputPath = Path.Combine(temporaryDirectory, "joined" + Path.GetExtension(outputPath));
        var concatResult = await ExternalProcessRunner.RunAsync(
            ffmpegPath,
            BuildConcatArguments(listPath, temporaryOutputPath),
            progress,
            cancellationToken);
        if (!File.Exists(temporaryOutputPath) || new FileInfo(temporaryOutputPath).Length == 0)
            throw MediaAudioDemuxer.BuildFfmpegException("ffmpeg 没有生成有效的拼接音频。", concatResult.Output);

        await CopyFileAsync(temporaryOutputPath, outputPath, cancellationToken);
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
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
}

public static partial class FfmpegPacketBoundaryAnalyzer
{
    [GeneratedRegex(@"^#tb\s+0:\s*(?<num>\d+)/(?<den>\d+)\r?$", RegexOptions.Multiline)]
    private static partial Regex TimeBaseRegex();

    [GeneratedRegex(@"^#sample_rate\s+0:\s*(?<rate>\d+)\r?$", RegexOptions.Multiline)]
    private static partial Regex SampleRateRegex();

    [GeneratedRegex(
        @"^\s*\d+,\s*-?\d+,\s*(?<pts>-?\d+),\s*(?<duration>\d+),",
        RegexOptions.Multiline)]
    private static partial Regex PacketRegex();

    public static IReadOnlyList<string> BuildProbeArguments(string inputPath, double boundarySeconds)
    {
        var seekSeconds = Math.Max(0, boundarySeconds - 0.1D);
        return
        [
            "-hide_banner",
            "-loglevel",
            "error",
            "-ss",
            seekSeconds.ToString("R", CultureInfo.InvariantCulture),
            "-copyts",
            "-i",
            inputPath,
            "-map",
            "0:a:0",
            "-frames:a",
            "64",
            "-c:a",
            "copy",
            "-f",
            "framecrc",
            "-"
        ];
    }

    public static async Task<PacketBoundaryAnalysis> AnalyzeAsync(
        string ffmpegPath,
        string inputPath,
        double framerate,
        IReadOnlyList<TrimSection> sections,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var boundaries = BeSplitAudioCutter.NormalizeSections(sections)
            .SelectMany(section =>
            {
                var range = BeSplitAudioCutter.ToTimeRange(section, framerate);
                return new[] { range.StartSeconds, range.EndSeconds };
            })
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        var misaligned = new List<double>();
        var sampleRate = 0;

        for (var index = 0; index < boundaries.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"ffmpeg 正在检测切点 {index + 1}/{boundaries.Length}...");
            var result = await ExternalProcessRunner.RunAsync(
                ffmpegPath,
                BuildProbeArguments(inputPath, boundaries[index]),
                progress: null,
                cancellationToken);
            var parsed = ParseProbeOutput(result.Output);
            sampleRate = parsed.SampleRate;
            var tolerance = 0.5D / sampleRate + 1e-9;
            if (!parsed.Boundaries.Any(value => Math.Abs(value - boundaries[index]) <= tolerance))
                misaligned.Add(boundaries[index]);
        }

        if (sampleRate <= 0)
            throw new InvalidOperationException("ffmpeg 没有返回有效的音频采样率。");

        return new PacketBoundaryAnalysis(misaligned.Count == 0, sampleRate, misaligned);
    }

    public static (int SampleRate, IReadOnlyList<double> Boundaries) ParseProbeOutput(string output)
    {
        var timeBaseMatch = TimeBaseRegex().Match(output);
        var sampleRateMatch = SampleRateRegex().Match(output);
        if (!timeBaseMatch.Success || !sampleRateMatch.Success)
            throw new InvalidOperationException("无法解析 ffmpeg 音频包边界信息。");

        var numerator = long.Parse(timeBaseMatch.Groups["num"].Value, CultureInfo.InvariantCulture);
        var denominator = long.Parse(timeBaseMatch.Groups["den"].Value, CultureInfo.InvariantCulture);
        var sampleRate = int.Parse(sampleRateMatch.Groups["rate"].Value, CultureInfo.InvariantCulture);
        var timeBase = numerator / (double)denominator;
        var boundaries = new HashSet<double>();
        foreach (Match match in PacketRegex().Matches(output))
        {
            var pts = long.Parse(match.Groups["pts"].Value, CultureInfo.InvariantCulture);
            var duration = long.Parse(match.Groups["duration"].Value, CultureInfo.InvariantCulture);
            boundaries.Add(pts * timeBase);
            boundaries.Add((pts + duration) * timeBase);
        }

        if (boundaries.Count == 0)
            throw new InvalidOperationException("ffmpeg 没有返回目标切点附近的音频包。");

        return (sampleRate, boundaries.OrderBy(value => value).ToArray());
    }
}

public readonly record struct SampleRange(long StartSample, long EndSampleExclusive);

public static class FfmpegExactAudioCutter
{
    public static SampleRange ToSampleRange(TrimSection section, double framerate, int sampleRate)
    {
        if (framerate <= 0)
            throw new ArgumentOutOfRangeException(nameof(framerate));
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        var startSample = checked((long)Math.Round(
            section.StartFrame * (double)sampleRate / framerate,
            MidpointRounding.AwayFromZero));
        var endSample = checked((long)Math.Round(
            (section.EndFrame + 1D) * sampleRate / framerate,
            MidpointRounding.AwayFromZero));
        if (endSample <= startSample)
            throw new InvalidOperationException("计算出的精确音频采样范围为空。");

        return new SampleRange(startSample, endSample);
    }

    public static string BuildFilter(
        double framerate,
        int sampleRate,
        IReadOnlyList<TrimSection> sections)
    {
        var normalized = BeSplitAudioCutter.NormalizeSections(sections);
        if (normalized.Count == 0)
            throw new InvalidOperationException("没有可切割的 Trim 片段。");

        var builder = new StringBuilder();
        if (normalized.Count == 1)
        {
            var range = ToSampleRange(normalized[0], framerate, sampleRate);
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "[0:a:0]atrim=start_sample={0}:end_sample={1},asetpts=PTS-STARTPTS[out]",
                range.StartSample,
                range.EndSampleExclusive);
            return builder.ToString();
        }

        builder.AppendFormat(CultureInfo.InvariantCulture, "[0:a:0]asplit={0}", normalized.Count);
        for (var index = 0; index < normalized.Count; index++)
            builder.AppendFormat(CultureInfo.InvariantCulture, "[s{0}]", index);
        builder.Append(';');

        for (var index = 0; index < normalized.Count; index++)
        {
            var range = ToSampleRange(normalized[index], framerate, sampleRate);
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "[s{0}]atrim=start_sample={1}:end_sample={2},asetpts=PTS-STARTPTS[a{0}];",
                index,
                range.StartSample,
                range.EndSampleExclusive);
        }

        for (var index = 0; index < normalized.Count; index++)
            builder.AppendFormat(CultureInfo.InvariantCulture, "[a{0}]", index);
        builder.AppendFormat(
            CultureInfo.InvariantCulture,
            "concat=n={0}:v=0:a=1[out]",
            normalized.Count);
        return builder.ToString();
    }

    public static IReadOnlyList<string> BuildArguments(
        string inputPath,
        string outputPath,
        string codec,
        int? bitrateKbps,
        string filter)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            "-nostdin",
            "-y",
            "-i",
            inputPath,
            "-filter_complex",
            filter,
            "-map",
            "[out]",
            "-c:a",
            GetEncoder(codec)
        };

        if (bitrateKbps is > 0 && CodecUsesBitrate(codec))
        {
            arguments.Add("-b:a");
            arguments.Add($"{bitrateKbps.Value}k");
        }

        if (codec is "truehd" or "dts")
        {
            arguments.Add("-strict");
            arguments.Add("-2");
        }

        arguments.Add(outputPath);
        return arguments;
    }

    public static async Task CutAsync(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        string codec,
        int? bitrateKbps,
        double framerate,
        int sampleRate,
        IReadOnlyList<TrimSection> sections,
        string temporaryDirectory,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("切点未对齐，ffmpeg 正在精确裁切并编码回原格式...");
        var temporaryOutputPath = Path.Combine(temporaryDirectory, "exact" + Path.GetExtension(outputPath));
        var filter = BuildFilter(framerate, sampleRate, sections);
        var result = await ExternalProcessRunner.RunAsync(
            ffmpegPath,
            BuildArguments(inputPath, temporaryOutputPath, codec, bitrateKbps, filter),
            progress,
            cancellationToken);
        if (!File.Exists(temporaryOutputPath) || new FileInfo(temporaryOutputPath).Length == 0)
            throw MediaAudioDemuxer.BuildFfmpegException("ffmpeg 精确切割没有生成有效输出。", result.Output);

        await CopyFileAsync(temporaryOutputPath, outputPath, cancellationToken);
    }

    private static string GetEncoder(string codec)
    {
        return codec switch
        {
            "opus" => "libopus",
            "vorbis" => "libvorbis",
            "dts" => "dca",
            _ => codec
        };
    }

    private static bool CodecUsesBitrate(string codec)
    {
        return codec is "aac" or "ac3" or "eac3" or "mp2" or "mp3" or "opus" or "vorbis" or "dts";
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
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
}

public static partial class FfmpegAudioOutputValidator
{
    [GeneratedRegex(@"nb_samples:(?<samples>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SampleCountRegex();

    [GeneratedRegex(
        @"Duration:\s*(?<hours>\d+):(?<minutes>\d+):(?<seconds>\d+(?:\.\d+)?)",
        RegexOptions.IgnoreCase)]
    private static partial Regex DurationRegex();

    public static long GetExpectedSampleCount(
        double framerate,
        int sampleRate,
        IReadOnlyList<TrimSection> sections)
    {
        return BeSplitAudioCutter.NormalizeSections(sections)
            .Select(section => FfmpegExactAudioCutter.ToSampleRange(section, framerate, sampleRate))
            .Sum(range => checked(range.EndSampleExclusive - range.StartSample));
    }

    public static async Task<bool> ValidateAsync(
        string ffmpegPath,
        string outputPath,
        long expectedSamples,
        int sampleRate,
        CancellationToken cancellationToken)
    {
        var result = await ExternalProcessRunner.RunAsync(
            ffmpegPath,
            [
                "-hide_banner",
                "-i",
                outputPath,
                "-map",
                "0:a:0",
                "-af",
                "ashowinfo",
                "-f",
                "null",
                "-"
            ],
            progress: null,
            cancellationToken);

        var decodedSamples = SampleCountRegex()
            .Matches(result.Output)
            .Select(match => long.Parse(match.Groups["samples"].Value, CultureInfo.InvariantCulture))
            .Sum();
        if (decodedSamples != expectedSamples)
            return false;

        if (!Path.GetExtension(outputPath).Equals(".flac", StringComparison.OrdinalIgnoreCase))
            return true;

        var durationMatch = DurationRegex().Match(result.Output);
        if (!durationMatch.Success)
            return false;

        var reportedDuration =
            int.Parse(durationMatch.Groups["hours"].Value, CultureInfo.InvariantCulture) * 3600D +
            int.Parse(durationMatch.Groups["minutes"].Value, CultureInfo.InvariantCulture) * 60D +
            double.Parse(durationMatch.Groups["seconds"].Value, CultureInfo.InvariantCulture);
        var expectedDuration = expectedSamples / (double)sampleRate;
        return Math.Abs(reportedDuration - expectedDuration) <= 0.02D;
    }
}

public static class AudioCutWorkflow
{
    public static async Task<AudioCutWorkflowResult> CutAsync(
        string inputPath,
        string outputPath,
        AudioTrackInfo track,
        double framerate,
        IReadOnlyList<TrimSection> sections,
        string? beSplitPath,
        string? ffmpegPath,
        string? eac3toPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "VAC",
            Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var directBeSplitInput =
                track.UseBeSplit &&
                track.AudioIndex == 0 &&
                BeSplitAudioCutter.IsSupportedAudioPath(inputPath) &&
                string.Equals(
                    Path.GetExtension(inputPath),
                    track.PreferredExtension,
                    StringComparison.OrdinalIgnoreCase);
            var extractedPath = inputPath;
            if (!directBeSplitInput)
            {
                if (string.IsNullOrWhiteSpace(ffmpegPath))
                    throw new InvalidOperationException("当前输入需要 ffmpeg，但没有找到 ffmpeg.exe。");

                extractedPath = await MediaAudioDemuxer.DemuxAsync(
                    inputPath,
                    track,
                    temporaryDirectory,
                    ffmpegPath,
                    eac3toPath,
                    progress,
                    cancellationToken);
            }

            if (track.UseBeSplit)
            {
                if (string.IsNullOrWhiteSpace(beSplitPath))
                    throw new InvalidOperationException("当前音轨需要 BeSplit，但没有找到 besplit.exe。");

                await BeSplitAudioCutter.CutAsync(
                    beSplitPath,
                    extractedPath,
                    outputPath,
                    framerate,
                    sections,
                    progress,
                    cancellationToken);
                return new AudioCutWorkflowResult(
                    outputPath,
                    UsedBeSplit: true,
                    PacketBoundaryCut: false,
                    Reencoded: false);
            }

            if (string.IsNullOrWhiteSpace(ffmpegPath))
                throw new InvalidOperationException("当前音轨需要 ffmpeg，但没有找到 ffmpeg.exe。");

            var boundaryAnalysis = await FfmpegPacketBoundaryAnalyzer.AnalyzeAsync(
                ffmpegPath,
                extractedPath,
                framerate,
                sections,
                progress,
                cancellationToken);
            if (boundaryAnalysis.AllBoundariesAligned)
            {
                try
                {
                    progress?.Report("所有切点均对齐音频包边界，ffmpeg 将无转码直通...");
                    await FfmpegStreamCopyCutter.CutAsync(
                        ffmpegPath,
                        extractedPath,
                        outputPath,
                        framerate,
                        sections,
                        temporaryDirectory,
                        progress,
                        cancellationToken);
                    var expectedSamples = FfmpegAudioOutputValidator.GetExpectedSampleCount(
                        framerate,
                        boundaryAnalysis.SampleRate,
                        sections);
                    if (await FfmpegAudioOutputValidator.ValidateAsync(
                            ffmpegPath,
                            outputPath,
                            expectedSamples,
                            boundaryAnalysis.SampleRate,
                            cancellationToken))
                    {
                        return new AudioCutWorkflowResult(
                            outputPath,
                            UsedBeSplit: false,
                            PacketBoundaryCut: true,
                            Reencoded: false);
                    }

                    progress?.Report("无转码输出的采样数或时长元数据不正确，自动回退到同格式重编码...");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    progress?.Report($"ffmpeg 无转码直通失败，自动回退到同格式重编码：{exception.Message}");
                }
            }

            await FfmpegExactAudioCutter.CutAsync(
                ffmpegPath,
                extractedPath,
                outputPath,
                track.Codec,
                track.BitrateKbps,
                framerate,
                boundaryAnalysis.SampleRate,
                sections,
                temporaryDirectory,
                progress,
                cancellationToken);
            return new AudioCutWorkflowResult(
                outputPath,
                UsedBeSplit: false,
                PacketBoundaryCut: false,
                Reencoded: true);
        }
        finally
        {
            TryDeleteTemporaryDirectory(temporaryDirectory);
        }
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
