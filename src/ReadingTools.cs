using System;
using System.Drawing;
using System.Collections.Generic;
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
        Width = 168;
        Opacity = RestingOpacity;
        strip.Dock = DockStyle.Fill;
        Controls.Add(strip);
        hoverTimer.Tick += delegate
        {
            bool editing = reader.PageInputFocused;
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
    private readonly List<ToolStripControlHost> toolGroups = new List<ToolStripControlHost>();
    private ToolStrip annotationStrip, annotationEditStrip, historyStrip;
    internal bool PageInputFocused { get { return pageBox.TextBox.ContainsFocus; } }
    private ReadingToolsWindow rightTools;
    private readonly ToolStripButton directoryButton = new ToolStripButton("目录");
    private readonly ToolStripButton thumbnailsButton = new ToolStripButton("缩略图");
    private readonly ToolStripButton bookmarksButton = new ToolStripButton("书签");
    internal bool DispatchToolKey(ref Message msg, Keys keys) { return ProcessCmdKey(ref msg, keys); }

    private void InitializeReadingTools()
    {
        readingStrip.GripStyle = ToolStripGripStyle.Hidden;
        readingStrip.AutoSize = false; readingStrip.Padding = Padding.Empty; readingStrip.BackColor = Color.White;
        readingStrip.LayoutStyle = ToolStripLayoutStyle.Table;
        var table = (TableLayoutSettings)readingStrip.LayoutSettings;
        table.ColumnCount = 1; table.RowCount = 8;
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var button in new[] { directoryButton, thumbnailsButton, bookmarksButton }) { button.AutoSize = false; button.Size = new Size(45, 28); }
        AddToolGroup("导航", directoryButton, thumbnailsButton, bookmarksButton);
        directoryButton.ToolTipText = "显示/隐藏 PDF 目录 (Ctrl+B)";
        thumbnailsButton.ToolTipText = "显示/隐藏全部页面缩略图";
        bookmarksButton.ToolTipText = "显示/隐藏用户书签";
        directoryButton.Click += delegate { ToggleNavigation(0); };
        bookmarksButton.Click += delegate { ToggleNavigation(1); };
        thumbnailsButton.Click += delegate { ToggleNavigation(2); };
        previousButton.AutoSize = nextButton.AutoSize = false;
        previousButton.Size = nextButton.Size = new Size(25, 28);
        pageBox.AutoSize = false; pageBox.Size = new Size(42, 26); pageBox.Margin = new Padding(1, 2, 1, 1);
        pageTotalLabel.AutoSize = false; pageTotalLabel.Size = new Size(42, 28);
        AddToolGroup("页码", previousButton, pageBox, pageTotalLabel, nextButton);
        zoomOutButton.AutoSize = zoomInButton.AutoSize = false;
        zoomOutButton.Size = zoomInButton.Size = new Size(32, 28);
        zoomLabel.AutoSize = false; zoomLabel.Size = new Size(68, 28);
        AddToolGroup("缩放", zoomOutButton, zoomLabel, zoomInButton);
        highlightButton.Text = "高亮 Ctrl+Shift+H"; highlightButton.AutoSize = false; highlightButton.Size = new Size(142,28);
        annotationStrip = AddToolGroup("注释工具", highlightButton);
        annotationEditStrip = AddToolGroup("注释编辑", new ToolStripLabel("选择注释后编辑"));
        saveButton.AutoSize = false; saveButton.Size = new Size(44,28);
        historyStrip = AddToolGroup("保存与历史", saveButton);
        var addBookmark = new ToolStripButton("添加书签 Ctrl+D") { AutoSize = false, Size = new Size(142,28) };
        addBookmark.Click += delegate { AddBookmark(false); }; AddToolGroup("用户书签", addBookmark);
        helpButton.Text="快捷键 F1"; helpButton.AutoSize=false; helpButton.Size=new Size(67,28);
        statusLabel.AutoSize=false; statusLabel.Size=new Size(77,28);
        AddToolGroup("帮助与状态", helpButton, statusLabel);
        rightTools = new ReadingToolsWindow(this, readingStrip);
        rightTools.ClientSizeChanged += delegate { ReflowReadingTools(); };
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
    private ToolStrip AddToolGroup(string title, params ToolStripItem[] items)
    {
        var panel = new Panel { BackColor=Color.White, Margin=Padding.Empty };
        var label = new Label { Text=title, ForeColor=Color.SlateGray, AutoSize=false, Height=18, Width=150, TextAlign=ContentAlignment.MiddleLeft };
        var strip = new ToolStrip { AutoSize=false, GripStyle=ToolStripGripStyle.Hidden, BackColor=Color.White, Padding=Padding.Empty, CanOverflow=false, LayoutStyle=ToolStripLayoutStyle.HorizontalStackWithOverflow };
        strip.Items.AddRange(items);
        panel.Controls.Add(label);panel.Controls.Add(strip);
        panel.Resize += delegate
        {
            int top=Math.Max(0,(panel.Height-46)/2);
            label.SetBounds(3,top,Math.Max(1,panel.Width-6),18);
            strip.SetBounds(0,top+18,panel.Width,28);
        };
        var host=new ToolStripControlHost(panel) { AutoSize=false, Margin=Padding.Empty, Padding=Padding.Empty };
        int row=toolGroups.Count;toolGroups.Add(host);readingStrip.Items.Add(host);
        var table=(TableLayoutSettings)readingStrip.LayoutSettings;
        table.RowStyles.Add(new RowStyle(SizeType.Percent,12.5f));table.SetRow(host,row);table.SetColumn(host,0);
        return strip;
    }
    private void ReflowReadingTools()
    {
        int height=Math.Max(1,readingStrip.ClientSize.Height/8),width=Math.Max(1,rightTools.ClientSize.Width-12);
        foreach(var group in toolGroups)group.Size=new Size(width,height);
        readingStrip.PerformLayout();
    }
    private void PositionReadingTools()
    {
        if (rightTools == null || rightTools.IsDisposed || !IsHandleCreated || WindowState == FormWindowState.Minimized) return;
        Rectangle area = navigationSplit.Panel1.RectangleToScreen(navigationSplit.Panel1.ClientRectangle);
        rightTools.Height = Math.Max(1, (int)Math.Round(area.Height * .80));
        ReflowReadingTools();
        rightTools.Location = new Point(area.Right - rightTools.Width - 24, area.Top + (int)Math.Round(area.Height * .10));
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
