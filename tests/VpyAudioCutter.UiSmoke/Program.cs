using System.Drawing;
using VpyAudioCutter;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var form = new MainForm
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-2000, -2000)
        };
        form.Show();
        Application.DoEvents();

        var controls = EnumerateControls(form).ToList();
        var pathBoxes = controls.OfType<TextBox>().ToList();
        if (pathBoxes.Count != 4 || pathBoxes.Any(textBox => !textBox.AllowDrop))
            throw new InvalidOperationException("All four path fields must accept file drops.");

        if (controls.OfType<Button>().Count(button => button.Text == "浏览...") != 4)
            throw new InvalidOperationException("All four path rows must have a visible browse button.");

        var fpsSelector = controls
            .OfType<ComboBox>()
            .FirstOrDefault(comboBox => comboBox.Items.Contains("23.976024"));
        if (fpsSelector is null || !fpsSelector.Items.Contains("29.97003") || fpsSelector.DropDownStyle != ComboBoxStyle.DropDown)
            throw new InvalidOperationException("The editable framerate preset list is missing.");

        var transitionSelector = controls
            .OfType<ComboBox>()
            .Single(comboBox => comboBox.Items.Contains("NO_TRANSITION"));
        if (transitionSelector.DropDownStyle != ComboBoxStyle.DropDown ||
            transitionSelector.GetType().Name != "UnifiedComboBox" ||
            fpsSelector.GetType() != transitionSelector.GetType())
        {
            throw new InvalidOperationException("The framerate and transition selectors must use the same control implementation.");
        }
        AssertExactGeometry(
            "The framerate and transition selector borders must align exactly.",
            fpsSelector,
            transitionSelector);

        var parseButton = controls.OfType<Button>().Single(button => button.Text == "解析脚本");
        AssertRowAlignment(
            "The framerate, transition, and parse controls must share one top edge and height.",
            fpsSelector,
            transitionSelector,
            parseButton);

        var framerateLabel = controls.OfType<Label>().Single(label => label.Text == "帧率");
        var transitionLabel = controls.OfType<Label>().Single(label => label.Text == "过渡方式");
        AssertExactGeometry(
            "The framerate and transition labels must align exactly.",
            framerateLabel,
            transitionLabel);

        var audioTrackSelector = controls
            .OfType<ComboBox>()
            .Single(comboBox => comboBox.GetType().Name == "UnifiedComboBox" && comboBox.Items.Count == 0);
        if (audioTrackSelector.DropDownStyle != ComboBoxStyle.DropDown)
            throw new InvalidOperationException("The audio track selector must use the normalized native appearance.");

        var analyzeButton = controls.OfType<Button>().Single(button => button.Text == "分析媒体");
        var toolsButton = controls.OfType<Button>().Single(button => button.Text == "工具...");
        AssertRowAlignment(
            "The audio track controls must share one top edge and height.",
            audioTrackSelector,
            analyzeButton,
            toolsButton);

        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(Path.Combine(AppContext.BaseDirectory, "ui-smoke.png"));
        form.Close();
    }

    private static void AssertRowAlignment(string message, params Control[] controls)
    {
        var expectedTop = controls[0].Top;
        var expectedHeight = controls[0].Height;
        if (controls.Any(control =>
                Math.Abs(control.Top - expectedTop) > 1 ||
                Math.Abs(control.Height - expectedHeight) > 1))
        {
            var geometry = string.Join(
                ", ",
                controls.Select(control => $"{control.Text}: top={control.Top}, height={control.Height}"));
            throw new InvalidOperationException($"{message} Actual geometry: {geometry}");
        }
    }

    private static void AssertExactGeometry(string message, params Control[] controls)
    {
        var expectedTop = controls[0].Top;
        var expectedHeight = controls[0].Height;
        if (controls.Any(control =>
                control.Top != expectedTop ||
                control.Height != expectedHeight))
        {
            var geometry = string.Join(
                ", ",
                controls.Select(control => $"{control.Text}: top={control.Top}, height={control.Height}"));
            throw new InvalidOperationException($"{message} Actual geometry: {geometry}");
        }
    }

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in EnumerateControls(child))
                yield return descendant;
        }
    }
}
