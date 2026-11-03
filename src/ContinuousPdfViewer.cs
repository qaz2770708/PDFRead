using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

internal sealed class HighlightCreatedEventArgs : EventArgs
{
    public int PageIndex { get; private set; }
    public RectangleF NormalizedBounds { get; private set; }

    public HighlightCreatedEventArgs(int pageIndex, RectangleF normalizedBounds)
    {
        PageIndex = pageIndex;
        NormalizedBounds = normalizedBounds;
    }
}

internal sealed class ReadingPositionEventArgs : EventArgs
{
    public int PageIndex { get; private set; }
    public double PageOffset { get; private set; }

    public ReadingPositionEventArgs(int pageIndex, double pageOffset)
    {
        PageIndex = pageIndex;
        PageOffset = pageOffset;
    }
}

internal sealed class ContinuousPdfViewer : ScrollableControl
{
    private const int OuterMargin = 30;
    private const int PageGap = 34;
    private readonly List<SizeF> pageSizes = new List<SizeF>();
    private readonly List<Rectangle> pageBounds = new List<Rectangle>();
    private readonly Dictionary<int, Bitmap> renderedPages = new Dictionary<int, Bitmap>();
    private readonly Timer renderTimer = new Timer { Interval = 120 };
    private object pdfDocument;
    private List<AreaHighlight> annotations = new List<AreaHighlight>();
    private float zoom = 0.82f;
    private int currentPage = -1;
    private int contentWidth;
    private bool layoutInProgress;
    private bool highlightMode;
    private bool active;
    private bool dragging;
    private int dragPage = -1;
    private Point dragStart;
    private Point dragEnd;

    public event EventHandler<HighlightCreatedEventArgs> HighlightCreated;
    public event EventHandler<ReadingPositionEventArgs> ReadingPositionChanged;
    public event EventHandler<int> ActivePageChanged;

    public int PageCount { get { return pageBounds.Count; } }
    public int CurrentPage { get { return Math.Max(0, currentPage); } }
    public float Zoom { get { return zoom; } }
    public bool HighlightMode { get { return highlightMode; } }
    public Size ContentSize { get { return AutoScrollMinSize; } }
    public IList<Rectangle> PageRectangles { get { return pageBounds.AsReadOnly(); } }

    public ContinuousPdfViewer()
    {
        AutoScroll = true;
        BackColor = Color.FromArgb(233, 237, 242);
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        renderTimer.Tick += delegate { renderTimer.Stop(); RenderPagesNearViewport(); };
    }

    public void Configure(object document, int count, float initialZoom, List<AreaHighlight> marks)
    {
        pdfDocument = document;
        zoom = ClampZoom(initialZoom);
        annotations = marks ?? new List<AreaHighlight>();
        pageSizes.Clear();
        pageBounds.Clear();
        for (int i = 0; i < count; i++)
            pageSizes.Add(WindowsPdfRenderer.GetPageSize(pdfDocument, i));
        RebuildLayout(0, 0, false);
        QueueVisibleRender();
    }

    public void SetAnnotations(List<AreaHighlight> marks)
    {
        annotations = marks ?? new List<AreaHighlight>();
        Invalidate();
    }

    public void SetHighlightMode(bool enabled)
    {
        highlightMode = enabled;
        Cursor = enabled ? Cursors.Cross : Cursors.Default;
    }

    public void SetActive(bool value)
    {
        if (active == value) return;
        active = value;
        if (active) QueueVisibleRender();
        else
        {
            renderTimer.Stop();
            DisposeRenderedPages();
        }
    }

    public void SetZoom(float value)
    {
        if (pageBounds.Count == 0) return;
        int page;
        double offset;
        CaptureReadingPosition(out page, out offset);
        zoom = ClampZoom(value);
        DisposeRenderedPages();
        RebuildLayout(page, offset, true);
        QueueVisibleRender();
    }

    public void GoToPage(int pageIndex, double offset)
    {
        if (pageBounds.Count == 0) return;
        pageIndex = Math.Max(0, Math.Min(pageBounds.Count - 1, pageIndex));
        Rectangle page = pageBounds[pageIndex];
        int y = page.Top + (int)Math.Round(page.Height * Math.Max(0, Math.Min(1, offset)));
        int x = Math.Max(0, (contentWidth - ClientSize.Width) / 2);
        AutoScrollPosition = new Point(x, Math.Max(0, y));
        NotifyReadingPosition();
        QueueVisibleRender();
    }

