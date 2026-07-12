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

        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(Path.Combine(AppContext.BaseDirectory, "ui-smoke.png"));
        form.Close();
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
