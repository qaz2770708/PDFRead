using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed class ReadingToolsWindow : Form
{
    private readonly PdfReaderForm reader;
    private readonly Timer hoverTimer = new Timer { Interval = 100 };
    internal const double RestingOpacity = 0.40; // 60% transparent; only the tools window is translucent.
    protected override bool ShowWithoutActivation { get { return true; } }
    public ReadingToolsWindow(PdfReaderForm owner, ToolStrip strip)
    {
        reader = owner;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        Font = owner.Font;
        Padding = new Padding(5);
        Width = 146;
        Opacity = RestingOpacity;
        strip.Dock = DockStyle.Fill;
        Controls.Add(strip);
        hoverTimer.Tick += delegate
        {
            bool editing = false;
            foreach (ToolStripItem item in strip.Items)
            {
                var host = item as ToolStripControlHost;
                if (host != null && host.Control.ContainsFocus) editing = true;
            }
            Opacity = Bounds.Contains(Cursor.Position) || editing ? 1.0 : RestingOpacity;
        };
        Shown += delegate { hoverTimer.Start(); };
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        return reader.DispatchToolKey(ref msg, keyData) || base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !reader.IsDisposed)
        {
            e.Cancel = true;
            reader.BeginInvoke((Action)delegate { if (!reader.IsDisposed) reader.Close(); });
        }
        base.OnFormClosing(e);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (var pen = new Pen(Color.FromArgb(185, 196, 210))) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) hoverTimer.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed partial class PdfReaderForm
{
    private readonly ToolStrip readingStrip = new ToolStrip();
    private ReadingToolsWindow rightTools;
    private readonly ToolStripButton directoryButton = new ToolStripButton("目录");
    private readonly ToolStripButton thumbnailsButton = new ToolStripButton("缩略图");
    private readonly ToolStripButton bookmarksButton = new ToolStripButton("书签");
    internal bool DispatchToolKey(ref Message msg, Keys keys) { return ProcessCmdKey(ref msg, keys); }

    private void InitializeReadingTools()
    {
        readingStrip.GripStyle = ToolStripGripStyle.Hidden;
        readingStrip.AutoSize = false;
        readingStrip.BackColor = Color.White;
        readingStrip.LayoutStyle = ToolStripLayoutStyle.Flow;
        var flow = (FlowLayoutSettings)readingStrip.LayoutSettings;
        flow.FlowDirection = FlowDirection.LeftToRight;
        flow.WrapContents = true;
        AddToolLabel("阅读工具", 132, 25);
        foreach (var item in new[] { directoryButton, thumbnailsButton, bookmarksButton }) { item.AutoSize = false; item.Size = new Size(42, 30); readingStrip.Items.Add(item); }
        directoryButton.ToolTipText = "显示/隐藏 PDF 目录 (Ctrl+B)";
        thumbnailsButton.ToolTipText = "显示/隐藏全部页面缩略图";
        bookmarksButton.ToolTipText = "显示/隐藏用户书签";
        directoryButton.Click += delegate { ToggleNavigation(0); };
        bookmarksButton.Click += delegate { ToggleNavigation(1); };
        thumbnailsButton.Click += delegate { ToggleNavigation(2); };
        AddToolLabel("页码", 132, 20);
        previousButton.AutoSize = nextButton.AutoSize = false;
        previousButton.Size = nextButton.Size = new Size(23, 28);
        pageBox.AutoSize = false; pageBox.Size = new Size(40, 26); pageBox.Margin = new Padding(1, 2, 1, 1);
        pageTotalLabel.AutoSize = false; pageTotalLabel.Size = new Size(39, 28);
        readingStrip.Items.AddRange(new ToolStripItem[] { previousButton, pageBox, pageTotalLabel, nextButton });
        AddToolLabel("缩放", 132, 20);
        zoomOutButton.AutoSize = zoomInButton.AutoSize = false;
        zoomOutButton.Size = zoomInButton.Size = new Size(30, 28);
        zoomLabel.AutoSize = false; zoomLabel.Size = new Size(62, 28);
        readingStrip.Items.AddRange(new ToolStripItem[] { zoomOutButton, zoomLabel, zoomInButton });
        highlightButton.Text = "区域高亮";
        highlightButton.AutoSize = saveButton.AutoSize = false;
        highlightButton.Size = new Size(75, 32); saveButton.Size = new Size(51, 32);
        readingStrip.Items.AddRange(new ToolStripItem[] { highlightButton, saveButton });
        var addBookmark = new ToolStripButton("添加书签  Ctrl+D") { AutoSize = false, Size = new Size(128, 30) };
        addBookmark.Click += delegate { AddBookmark(false); };
        readingStrip.Items.Add(addBookmark);
        helpButton.AutoSize = false; helpButton.Size = new Size(128, 28);
        readingStrip.Items.Add(helpButton);
        statusLabel.AutoSize = false; statusLabel.Size = new Size(128, 28);
        readingStrip.Items.Add(statusLabel);
        rightTools = new ReadingToolsWindow(this, readingStrip) { Height = 336 };
        Shown += delegate { ShowReadingTools(); };
        LocationChanged += delegate { PositionReadingTools(); };
        SizeChanged += delegate { PositionReadingTools(); };
        navigationSplit.SplitterMoved += delegate { PositionReadingTools(); };
        Activated += delegate { ShowReadingTools(); };
        Deactivate += delegate
        {
            if (!IsHandleCreated || IsDisposed) return;
            BeginInvoke((Action)delegate
            {
                if (!IsDisposed && rightTools != null && !rightTools.IsDisposed && Form.ActiveForm != this && Form.ActiveForm != rightTools) rightTools.Hide();
            });
        };
        FormClosed += delegate { rightTools.Dispose(); };
    }
    private void AddToolLabel(string text, int width, int height)
    {
        readingStrip.Items.Add(new ToolStripLabel(text) { AutoSize = false, Size = new Size(width, height), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.SlateGray });
    }
    private void PositionReadingTools()
    {
        if (rightTools == null || rightTools.IsDisposed || !IsHandleCreated || WindowState == FormWindowState.Minimized) return;
        Rectangle area = navigationSplit.Panel1.RectangleToScreen(navigationSplit.Panel1.ClientRectangle);
        rightTools.Height = Math.Min(336, Math.Max(180, area.Height - 52));
        rightTools.Location = new Point(area.Right - rightTools.Width - 24, area.Top + Math.Max(44, (area.Height - rightTools.Height) / 2));
    }
    private void ShowReadingTools()
    {
        if (rightTools == null || rightTools.IsDisposed || !Visible || WindowState == FormWindowState.Minimized) return;
        PositionReadingTools();
        if (!rightTools.Visible) rightTools.Show(this);
    }
    private void FocusPageInput()
    {
        ShowReadingTools(); rightTools.Opacity = 1; rightTools.Activate(); pageBox.Focus(); pageBox.SelectAll();
    }
}
