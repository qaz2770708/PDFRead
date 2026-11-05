using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

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

internal sealed partial class ContinuousPdfViewer : ScrollableControl
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
    private bool active;
    private int zoomWheelRemainder;

    public List<PageLink> Links = new List<PageLink>();
    public event EventHandler<NavigationTarget> LinkActivated;

    public event EventHandler<ReadingPositionEventArgs> ReadingPositionChanged;
    public event EventHandler<int> ActivePageChanged;

    public int PageCount { get { return pageBounds.Count; } }
    public int CurrentPage { get { return Math.Max(0, currentPage); } }
    public float Zoom { get { return zoom; } }
    public bool HighlightMode { get { return AnnotationLayer.Tool==AnnotationTool.Highlight; } }
    public Size ContentSize { get { return AutoScrollMinSize; } }
    public IList<Rectangle> PageRectangles { get { return pageBounds.AsReadOnly(); } }

    public ContinuousPdfViewer()
    {
        InitializeAnnotationLayer();
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
        AnnotationLayer.SetMarks(annotations);
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
        AnnotationLayer.SetMarks(annotations);
        Invalidate();
    }

    public void SetHighlightMode(bool enabled) { SetAnnotationTool(enabled?AnnotationTool.Highlight:AnnotationTool.Select); }

    internal IList<SizeF> DocumentPageSizes {get{return pageSizes.AsReadOnly();}}
    private bool modernPresentation;
    internal void SetModernPresentation(bool value){modernPresentation=value;if(value)SetActive(false);}
    public void SetActive(bool value)
    {
        if(modernPresentation)value=false;
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

    public void SetZoomAt(float value, Point anchor)
    {
        if (pageBounds.Count == 0 || Math.Abs(ClampZoom(value) - zoom) < 0.0001f) return;
        Point content = ToContentPoint(anchor);
        int index = FindPageAt(content);
        if (index < 0) { SetZoom(value); return; }
        Rectangle before = pageBounds[index];
        double x = (double)(content.X - before.Left) / before.Width;
        double y = (double)(content.Y - before.Top) / before.Height;
        SetZoom(value);
        Rectangle after = pageBounds[index];
        AutoScrollPosition = new Point(Math.Max(0, after.Left + (int)Math.Round(x * after.Width) - anchor.X),
            Math.Max(0, after.Top + (int)Math.Round(y * after.Height) - anchor.Y));
        NotifyReadingPosition();
        QueueVisibleRender();
        InlineEditor.Reposition();
    }

    public void FitPage(bool widthOnly)
    {
        if (pageSizes.Count == 0) return;
        SizeF size = pageSizes[CurrentPage];
        float fit = (ClientSize.Width - OuterMargin * 2f - SystemInformation.VerticalScrollBarWidth) / size.Width;
        if (!widthOnly) fit = Math.Min(fit, (ClientSize.Height - OuterMargin * 2f) / size.Height);
        SetZoom(fit);
    }

    internal bool HandleZoomWheel(MouseEventArgs e, Keys modifiers)
    {
        if ((modifiers & Keys.Control) == 0) { zoomWheelRemainder = 0; return false; }
        HandledMouseEventArgs handled = e as HandledMouseEventArgs;
        if (handled != null) handled.Handled = true;
        zoomWheelRemainder += e.Delta;
        int steps = zoomWheelRemainder / 120;
        zoomWheelRemainder %= 120;
        if (steps != 0) SetZoomAt(zoom + steps * 0.1f, e.Location);
        return true;
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
        InlineEditor.Reposition();
    }

    public double CaptureHorizontalPosition()
    {
        if (pageBounds.Count == 0) return 0;
        Rectangle page = pageBounds[CurrentPage];
        return Math.Max(0, Math.Min(1, (double)(-AutoScrollPosition.X - page.Left) / page.Width));
    }

    public void GoToLocation(int index, double offset, double horizontal)
    {
        GoToPage(index, offset);
        if (pageBounds.Count == 0) return;
        Rectangle page = pageBounds[Math.Max(0, Math.Min(pageBounds.Count - 1, index))];
        int x = page.Width <= ClientSize.Width ? 0 : Math.Max(0, page.Left + (int)Math.Round(page.Width * Math.Max(0, Math.Min(1,horizontal))));
        AutoScrollPosition = new Point(x,-AutoScrollPosition.Y);
        NotifyReadingPosition();
        QueueVisibleRender();
        InlineEditor.Reposition();
    }

    private PageLink LinkAt(Point client)
    {
        Point content=ToContentPoint(client);
        int index=FindPageAt(content);
        if(index<0) return null;
        Rectangle page=pageBounds[index];
        PointF p=new PointF((float)(content.X-page.Left)/page.Width,(float)(content.Y-page.Top)/page.Height);
        return Links.FirstOrDefault(l=>l.PageIndex==index && l.Bounds.Contains(p));
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
        if (scrollTop > 0 && scrollTop + ClientSize.Height >= AutoScrollMinSize.Height - 2)
        {
            pageIndex = pageBounds.Count - 1;
            Rectangle last = pageBounds[pageIndex];
            pageOffset = Math.Max(0, Math.Min(1, (double)(scrollTop - last.Top) / last.Height));
            return;
        }
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
        InlineEditor.Reposition();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (HandleZoomWheel(e, ModifierKeys)) return;
        base.OnMouseWheel(e);
        QueueVisibleRender();
        NotifyReadingPosition();
        InlineEditor.Reposition();
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

            AnnotationLayer.Paint(e.Graphics,page,i,zoom);
        }
        e.Graphics.Restore(saved);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);Focus();
        if(e.Button!=MouseButtons.Left && e.Button!=MouseButtons.Right)return;
        if(!BeginAnnotation(e) && e.Button==MouseButtons.Left && AnnotationLayer.Tool==AnnotationTool.Select)
        { var link=LinkAt(e.Location);if(link!=null)LinkActivated?.Invoke(this,link.Target); }
    }
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);MoveAnnotation(e);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);EndAnnotation(e);}

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            InlineEditor.Dispose();
            ContextMenuStrip?.Dispose();
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
        InlineEditor.Reposition();
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
