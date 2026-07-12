using VpyAudioCutter;

var source = """
import vapoursynth as vs
# core.std.Trim(src, 1, 2)
text = "core.std.Trim(src, 3, 4)"
START = 3253
clip = core.std.Trim(src, START, 84170)
clip = clip.trim(first=90000, length=101)
""";

var result = VpyTrimParser.Parse(source);
if (result.Sections.Count != 2)
    throw new InvalidOperationException($"Expected 2 sections, got {result.Sections.Count}.");

if (result.Sections[0] is not { StartFrame: 3253, EndFrame: 84170 })
    throw new InvalidOperationException("The first section was parsed incorrectly.");

if (result.Sections[1] is not { StartFrame: 90000, EndFrame: 90100 })
    throw new InvalidOperationException("The length-based section was parsed incorrectly.");

var avsSource = """
# MeGUI-generated AviSynth cuts
__film = last
__t0 = __film.trim(4579, 132251)
__t1 = Trim(140000, 150000)
__t2 = Trim(first_frame=160000, last_frame=170000)
__t0 ++ __t1 ++ __t2
""";
var avsResult = VpyTrimParser.Parse(avsSource, ScriptSyntax.AviSynth);
if (avsResult.Sections.Count != 3 ||
    avsResult.Sections[0] is not { StartFrame: 4579, EndFrame: 132251 } ||
    avsResult.Sections[1] is not { StartFrame: 140000, EndFrame: 150000 } ||
    avsResult.Sections[2] is not { StartFrame: 160000, EndFrame: 170000 })
{
    throw new InvalidOperationException("AviSynth Trim syntax was parsed incorrectly.");
}

var avsBoundarySource = """
first = Trim(0, -4)
second = Trim(3, end=7)
third = Trim(10, length=5)
unknown_end = Trim(100, 0)
single = Trim(0, end=0)
""";
var avsBoundaryResult = VpyTrimParser.Parse(avsBoundarySource, ScriptSyntax.AviSynth);
if (avsBoundaryResult.Sections.Count != 4 ||
    avsBoundaryResult.Sections[0] is not { StartFrame: 0, EndFrame: 3 } ||
    avsBoundaryResult.Sections[1] is not { StartFrame: 3, EndFrame: 7 } ||
    avsBoundaryResult.Sections[2] is not { StartFrame: 10, EndFrame: 14 } ||
    avsBoundaryResult.Sections[3] is not { StartFrame: 0, EndFrame: 0 } ||
    !avsBoundaryResult.Warnings.Any(warning => warning.Contains("直到片尾", StringComparison.Ordinal)))
{
    throw new InvalidOperationException("AviSynth boundary semantics were parsed incorrectly.");
}

var singleFrame = BeSplitAudioCutter.ToTimeRange(new TrimSection(100, 100, 1), 24);
if (Math.Abs(singleFrame.StartSeconds - (100D / 24D)) > 1e-12 ||
    Math.Abs(singleFrame.EndSeconds - (101D / 24D)) > 1e-12)
{
    throw new InvalidOperationException("Inclusive VPY frame conversion is off by one.");
}

var splitArguments = BeSplitAudioCutter.BuildSplitArguments(
    @"C:\input.aac",
    @"C:\temp\part_",
    ".aac",
    24,
    [new TrimSection(100, 100, 1)]);
if (!splitArguments.Contains($" {singleFrame.StartSeconds:R} {singleFrame.EndSeconds:R} ", StringComparison.Ordinal))
    throw new InvalidOperationException("The BeSplit command does not contain the exact frame interval.");

var numbered = BeSplitAudioCutter.GenerateNumberedFilenames(@"C:\temp\part_", ".aac", 4);
if (numbered.Length != 4 ||
    !numbered[0].EndsWith("part_01.aac", StringComparison.Ordinal) ||
    !numbered[2].EndsWith("part_03.aac", StringComparison.Ordinal))
{
    throw new InvalidOperationException("BeSplit numbered output names do not match MeGUI.");
}

try
{
    BeSplitAudioCutter.NormalizeSections(
    [
        new TrimSection(0, 100, 1),
        new TrimSection(100, 200, 2)
    ]);
    throw new InvalidOperationException("Overlapping sections were not rejected.");
}
catch (InvalidOperationException exception) when (exception.Message.Contains("重叠", StringComparison.Ordinal))
{
}

var ffmpegProbe = """
  Duration: 00:00:08.00, start: 1.400000, bitrate: 186 kb/s
  Stream #0:0[0x100](jpn): Audio: aac (LC), 48000 Hz, stereo, fltp, 156 kb/s
  Stream #0:1(eng): Audio: flac, 48000 Hz, stereo, s16
""";
var probedTracks = MediaAudioProbe.ParseFfmpegOutput(ffmpegProbe);
if (probedTracks.Count != 2 ||
    probedTracks[0] is not { AudioIndex: 0, StreamIndex: 0, Codec: "aac", PreferredExtension: ".aac", UseBeSplit: true, BitrateKbps: 156 } ||
    probedTracks[1] is not { AudioIndex: 1, StreamIndex: 1, Codec: "flac", PreferredExtension: ".flac", UseBeSplit: false })
{
    throw new InvalidOperationException("ffmpeg audio track output was parsed incorrectly.");
}