    public void CaptureReadingPosition(out int pageIndex, out double pageOffset)
    {
        if (pageBounds.Count == 0)
        {
            pageIndex = 0;
            pageOffset = 0;
            return;
        }
        int scrollTop = Math.Max(0, -AutoScrollPosition.Y);
        pageIndex = 0;
        for (int i = 0; i < pageBounds.Count; i++)
        {
            Rectangle page = pageBounds[i];
            if (page.Bottom > scrollTop || i == pageBounds.Count - 1)
            {
                pageIndex = i;
                pageOffset = page.Height <= 0 ? 0 : Math.Max(0, Math.Min(1, (double)(scrollTop - page.Top) / page.Height));
                return;
            }
        }
        pageOffset = 0;
    }

    protected override void OnResize(EventArgs e)
    {
        int page;
        double offset;
        if (!layoutInProgress && pageBounds.Count > 0) CaptureReadingPosition(out page, out offset);
        else { page = 0; offset = 0; }
        base.OnResize(e);
        if (!layoutInProgress && pageSizes.Count > 0) RebuildLayout(page, offset, true);
        QueueVisibleRender();
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        QueueVisibleRender();
        NotifyReadingPosition();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        QueueVisibleRender();
        NotifyReadingPosition();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (pageBounds.Count == 0) return;
        GraphicsState saved = e.Graphics.Save();
        Point scroll = AutoScrollPosition;
        e.Graphics.TranslateTransform(scroll.X, scroll.Y);
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        Rectangle viewport = new Rectangle(-scroll.X, -scroll.Y, ClientSize.Width, ClientSize.Height);
        for (int i = 0; i < pageBounds.Count; i++)
        {
            Rectangle page = pageBounds[i];
            if (!page.IntersectsWith(viewport)) continue;
            Rectangle shadow = new Rectangle(page.X + 3, page.Y + 4, page.Width, page.Height);
            using (Brush shadowBrush = new SolidBrush(Color.FromArgb(38, 35, 45, 55)))
                e.Graphics.FillRectangle(shadowBrush, shadow);
            e.Graphics.FillRectangle(Brushes.White, page);
            Bitmap image;
            if (renderedPages.TryGetValue(i, out image))
                e.Graphics.DrawImage(image, page);
            else
            {
                using (Pen border = new Pen(Color.FromArgb(218, 222, 228))) e.Graphics.DrawRectangle(border, page);
                using (Brush text = new SolidBrush(Color.FromArgb(139, 148, 158)))
                using (Font font = new Font("Segoe UI", 10))
                {
                    string label = "第 " + (i + 1) + " 页";
                    SizeF labelSize = e.Graphics.MeasureString(label, font);
                    e.Graphics.DrawString(label, font, text, page.Left + (page.Width - labelSize.Width) / 2, page.Top + 20);
                }
            }

            foreach (AreaHighlight mark in annotations)
            {
                if (mark.pageIndex != i) continue;
                RectangleF rect = new RectangleF(
                    page.Left + (float)(mark.x * page.Width),
                    page.Top + (float)(mark.y * page.Height),
                    Math.Max(2, (float)(mark.width * page.Width)),
                    Math.Max(2, (float)(mark.height * page.Height)));
                using (Brush fill = new SolidBrush(Color.FromArgb(112, 255, 230, 75)))
                using (Pen outline = new Pen(Color.FromArgb(215, 220, 158, 20), 1))
                {
                    e.Graphics.FillRectangle(fill, rect);
                    e.Graphics.DrawRectangle(outline, rect.X, rect.Y, rect.Width, rect.Height);
                }
            }
        }

        if (dragging && dragPage >= 0 && dragPage < pageBounds.Count)
        {
            Rectangle page = pageBounds[dragPage];
            Rectangle selection = MakeRectangle(dragStart, dragEnd);
            selection.Intersect(page);
            using (Brush fill = new SolidBrush(Color.FromArgb(85, 255, 210, 65)))
            using (Pen outline = new Pen(Color.FromArgb(230, 218, 152, 15), 1))
            {
                e.Graphics.FillRectangle(fill, selection);
                e.Graphics.DrawRectangle(outline, selection);
            }
        }
        e.Graphics.Restore(saved);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !highlightMode || pageBounds.Count == 0) return;
        Point contentPoint = ToContentPoint(e.Location);
        dragPage = FindPageAt(contentPoint);
        if (dragPage < 0) return;
        dragging = true;
        dragStart = contentPoint;
        dragEnd = contentPoint;
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging) return;
        dragEnd = ToContentPoint(e.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!dragging || e.Button != MouseButtons.Left) return;
        dragging = false;
        Capture = false;
        dragEnd = ToContentPoint(e.Location);
        Rectangle bounds = MakeRectangle(dragStart, dragEnd);
        Rectangle page = pageBounds[dragPage];
        bounds.Intersect(page);
        Invalidate();
        if (bounds.Width < 3 || bounds.Height < 3 || page.Width <= 0 || page.Height <= 0) return;
        RectangleF normalized = new RectangleF(
            (float)(bounds.X - page.X) / page.Width,
            (float)(bounds.Y - page.Y) / page.Height,
            (float)bounds.Width / page.Width,
            (float)bounds.Height / page.Height);
        EventHandler<HighlightCreatedEventArgs> handler = HighlightCreated;
        if (handler != null) handler(this, new HighlightCreatedEventArgs(dragPage, normalized));
        dragPage = -1;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            renderTimer.Stop();
            renderTimer.Dispose();
            DisposeRenderedPages();
        }
        base.Dispose(disposing);
    }

    private void RebuildLayout(int restorePage, double restoreOffset, bool restore)
    {
        if (pageSizes.Count == 0) return;
        layoutInProgress = true;
        try
        {
            float maxWidth = pageSizes.Max(s => s.Width * zoom);
            contentWidth = Math.Max(ClientSize.Width, (int)Math.Ceiling(maxWidth) + OuterMargin * 2);
            pageBounds.Clear();
            int y = OuterMargin;
            for (int i = 0; i < pageSizes.Count; i++)
            {
                int width = Math.Max(1, (int)Math.Ceiling(pageSizes[i].Width * zoom));
                int height = Math.Max(1, (int)Math.Ceiling(pageSizes[i].Height * zoom));
                int x = Math.Max(OuterMargin, (contentWidth - width) / 2);
                pageBounds.Add(new Rectangle(x, y, width, height));
                y += height + PageGap;
            }
            AutoScrollMinSize = new Size(contentWidth, y - PageGap + OuterMargin);
            if (restore)
            {
                int safePage = Math.Max(0, Math.Min(pageBounds.Count - 1, restorePage));
                int restoreY = pageBounds[safePage].Top + (int)Math.Round(pageBounds[safePage].Height * Math.Max(0, Math.Min(1, restoreOffset)));
                int restoreX = Math.Max(0, (contentWidth - ClientSize.Width) / 2);
                AutoScrollPosition = new Point(restoreX, restoreY);
            }
            else
            {
                AutoScrollPosition = new Point(Math.Max(0, (contentWidth - ClientSize.Width) / 2), 0);
            }
            currentPage = -1;
        }
        finally
        {
            layoutInProgress = false;
        }
        Invalidate();
        NotifyReadingPosition();
    }

    private void QueueVisibleRender()
    {
        if (!active || pdfDocument == null || pageBounds.Count == 0 || IsDisposed) return;
        renderTimer.Stop();
        renderTimer.Start();
    }

    internal void RenderPagesNearViewport()
    {
        if (pdfDocument == null || pageBounds.Count == 0 || ClientSize.Height <= 0) return;
        Point scroll = AutoScrollPosition;
        int top = Math.Max(0, -scroll.Y - ClientSize.Height);
        int bottom = -scroll.Y + ClientSize.Height * 2;
        HashSet<int> wanted = new HashSet<int>();
        for (int i = 0; i < pageBounds.Count; i++)
        {
            Rectangle page = pageBounds[i];
            if (page.Bottom >= top && page.Top <= bottom) wanted.Add(i);
        }

        foreach (int index in wanted)
        {
            if (renderedPages.ContainsKey(index)) continue;
            try { renderedPages.Add(index, WindowsPdfRenderer.Render(pdfDocument, index, zoom)); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("PDF page render failed: " + ex.Message); }
        }
        foreach (int index in renderedPages.Keys.Where(i => !wanted.Contains(i)).ToArray())
        {
            renderedPages[index].Dispose();
            renderedPages.Remove(index);
        }
        Invalidate();
    }

    private void NotifyReadingPosition()
    {
        if (pageBounds.Count == 0) return;
        int page;
        double offset;
        CaptureReadingPosition(out page, out offset);
        if (page != currentPage)
        {
            currentPage = page;
            EventHandler<int> pageHandler = ActivePageChanged;
            if (pageHandler != null) pageHandler(this, page);
        }
        EventHandler<ReadingPositionEventArgs> handler = ReadingPositionChanged;
        if (handler != null) handler(this, new ReadingPositionEventArgs(page, offset));
    }

    private int FindPageAt(Point contentPoint)
    {
        for (int i = 0; i < pageBounds.Count; i++)
            if (pageBounds[i].Contains(contentPoint)) return i;
        return -1;
    }

    private Point ToContentPoint(Point clientPoint)
    {
        Point scroll = AutoScrollPosition;
        return new Point(clientPoint.X - scroll.X, clientPoint.Y - scroll.Y);
    }

    private void DisposeRenderedPages()
    {
        foreach (Bitmap image in renderedPages.Values) image.Dispose();
        renderedPages.Clear();
    }

    private static Rectangle MakeRectangle(Point first, Point second)
    {
        return new Rectangle(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y),
            Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y));
    }

    private static float ClampZoom(float value)
    {
        return Math.Max(0.35f, Math.Min(2.2f, value));
    }
}
