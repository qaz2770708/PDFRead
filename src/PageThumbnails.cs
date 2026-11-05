using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal sealed class OutlinePreviewTree : TreeView
{
    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.Style |= 0x8000; return parameters; } // TVS_NOHSCROLL: preview titles wrap within the sidebar.
    }
}

internal sealed class DocumentThumbnails : IDisposable
{
    private readonly object document;
    private readonly int count;
    private readonly Dictionary<int, Bitmap> cache = new Dictionary<int, Bitmap>();
    private readonly LinkedList<int> recent = new LinkedList<int>();
    private readonly HashSet<int> failed = new HashSet<int>();
    private bool disposed;
    public DocumentThumbnails(object doc, int pageCount) { document = doc; count = pageCount; }
    public int CachedCount { get { return cache.Count; } }
    public Bitmap Peek(int page)
    {
        Bitmap image;
        if (!cache.TryGetValue(page, out image)) return null;
        recent.Remove(page); recent.AddLast(page);
        return image;
    }
    public bool NeedsRender(int page) { return !disposed && page >= 0 && page < count && !cache.ContainsKey(page) && !failed.Contains(page); }
    public bool Failed(int page) { return failed.Contains(page); }
    public void Render(int page)
    {
        if (!NeedsRender(page)) return;
        try
        {
            SizeF size = WindowsPdfRenderer.GetPageSize(document, page);
            Bitmap image = WindowsPdfRenderer.Render(document, page, Math.Min(144f / size.Width, 180f / size.Height));
            cache.Add(page, image); recent.AddLast(page);
            while (cache.Count > 40) { int old = recent.First.Value; recent.RemoveFirst(); cache[old].Dispose(); cache.Remove(old); }
        }
        catch (Exception ex) { failed.Add(page); System.Diagnostics.Debug.WriteLine("Thumbnail render failed: " + ex.Message); }
    }
    public void Dispose() { disposed = true; foreach (var image in cache.Values) image.Dispose(); cache.Clear(); recent.Clear(); }
}

internal sealed class PageThumbnailView : ScrollableControl
{
    private DocumentThumbnails thumbnails;
    private int pageCount;
    private int current;
    private const int CellHeight = 216;
    public event EventHandler<int> PageSelected;
    public PageThumbnailView() { Dock = DockStyle.Fill; AutoScroll = true; DoubleBuffered = true; BackColor = Color.FromArgb(240, 243, 247); Cursor = Cursors.Hand; }
    public void Configure(DocumentThumbnails source, int count, int selected)
    {
        thumbnails = source; pageCount = count; current = selected;
        AutoScrollMinSize = new Size(0, count * CellHeight + 12); AutoScrollPosition = Point.Empty;
        SelectPage(selected, true); Invalidate();
    }
    public void SelectPage(int page, bool ensureVisible)
    {
        current = page;
        if (ensureVisible && pageCount > 0)
        {
            int top = -AutoScrollPosition.Y;
            if (page * CellHeight < top || (page + 1) * CellHeight > top + ClientSize.Height)
                AutoScrollPosition = new Point(0, page * CellHeight);
        }
        Invalidate();
    }
    public IEnumerable<int> VisiblePages()
    {
        int first = Math.Max(0, -AutoScrollPosition.Y / CellHeight);
        for (int i = first; i < pageCount && i * CellHeight < -AutoScrollPosition.Y + ClientSize.Height; i++) yield return i;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        foreach (int page in VisiblePages())
        {
            int y = page * CellHeight + AutoScrollPosition.Y + 10;
            Rectangle card = new Rectangle(Math.Max(8, (ClientSize.Width - 158) / 2), y, 158, 202);
            using (var brush = new SolidBrush(page == current ? Color.FromArgb(212, 229, 255) : Color.White)) e.Graphics.FillRectangle(brush, card);
            using (var pen = new Pen(page == current ? Color.RoyalBlue : Color.FromArgb(214, 220, 229), page == current ? 2 : 1)) e.Graphics.DrawRectangle(pen, card);
            Bitmap image = thumbnails?.Peek(page);
            if (image != null) DrawContained(e.Graphics, image, new Rectangle(card.X + 7, y + 6, 144, 170));
            else TextRenderer.DrawText(e.Graphics, thumbnails != null && thumbnails.Failed(page) ? "预览失败" : "正在加载…", Font, new Rectangle(card.X, y + 65, card.Width, 30), Color.SlateGray, TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(e.Graphics, "第 " + (page + 1) + " 页", Font, new Rectangle(card.X, y + 177, card.Width, 24), Color.FromArgb(45, 57, 73), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
    internal static void DrawContained(Graphics graphics, Bitmap image, Rectangle bounds)
    {
        float scale = Math.Min((float)bounds.Width / image.Width, (float)bounds.Height / image.Height);
        int w = (int)(image.Width * scale), h = (int)(image.Height * scale);
        graphics.DrawImage(image, bounds.X + (bounds.Width - w) / 2, bounds.Y + (bounds.Height - h) / 2, w, h);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int index = (e.Y - AutoScrollPosition.Y) / CellHeight;
        if (e.Button == MouseButtons.Left && index >= 0 && index < pageCount) PageSelected?.Invoke(this, index);
    }
}
