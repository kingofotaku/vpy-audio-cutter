namespace VpyAudioCutter;

internal sealed class UnifiedComboBox : ComboBox
{
    public UnifiedComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDown;
    }

    public bool IsTextReadOnly { get; init; }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (IsTextReadOnly && !char.IsControl(e.KeyChar))
            e.Handled = true;

        base.OnKeyPress(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (IsTextReadOnly &&
            (e.KeyCode is Keys.Back or Keys.Delete ||
             e.Control && e.KeyCode is Keys.V or Keys.X or Keys.Z ||
             e.Shift && e.KeyCode is Keys.Insert or Keys.Delete))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnTextUpdate(EventArgs e)
    {
        if (IsTextReadOnly)
        {
            var selectedText = SelectedIndex >= 0
                ? GetItemText(SelectedItem)
                : string.Empty;
            if (!string.Equals(Text, selectedText, StringComparison.Ordinal))
            {
                Text = selectedText;
                SelectionStart = 0;
                SelectionLength = 0;
            }
        }

        base.OnTextUpdate(e);
    }
}