var eac3toProbe = "\b1: AAC, Japanese, 2.0 channels, 48kHz\r\n\b3: FLAC, English, 2.0 channels, 48kHz\r\n";
var mappedTracks = MediaAudioProbe.ApplyEac3toTrackNumbers(probedTracks, eac3toProbe);
if (mappedTracks[0].Eac3toTrackNumber != 1 || mappedTracks[1].Eac3toTrackNumber != 3)
    throw new InvalidOperationException("eac3to track numbers were not mapped by audio order.");

var frameCrc = """
#software: Lavf59.16.100
#tb 0: 1/48000
#media_type 0: audio
#codec_id 0: flac
#sample_rate 0: 48000
0,          0,          0,     4608,     1287, 0x00000000
0,       4608,       4608,     4608,     1277, 0x00000000
""";
var packetProbe = FfmpegPacketBoundaryAnalyzer.ParseProbeOutput(frameCrc);
if (packetProbe.SampleRate != 48000 ||
    !packetProbe.Boundaries.Any(value => Math.Abs(value - 0.096D) < 1e-12) ||
    !packetProbe.Boundaries.Any(value => Math.Abs(value - 0.192D) < 1e-12))
{
    throw new InvalidOperationException("ffmpeg packet boundaries were parsed incorrectly.");
}

var exactSampleRange = FfmpegExactAudioCutter.ToSampleRange(new TrimSection(100, 100, 1), 24, 48000);
if (exactSampleRange is not { StartSample: 200000, EndSampleExclusive: 202000 })
    throw new InvalidOperationException("Video frames were not converted to an exact PCM sample range.");

var exactFilter = FfmpegExactAudioCutter.BuildFilter(
    24,
    48000,
    [
        new TrimSection(0, 23, 1),
        new TrimSection(48, 71, 2)
    ]);
if (!exactFilter.Contains("atrim=start_sample=0:end_sample=48000", StringComparison.Ordinal) ||
    !exactFilter.Contains("atrim=start_sample=96000:end_sample=144000", StringComparison.Ordinal) ||
    !exactFilter.EndsWith("concat=n=2:v=0:a=1[out]", StringComparison.Ordinal))
{
    throw new InvalidOperationException("The exact-cut ffmpeg filter is incorrect.");
}

var expectedSamples = FfmpegAudioOutputValidator.GetExpectedSampleCount(
    24,
    48000,
    [
        new TrimSection(0, 23, 1),
        new TrimSection(48, 71, 2)
    ]);
if (expectedSamples != 96000)
    throw new InvalidOperationException("Expected exact output sample count was calculated incorrectly.");

var blurayPcmTrack = new AudioTrackInfo(
    0,
    1,
    2,
    "pcm_bluray",
    "PCM, 48000 Hz, stereo, s32 (24 bit), 2304 kb/s",
    ".wav",
    UseBeSplit: true,
    BitrateKbps: 2304);
var pcmDemuxArguments = MediaAudioDemuxer.BuildFfmpegDemuxArguments(
    @"C:\input.m2ts",
    @"C:\output.wav",
    blurayPcmTrack);
var pcmCodecIndex = pcmDemuxArguments.ToList().IndexOf("-c:a");
if (pcmCodecIndex < 0 || pcmDemuxArguments[pcmCodecIndex + 1] != "pcm_s24le")
    throw new InvalidOperationException("Blu-ray PCM should be written to a 24-bit PCM WAV fallback.");

var cltPath = Path.Combine(Path.GetTempPath(), $"vpy-audio-cutter-{Guid.NewGuid():N}.clt");
try
{
    CltWriter.Write(cltPath, 29.97003, "NO_TRANSITION", result.Sections);
    var clt = File.ReadAllText(cltPath);
    if (!clt.Contains("<Framerate>29.97003</Framerate>", StringComparison.Ordinal))
        throw new InvalidOperationException("The CLT framerate was not written correctly.");
}
finally
{
    if (File.Exists(cltPath))
        File.Delete(cltPath);
}

Console.WriteLine("VpyTrimParser smoke tests passed.");

foreach (var scriptPath in args)
{
    var syntax = string.Equals(Path.GetExtension(scriptPath), ".avs", StringComparison.OrdinalIgnoreCase)
        ? ScriptSyntax.AviSynth
        : ScriptSyntax.VapourSynth;
    var parsed = VpyTrimParser.Parse(File.ReadAllText(scriptPath), syntax);
    Console.WriteLine($"{Path.GetFileName(scriptPath)}: {parsed.Sections.Count} Trim section(s)");
    foreach (var section in parsed.Sections)
        Console.WriteLine($"  {section.StartFrame}-{section.EndFrame} (line {section.SourceLine})");
}
