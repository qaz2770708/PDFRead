using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal sealed partial class PdfReaderForm
{
    private bool sessionResourcesDisposed;
    private void ToggleNavigation(int tab)
    {
        if (!navigationSplit.Panel2Collapsed && navigationTabs.SelectedIndex == tab) SetNavigationVisible(false);
        else ShowNavigation(tab);
    }
    private void ShowNavigation(int tab)
    {
        navigationTabs.SelectedIndex = tab;
        SetNavigationVisible(true);
    }
    private void SetNavigationVisible(bool visible)
    {
        navigationSplit.Panel2Collapsed = !visible;
        UpdateNavigationButtons();
        PositionReadingTools();
    }
    private void UpdateNavigationButtons()
    {
        bool shown = !navigationSplit.Panel2Collapsed;
        directoryButton.Checked = shown && navigationTabs.SelectedIndex == 0;
        bookmarksButton.Checked = shown && navigationTabs.SelectedIndex == 1;
        thumbnailsButton.Checked = shown && navigationTabs.SelectedIndex == 2;
    }
    private void DrawOutlineThumbnail(object sender, DrawTreeNodeEventArgs e)
    {
        var target = e.Node.Tag as NavigationTarget;
        if (target == null) { e.DrawDefault = true; return; }
        int x = e.Node.Level * 16 + 16;
        Rectangle row = new Rectangle(0, e.Bounds.Y, Math.Max(1, outlineTree.ClientSize.Width), outlineTree.ItemHeight);
        using (var brush = new SolidBrush(e.Node.IsSelected ? Color.FromArgb(222, 234, 252) : Color.White)) e.Graphics.FillRectangle(brush, row);
        if (e.Node.Nodes.Count > 0)
        {
            int left = x - 12, top = row.Top + 18;
            Point[] triangle = e.Node.IsExpanded ? new[] { new Point(left, top), new Point(left + 8, top), new Point(left + 4, top + 6) }
                : new[] { new Point(left, top), new Point(left, top + 8), new Point(left + 6, top + 4) };
            e.Graphics.FillPolygon(Brushes.SlateGray, triangle);
        }
        Rectangle card = new Rectangle(x + 4, row.Top + 5, 76, 100);
        e.Graphics.FillRectangle(Brushes.WhiteSmoke, card);
        Bitmap image = navigationSession?.Thumbnails?.Peek(target.PageIndex);
        if (image != null) PageThumbnailView.DrawContained(e.Graphics, image, Rectangle.Inflate(card, -2, -2));
        else TextRenderer.DrawText(e.Graphics, target.PageIndex < 0 ? "无预览" : navigationSession.Thumbnails.Failed(target.PageIndex) ? "预览失败" : "加载中", outlineTree.Font, card, Color.SlateGray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        e.Graphics.DrawRectangle(Pens.LightGray, card);
        string text = target.Title + "\r\n" + (target.PageIndex < 0 ? "无有效页目标" : "第 " + (target.PageIndex + 1) + " 页");
        TextRenderer.DrawText(e.Graphics, text, outlineTree.Font,
            new Rectangle(x + 88, row.Top + 12, Math.Max(30, row.Width - x - 94), row.Height - 20),
            target.PageIndex < 0 ? Color.Gray : Color.FromArgb(40, 52, 69), TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
    }
    private void RenderNavigationThumbnail()
    {
        if (movingSession || navigationSplit.Panel2Collapsed || !Visible || WindowState == FormWindowState.Minimized) return;
        var cache = navigationSession?.Thumbnails;
        if (cache == null) return;
        if (navigationTabs.SelectedIndex == 0)
        {
            TreeNode node = outlineTree.TopNode;
            while (node != null && node.Bounds.Top < outlineTree.ClientSize.Height)
            {
                var target = node.Tag as NavigationTarget;
                if (target != null && cache.NeedsRender(target.PageIndex))
                { cache.Render(target.PageIndex); outlineTree.Invalidate(); return; }
                node = node.NextVisibleNode;
            }
        }
        else if (navigationTabs.SelectedIndex == 2)
        {
            foreach (int page in thumbnailView.VisiblePages()) if (cache.NeedsRender(page))
            { cache.Render(page); thumbnailView.Invalidate(); return; }
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            thumbnailTimer.Dispose();
            rightTools?.Dispose();
            DisposeSessions();
        }
        base.Dispose(disposing);
    }
}
