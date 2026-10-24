namespace BmsTrainer;

/// <summary>入り切らないパスは途中を省略して、末尾（ファイル名・フォルダ名）を見せるラベル</summary>
public sealed class PathLabel : Label
{
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis | TextFormatFlags.SingleLine);
    }
}
