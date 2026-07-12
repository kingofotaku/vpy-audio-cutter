using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace VpyAudioCutter;

public sealed class MainForm : Form
{
    private const string MediaInputFilter =
        "音频 / 视频媒体|*.aac;*.ac3;*.dts;*.mp2;*.mp3;*.pcm;*.wav;*.flac;*.eac3;*.thd;*.truehd;*.opus;*.ogg;*.mka;*.mkv;*.ts;*.m2ts;*.mts;*.mp4;*.m4a;*.mov;*.vob;*.evo;*.mpg;*.mpeg;*.webm|所有文件 (*.*)|*.*";

    private const string AudioOutputFilter =
        "音频文件|*.aac;*.ac3;*.dts;*.mp2;*.mp3;*.pcm;*.wav;*.flac;*.eac3;*.thd;*.opus;*.ogg;*.m4a;*.wv;*.mka|所有文件 (*.*)|*.*";

    private static readonly HashSet<string> MediaInputExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".aac", ".ac3", ".dts", ".mp2", ".mp3", ".pcm", ".wav",
        ".flac", ".eac3", ".thd", ".truehd", ".opus", ".ogg", ".m4a", ".wv",
        ".mka", ".mkv", ".ts", ".m2ts", ".mts", ".mp4", ".mov", ".vob", ".evo",
        ".mpg", ".mpeg", ".webm"
    };

    private static readonly HashSet<string> AudioOutputExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".aac", ".ac3", ".dts", ".mp2", ".mp3", ".pcm", ".wav",
        ".flac", ".eac3", ".thd", ".truehd", ".opus", ".ogg", ".m4a", ".wv", ".mka"
    };

    private readonly TextBox vpyPath = new();
    private readonly TextBox audioInputPath = new();
    private readonly TextBox audioOutputPath = new();
    private readonly TextBox cltPath = new();
    private readonly UnifiedComboBox framerate = new();
    private readonly UnifiedComboBox style = new() { IsTextReadOnly = true };
    private readonly UnifiedComboBox audioTrack = new() { IsTextReadOnly = true };
    private readonly DataGridView sections = new();
    private readonly Label status = new();
    private readonly ProgressBar progressBar = new();
    private readonly Button writeButton = new();
    private readonly Button cutButton = new();
    private readonly Button cancelButton = new();
    private readonly Button parseButton = new();
    private readonly Button analyzeButton = new();
    private readonly Button toolsButton = new();
    private readonly ToolTip toolTip = new();
    private readonly AppSettings settings;

    private CancellationTokenSource? cutCancellation;
    private CancellationTokenSource? probeCancellation;
    private string? probedInputPath;
    private string? automaticOutputPath;

    public MainForm()
    {
        settings = AppSettingsStore.Load();

        Text = "VPY / AVS Audio Cutter";
        var applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (applicationIcon is not null)
            Icon = applicationIcon;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(860, 620);
        Size = new Size(1020, 720);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        DragEnter += MainForm_DragEnter;
        DragDrop += MainForm_DragDrop;

        BuildUi();
        UpdateToolTooltip();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        cutCancellation?.Cancel();
        probeCancellation?.Cancel();
        base.OnFormClosing(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        SynchronizeOptionButtonHeights();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(BuildInputPanel(), 0, 0);
        root.Controls.Add(BuildOptionsPanel(), 0, 1);
        root.Controls.Add(BuildSectionsPanel(), 0, 2);
        root.Controls.Add(BuildActionPanel(), 0, 3);
    }

    private Control BuildInputPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 4,
            Padding = new Padding(0, 0, 0, 10)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));

        AddFileRow(
            panel,
            0,
            "VPY / AVS",
            vpyPath,
            SelectVpy,
            IsSupportedScriptPath,
            ApplyVpyPath);
        AddFileRow(
            panel,
            1,
            "输入媒体",
            audioInputPath,
            SelectAudioInput,
            IsSupportedMediaInput,
            ApplyAudioInputPath);
        AddFileRow(
            panel,
            2,
            "输出音频",
            audioOutputPath,
            SelectAudioOutput,
            IsSupportedAudioOutput,
            path =>
            {
                audioOutputPath.Text = path;
                automaticOutputPath = null;
            });
        AddFileRow(
            panel,
            3,
            "Cut 文件",
            cltPath,
            SelectClt,
            path => string.Equals(Path.GetExtension(path), ".clt", StringComparison.OrdinalIgnoreCase),
            path => cltPath.Text = path);

        return panel;
    }

    private Control BuildOptionsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 8,
            RowCount = 2,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
            Padding = new Padding(0, 0, 0, 10)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        panel.Controls.Add(CreateOptionLabel("帧率", 0), 0, 0);

        framerate.DropDownStyle = ComboBoxStyle.DropDown;
        framerate.Items.AddRange(
        [
            "23.976024",
            "24",
            "25",
            "29.97003",
            "30",
            "47.952048",
            "50",
            "59.94006",
            "60"
        ]);
        framerate.Text = settings.LastFramerate ?? string.Empty;
        framerate.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        framerate.Margin = new Padding(0, 0, 12, 0);
        panel.Controls.Add(framerate, 1, 0);
        var optionControlHeight = framerate.PreferredHeight;

        panel.Controls.Add(CreateOptionLabel("过渡方式", 0), 2, 0);
        style.Items.AddRange(["NO_TRANSITION", "FADE", "DISSOLVE"]);
        style.SelectedIndex = 0;
        style.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        style.Margin = new Padding(0, 0, 12, 0);
        panel.Controls.Add(style, 3, 0);

        parseButton.Text = "解析脚本";
        parseButton.Size = new Size(96, optionControlHeight);
        parseButton.Anchor = AnchorStyles.Left;
        parseButton.Margin = Padding.Empty;
        parseButton.Click += (_, _) => ParseVpy(showWarnings: true);
        panel.Controls.Add(parseButton, 4, 0);

        panel.Controls.Add(CreateOptionLabel("音轨", 0), 0, 1);
        audioTrack.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        audioTrack.Margin = new Padding(0, 0, 12, 0);
        audioTrack.SelectedIndexChanged += (_, _) => UpdateOutputForSelectedTrack();
        panel.Controls.Add(audioTrack, 1, 1);
        panel.SetColumnSpan(audioTrack, 5);

        analyzeButton.Text = "分析媒体";
        analyzeButton.Size = new Size(96, optionControlHeight);
        analyzeButton.Anchor = AnchorStyles.Left;
        analyzeButton.Margin = Padding.Empty;
        analyzeButton.Click += async (_, _) => await AnalyzeMediaAsync(promptForFfmpeg: true, showErrors: true);
        panel.Controls.Add(analyzeButton, 6, 1);

        toolsButton.Text = "工具...";
        toolsButton.Size = new Size(80, optionControlHeight);
        toolsButton.Anchor = AnchorStyles.Left;
        toolsButton.Margin = new Padding(8, 0, 0, 0);
        toolsButton.Click += (_, _) => ShowToolsMenu();
        panel.Controls.Add(toolsButton, 7, 1);

        return panel;
    }

    private void SynchronizeOptionButtonHeights()
    {
        parseButton.Height = framerate.Height;
        analyzeButton.Height = audioTrack.Height;
        toolsButton.Height = audioTrack.Height;
    }

    private static Label CreateOptionLabel(string text, int leftMargin)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(leftMargin, 0, 8, 0)
        };
    }

    private Control BuildSectionsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(
            new Label
            {
                Text = "解析出的保留片段",
                AutoSize = true,
                Padding = new Padding(0, 0, 0, 5)
            },
            0,
            0);

        sections.Dock = DockStyle.Fill;
        sections.AllowUserToAddRows = false;
        sections.AllowUserToDeleteRows = false;
        sections.AllowUserToResizeRows = false;
        sections.ReadOnly = true;
        sections.RowHeadersVisible = false;
        sections.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        sections.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        sections.Columns.Add("index", "序号");
        sections.Columns.Add("start", "起始帧");
        sections.Columns.Add("end", "结束帧");
        sections.Columns.Add("line", "脚本行号");
        sections.Columns["index"]!.FillWeight = 22;
        sections.Columns["line"]!.FillWeight = 28;
        panel.Controls.Add(sections, 0, 1);

        return panel;
    }

    private Control BuildActionPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Height = 42,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(0, 8, 0, 0),
            Margin = Padding.Empty
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        status.Text = "等待选择 VPY 或 AVS 脚本。";
        status.AutoEllipsis = true;
        status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.Margin = new Padding(4, 0, 8, 0);
        panel.Controls.Add(status, 0, 0);

        progressBar.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        progressBar.Height = 22;
        progressBar.Style = ProgressBarStyle.Continuous;
        progressBar.Margin = new Padding(0, 6, 12, 6);
        panel.Controls.Add(progressBar, 1, 0);

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        writeButton.Text = "生成 .clt";
        writeButton.Size = new Size(94, 32);
        writeButton.Margin = Padding.Empty;
        writeButton.Click += (_, _) => WriteClt(showCompletion: true);
        buttonPanel.Controls.Add(writeButton);

        cutButton.Text = "一键切音频";
        cutButton.Size = new Size(110, 32);
        cutButton.Margin = new Padding(8, 0, 0, 0);
        cutButton.Click += async (_, _) => await CutAudioAsync();
        buttonPanel.Controls.Add(cutButton);

        cancelButton.Text = "取消";
        cancelButton.Size = new Size(72, 32);
        cancelButton.Enabled = false;
        cancelButton.Margin = new Padding(8, 0, 0, 0);
        cancelButton.Click += (_, _) => cutCancellation?.Cancel();
        buttonPanel.Controls.Add(cancelButton);
        panel.Controls.Add(buttonPanel, 2, 0);

        return panel;
    }

    private void AddFileRow(
        TableLayoutPanel panel,
        int row,
        string labelText,
        TextBox textBox,
        Action select,
        Func<string, bool> acceptsDrop,
        Action<string> applyDrop)
    {
        panel.Controls.Add(
            new Label
            {
                Text = labelText,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 7, 6, 0)
            },
            0,
            row);

        textBox.Dock = DockStyle.Fill;
        textBox.Margin = new Padding(0, 3, 8, 3);
        ConfigureFileDrop(textBox, acceptsDrop, applyDrop);
        panel.Controls.Add(textBox, 1, row);

        var button = new Button
        {
            Text = "浏览...",
            Dock = DockStyle.Fill,
            Height = 28,
            Margin = new Padding(0, 2, 0, 2)
        };
        button.Click += (_, _) => select();
        panel.Controls.Add(button, 2, row);
    }

    private static void ConfigureFileDrop(Control control, Func<string, bool> accepts, Action<string> apply)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, eventArgs) =>
        {
            eventArgs.Effect = TryGetDroppedFile(eventArgs, accepts, out var ignoredPath)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        };
        control.DragDrop += (_, eventArgs) =>
        {
            if (TryGetDroppedFile(eventArgs, accepts, out var path))
                apply(path);
        };
    }

    private static bool TryGetDroppedFile(DragEventArgs eventArgs, Func<string, bool> accepts, out string path)
    {
        path = string.Empty;
        if (eventArgs.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return false;

        var candidate = files.FirstOrDefault(file => File.Exists(file) && accepts(file));
        if (candidate is null)
            return false;

        path = candidate;
        return true;
    }

    private void SelectVpy()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "VapourSynth / AviSynth 脚本 (*.vpy;*.avs)|*.vpy;*.avs|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            ApplyVpyPath(dialog.FileName);
    }

    private void SelectAudioInput()
    {
        using var dialog = new OpenFileDialog { Filter = MediaInputFilter };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            ApplyAudioInputPath(dialog.FileName);
    }

    private void SelectAudioOutput()
    {
        using var dialog = new SaveFileDialog { Filter = AudioOutputFilter };
        var track = SelectedAudioTrack;
        if (!string.IsNullOrWhiteSpace(audioInputPath.Text))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(audioInputPath.Text);
            var defaultPath = BuildDefaultAudioOutput(
                audioInputPath.Text,
                track?.PreferredExtension ?? Path.GetExtension(audioInputPath.Text));
            dialog.FileName = Path.GetFileName(defaultPath);
            dialog.DefaultExt = (track?.PreferredExtension ?? Path.GetExtension(defaultPath)).TrimStart('.');
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            audioOutputPath.Text = dialog.FileName;
            automaticOutputPath = null;
        }
    }

    private void SelectClt()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "MeGUI cut list (*.clt)|*.clt|所有文件 (*.*)|*.*",
            DefaultExt = "clt"
        };
        if (!string.IsNullOrWhiteSpace(vpyPath.Text))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(vpyPath.Text);
            dialog.FileName = Path.GetFileName(Path.ChangeExtension(vpyPath.Text, ".clt"));
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
            cltPath.Text = dialog.FileName;
    }

    private void ShowToolsMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("选择 BeSplit...", null, (_, _) => SelectBeSplit());
        menu.Items.Add("选择 eac3to...", null, (_, _) => SelectEac3to());
        menu.Items.Add("选择 ffmpeg...", null, (_, _) => SelectFfmpeg());
        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(toolsButton, new Point(0, toolsButton.Height));
    }

    private void SelectBeSplit()
    {
        var selected = SelectExecutable(
            "MeGUI BeSplit (besplit.exe)|besplit.exe|可执行文件 (*.exe)|*.exe",
            "选择 MeGUI\\tools\\besplit\\besplit.exe",
            BeSplitLocator.FindAutomatically(settings.BeSplitPath));
        if (selected is null)
            return;

        settings.BeSplitPath = selected;
        SaveCurrentSettings();
        UpdateToolTooltip();
    }

    private void SelectEac3to()
    {
        var selected = SelectExecutable(
            "eac3to (eac3to.exe)|eac3to.exe|可执行文件 (*.exe)|*.exe",
            "选择 eac3to.exe",
            MediaToolLocator.FindEac3to(settings.Eac3toPath, settings.BeSplitPath));
        if (selected is null)
            return;

        settings.Eac3toPath = selected;
        SaveCurrentSettings();
        UpdateToolTooltip();
    }

    private void SelectFfmpeg()
    {
        var selected = SelectExecutable(
            "ffmpeg (ffmpeg.exe)|ffmpeg.exe|可执行文件 (*.exe)|*.exe",
            "选择 ffmpeg.exe",
            MediaToolLocator.FindFfmpeg(settings.FfmpegPath, settings.BeSplitPath));
        if (selected is null)
            return;

        settings.FfmpegPath = selected;
        SaveCurrentSettings();
        UpdateToolTooltip();
    }

    private string? SelectExecutable(string filter, string title, string? automaticPath)
    {
        using var dialog = new OpenFileDialog { Filter = filter, Title = title };
        if (!string.IsNullOrWhiteSpace(automaticPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(automaticPath);
            dialog.FileName = automaticPath;
        }

        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private void ApplyVpyPath(string path)
    {
        vpyPath.Text = path;
        cltPath.Text = Path.ChangeExtension(path, ".clt");
        ParseVpy(showWarnings: true);
    }

    private void ApplyAudioInputPath(string path)
    {
        probeCancellation?.Cancel();
        audioInputPath.Text = path;
        probedInputPath = null;
        audioTrack.Items.Clear();

        var directTrack = MediaCodecPolicy.CreateDirectTrack(path);
        if (directTrack is not null)
        {
            ApplyAudioTracks(path, [directTrack]);
            return;
        }

        if (automaticOutputPath is not null &&
            string.Equals(audioOutputPath.Text, automaticOutputPath, StringComparison.OrdinalIgnoreCase))
        {
            audioOutputPath.Clear();
        }

        status.Text = "正在分析媒体音轨...";
        _ = AnalyzeMediaAsync(promptForFfmpeg: false, showErrors: false);
    }

    private static string BuildDefaultAudioOutput(string inputPath, string extension)
    {
        return Path.Combine(
            Path.GetDirectoryName(inputPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(inputPath) + "_cut" + extension);
    }

    private async Task<bool> AnalyzeMediaAsync(bool promptForFfmpeg, bool showErrors)
    {
        var input = audioInputPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
        {
            if (showErrors)
                MessageBox.Show(this, "请先选择有效的输入媒体。", "输入无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var directTrack = MediaCodecPolicy.CreateDirectTrack(input);
        if (directTrack is not null)
        {
            ApplyAudioTracks(input, [directTrack]);
            return true;
        }

        var ffmpegPath = promptForFfmpeg
            ? EnsureFfmpegPath()
            : MediaToolLocator.FindFfmpeg(settings.FfmpegPath, settings.BeSplitPath);
        if (ffmpegPath is null)
        {
            status.Text = "需要 ffmpeg 分析媒体音轨；点击“分析媒体”后选择 ffmpeg.exe。";
            return false;
        }

        settings.FfmpegPath = ffmpegPath;
        var eac3toPath = MediaToolLocator.FindEac3to(settings.Eac3toPath, settings.BeSplitPath);
        if (eac3toPath is not null)
            settings.Eac3toPath = eac3toPath;
        SaveCurrentSettings();
        UpdateToolTooltip();

        probeCancellation?.Cancel();
        probeCancellation?.Dispose();
        probeCancellation = new CancellationTokenSource();
        var token = probeCancellation.Token;

        try
        {
            analyzeButton.Enabled = false;
            status.Text = "ffmpeg 正在分析媒体音轨...";
            var tracks = await MediaAudioProbe.ProbeAsync(ffmpegPath, eac3toPath, input, token);
            if (!string.Equals(input, audioInputPath.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;

            ApplyAudioTracks(input, tracks);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            status.Text = "媒体音轨分析失败。";
            if (showErrors)
            {
                MessageBox.Show(
                    this,
                    $"媒体音轨分析失败：{ex.Message}",
                    "分析失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }

            return false;
        }
        finally
        {
            analyzeButton.Enabled = cutCancellation is null;
        }
    }

    private void ApplyAudioTracks(string input, IReadOnlyList<AudioTrackInfo> tracks)
    {
        audioTrack.BeginUpdate();
        try
        {
            audioTrack.Items.Clear();
            foreach (var track in tracks)
                audioTrack.Items.Add(track);
            if (audioTrack.Items.Count > 0)
                audioTrack.SelectedIndex = 0;
        }
        finally
        {
            audioTrack.EndUpdate();
        }

        probedInputPath = input;
        status.Text = tracks.Count == 1
            ? $"已识别音轨：{tracks[0].Description}"
            : $"已识别 {tracks.Count} 条音轨，请确认要处理的音轨。";
        UpdateOutputForSelectedTrack();
    }

    private AudioTrackInfo? SelectedAudioTrack => audioTrack.SelectedItem as AudioTrackInfo;

    private void UpdateOutputForSelectedTrack()
    {
        var track = SelectedAudioTrack;
        if (track is null || string.IsNullOrWhiteSpace(audioInputPath.Text))
            return;

        var newDefault = BuildDefaultAudioOutput(audioInputPath.Text, track.PreferredExtension);
        if (string.IsNullOrWhiteSpace(audioOutputPath.Text) ||
            automaticOutputPath is not null &&
            string.Equals(audioOutputPath.Text, automaticOutputPath, StringComparison.OrdinalIgnoreCase))
        {
            audioOutputPath.Text = newDefault;
            automaticOutputPath = newDefault;
        }
    }

    private VpyParseResult? ParseVpy(bool showWarnings)
    {
        if (string.IsNullOrWhiteSpace(vpyPath.Text) || !File.Exists(vpyPath.Text))
        {
            if (showWarnings)
                MessageBox.Show(this, "请先选择有效的 VPY 或 AVS 脚本。", "缺少输入", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        try
        {
            var parsed = VpyTrimParser.Parse(File.ReadAllText(vpyPath.Text), GetScriptSyntax(vpyPath.Text));
            var normalized = BeSplitAudioCutter.NormalizeSections(parsed.Sections);
            parsed.Sections.Clear();
            parsed.Sections.AddRange(normalized);
            UpdateSectionsGrid(parsed.Sections);

            status.Text = parsed.Sections.Count == 0
                ? "没有找到可用的 Trim 区间。"
                : $"已找到 {parsed.Sections.Count} 个片段。" +
                  (parsed.Warnings.Count > 0 ? $" {parsed.Warnings.Count} 个警告。" : string.Empty);

            if (showWarnings && parsed.Warnings.Count > 0)
            {
                MessageBox.Show(
                    this,
                    string.Join(Environment.NewLine, parsed.Warnings),
                    "解析警告",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            return parsed;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"读取脚本失败：{ex.Message}", "读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
    }

    private void UpdateSectionsGrid(IReadOnlyList<TrimSection> parsedSections)
    {
        sections.Rows.Clear();
        for (var i = 0; i < parsedSections.Count; i++)
        {
            var section = parsedSections[i];
            sections.Rows.Add(i + 1, section.StartFrame, section.EndFrame, section.SourceLine);
        }
    }

    private CutPreparation? PrepareCuts()
    {
        var parsed = ParseVpy(showWarnings: false);
        if (parsed is null || parsed.Sections.Count == 0)
        {
            MessageBox.Show(this, "没有找到可处理的 Trim 区间。", "没有片段", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        if (parsed.Warnings.Count > 0)
        {
            var decision = MessageBox.Show(
                this,
                string.Join(Environment.NewLine, parsed.Warnings) + Environment.NewLine + Environment.NewLine + "是否继续？",
                "解析警告",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (decision != DialogResult.Yes)
                return null;
        }

        if (!double.TryParse(
                framerate.Text.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var fps) ||
            fps <= 0)
        {
            MessageBox.Show(
                this,
                "请从下拉框选择或输入有效帧率，例如 23.976024 或 29.97003。",
                "帧率无效",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return null;
        }

        if (string.IsNullOrWhiteSpace(cltPath.Text))
            cltPath.Text = Path.ChangeExtension(vpyPath.Text, ".clt");

        return new CutPreparation(fps, parsed.Sections);
    }

    private bool ValidateInputPath()
    {
        if (string.IsNullOrWhiteSpace(audioInputPath.Text) || !File.Exists(audioInputPath.Text))
        {
            MessageBox.Show(this, "请选择有效的输入媒体。", "输入媒体无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return true;
    }

    private bool ValidateOutputPath(AudioTrackInfo track)
    {
        if (string.IsNullOrWhiteSpace(audioOutputPath.Text))
        {
            audioOutputPath.Text = BuildDefaultAudioOutput(audioInputPath.Text, track.PreferredExtension);
            automaticOutputPath = audioOutputPath.Text;
        }

        if (!string.Equals(
                Path.GetExtension(audioOutputPath.Text),
                track.PreferredExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                this,
                $"所选音轨将无转码输出为 {track.PreferredExtension}，输出文件扩展名必须一致。",
                "输出格式无效",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        if (string.Equals(
                Path.GetFullPath(audioInputPath.Text),
                Path.GetFullPath(audioOutputPath.Text),
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "输出音频不能覆盖输入媒体。", "输出路径无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var outputDirectory = Path.GetDirectoryName(audioOutputPath.Text);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            MessageBox.Show(this, "输出音频路径无效。", "输出路径无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        Directory.CreateDirectory(outputDirectory);
        return true;
    }

    private bool WriteClt(bool showCompletion)
    {
        var preparation = PrepareCuts();
        if (preparation is null)
            return false;

        try
        {
            CltWriter.Write(cltPath.Text, preparation.Framerate, style.Text, preparation.Sections);
            SaveCurrentSettings();
            status.Text = $"已生成：{cltPath.Text}";

            if (showCompletion)
                MessageBox.Show(this, "CLT 已生成。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);

            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"写入 CLT 失败：{ex.Message}", "写入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private async Task CutAudioAsync()
    {
        var preparation = PrepareCuts();
        if (preparation is null || !ValidateInputPath())
            return;

        if (!string.Equals(probedInputPath, audioInputPath.Text.Trim(), StringComparison.OrdinalIgnoreCase) ||
            SelectedAudioTrack is null)
        {
            if (!await AnalyzeMediaAsync(promptForFfmpeg: true, showErrors: true))
                return;
        }

        var track = SelectedAudioTrack;
        if (track is null || !ValidateOutputPath(track))
            return;

        var directBeSplitInput =
            track.UseBeSplit &&
            track.AudioIndex == 0 &&
            BeSplitAudioCutter.IsSupportedAudioPath(audioInputPath.Text) &&
            string.Equals(
                Path.GetExtension(audioInputPath.Text),
                track.PreferredExtension,
                StringComparison.OrdinalIgnoreCase);

        string? ffmpegPath = null;
        if (!directBeSplitInput)
        {
            ffmpegPath = EnsureFfmpegPath();
            if (ffmpegPath is null)
                return;
        }

        string? beSplitPath = null;
        if (track.UseBeSplit)
        {
            beSplitPath = EnsureBeSplitPath();
            if (beSplitPath is null)
                return;
        }
        else
        {
            var continueWithAdaptiveCut = MessageBox.Show(
                this,
                "该音轨不受 BeSplit 支持，将由 ffmpeg 自适应切割。" +
                Environment.NewLine +
                "切点全部对齐音频包边界时无转码直通；存在偏差时会精确裁切并编码回原格式。" +
                Environment.NewLine +
                Environment.NewLine +
                "是否继续？",
                "ffmpeg 自适应切割",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (continueWithAdaptiveCut != DialogResult.Yes)
                return;
        }

        if (File.Exists(audioOutputPath.Text))
        {
            var overwrite = MessageBox.Show(
                this,
                "输出音频已经存在，是否覆盖？",
                "确认覆盖",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (overwrite != DialogResult.Yes)
                return;
        }

        try
        {
            CltWriter.Write(cltPath.Text, preparation.Framerate, style.Text, preparation.Sections);
            var eac3toPath = MediaToolLocator.FindEac3to(settings.Eac3toPath, settings.BeSplitPath);
            if (eac3toPath is not null)
                settings.Eac3toPath = eac3toPath;
            SaveCurrentSettings();

            cutCancellation = new CancellationTokenSource();
            SetBusy(true);
            var progress = new Progress<string>(message => status.Text = message);
            var result = await AudioCutWorkflow.CutAsync(
                audioInputPath.Text,
                audioOutputPath.Text,
                track,
                preparation.Framerate,
                preparation.Sections,
                beSplitPath,
                ffmpegPath,
                eac3toPath,
                progress,
                cutCancellation.Token);

            status.Text = result.Reencoded
                ? $"切割完成（切点未对齐，已精确裁切并编码回原格式）：{result.OutputPath}"
                : result.PacketBoundaryCut
                    ? $"切割完成（切点已对齐，ffmpeg 无转码直通）：{result.OutputPath}"
                    : $"切割完成：{result.OutputPath}";
            var openFolder = MessageBox.Show(
                this,
                "音频切割完成。是否打开输出文件所在文件夹？",
                "完成",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (openFolder == DialogResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{audioOutputPath.Text}\"",
                    UseShellExecute = true
                });
            }
        }
        catch (OperationCanceledException)
        {
            status.Text = "操作已取消。";
        }
        catch (Exception ex)
        {
            status.Text = "切割失败。";
            MessageBox.Show(this, $"音频切割失败：{ex.Message}", "切割失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            cutCancellation?.Dispose();
            cutCancellation = null;
            SetBusy(false);
        }
    }

    private string? EnsureBeSplitPath()
    {
        var path = BeSplitLocator.FindAutomatically(settings.BeSplitPath);
        if (!string.IsNullOrWhiteSpace(path))
        {
            settings.BeSplitPath = path;
            SaveCurrentSettings();
            UpdateToolTooltip();
            return path;
        }

        MessageBox.Show(
            this,
            "没有自动找到 MeGUI 的 besplit.exe。请选择 MeGUI\\tools\\besplit\\besplit.exe；路径会被记住。",
            "需要 BeSplit",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        SelectBeSplit();
        return BeSplitLocator.FindAutomatically(settings.BeSplitPath);
    }

    private string? EnsureFfmpegPath()
    {
        var path = MediaToolLocator.FindFfmpeg(settings.FfmpegPath, settings.BeSplitPath);
        if (!string.IsNullOrWhiteSpace(path))
        {
            settings.FfmpegPath = path;
            SaveCurrentSettings();
            UpdateToolTooltip();
            return path;
        }

        MessageBox.Show(
            this,
            "没有自动找到 ffmpeg.exe。程序已检查保存路径、程序同目录、MeGUI tools 目录和 PATH；请手动选择一次。",
            "需要 ffmpeg",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        SelectFfmpeg();
        return MediaToolLocator.FindFfmpeg(settings.FfmpegPath, settings.BeSplitPath);
    }

    private void SaveCurrentSettings()
    {
        settings.LastFramerate = framerate.Text.Trim();
        AppSettingsStore.Save(settings);
    }

    private void UpdateToolTooltip()
    {
        var beSplit = BeSplitLocator.FindAutomatically(settings.BeSplitPath) ?? "未找到";
        var eac3to = MediaToolLocator.FindEac3to(settings.Eac3toPath, settings.BeSplitPath) ?? "未找到（可选）";
        var ffmpeg = MediaToolLocator.FindFfmpeg(settings.FfmpegPath, settings.BeSplitPath) ?? "未找到";
        toolTip.SetToolTip(
            toolsButton,
            $"BeSplit: {beSplit}{Environment.NewLine}eac3to: {eac3to}{Environment.NewLine}ffmpeg: {ffmpeg}");
    }

    private void SetBusy(bool busy)
    {
        vpyPath.Enabled = !busy;
        audioInputPath.Enabled = !busy;
        audioOutputPath.Enabled = !busy;
        cltPath.Enabled = !busy;
        framerate.Enabled = !busy;
        style.Enabled = !busy;
        audioTrack.Enabled = !busy;
        analyzeButton.Enabled = !busy;
        toolsButton.Enabled = !busy;
        writeButton.Enabled = !busy;
        cutButton.Enabled = !busy;
        cancelButton.Enabled = busy;
        progressBar.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        if (!busy)
            progressBar.Value = 0;
    }

    private void MainForm_DragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = TryGetDroppedFile(e, IsSupportedScriptPath, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void MainForm_DragDrop(object? sender, DragEventArgs e)
    {
        if (TryGetDroppedFile(e, IsSupportedScriptPath, out var path))
            ApplyVpyPath(path);
    }

    private sealed record CutPreparation(double Framerate, IReadOnlyList<TrimSection> Sections);

    private static bool IsSupportedScriptPath(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".vpy", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".avs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedMediaInput(string path)
    {
        return MediaInputExtensions.Contains(Path.GetExtension(path)) ||
               !string.IsNullOrWhiteSpace(Path.GetExtension(path));
    }

    private static bool IsSupportedAudioOutput(string path)
    {
        return AudioOutputExtensions.Contains(Path.GetExtension(path));
    }

    private static ScriptSyntax GetScriptSyntax(string path)
    {
        return string.Equals(Path.GetExtension(path), ".avs", StringComparison.OrdinalIgnoreCase)
            ? ScriptSyntax.AviSynth
            : ScriptSyntax.VapourSynth;
    }
}
