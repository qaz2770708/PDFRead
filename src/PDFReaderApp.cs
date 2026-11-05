using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ReaderApplicationContext(args));
    }
}

internal sealed partial class PdfReaderForm : Form
{
    private readonly ToolStrip toolStrip = new ToolStrip();
    private readonly ToolStripButton openButton = new ToolStripButton("打开 PDF");
    private readonly ToolStripButton previousButton = new ToolStripButton("‹") { Enabled = false };
    private readonly ToolStripTextBox pageBox = new ToolStripTextBox { Width = 44, TextBoxTextAlign = HorizontalAlignment.Center, Enabled = false };
    private readonly ToolStripLabel pageTotalLabel = new ToolStripLabel("/ -");
    private readonly ToolStripButton nextButton = new ToolStripButton("›") { Enabled = false };
    private readonly ToolStripButton zoomOutButton = new ToolStripButton("−") { Enabled = false };
    private readonly ToolStripLabel zoomLabel = new ToolStripLabel("82%");
    private readonly ToolStripButton zoomInButton = new ToolStripButton("+") { Enabled = false };
    private readonly ToolStripButton highlightButton = new ToolStripButton("区域高亮：拖拽框选") { CheckOnClick = true, Enabled = false };
    private readonly ToolStripButton saveButton = new ToolStripButton("保存") { Enabled = false };
    private readonly DocumentTabControl documentTabs = new DocumentTabControl();
    private readonly ToolStripLabel statusLabel = new ToolStripLabel("未打开文档");
    private readonly List<DocumentSession> sessions = new List<DocumentSession>();
    private readonly HashSet<DocumentSession> pendingProgressSaves = new HashSet<DocumentSession>();
    private readonly Timer progressSaveTimer = new Timer { Interval = 800 };
    private readonly string[] initialPaths;
    private bool isClosing;
    private readonly ToolStripButton helpButton = new ToolStripButton("快捷键 F1");

    public PdfReaderForm(string[] args)
    {
        initialPaths = args ?? new string[0];
        Text = "PDF 阅读器";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 560);
        Size = new Size(1120, 820);
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.FromArgb(244, 246, 248);

        toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        toolStrip.Padding = new Padding(10, 4, 8, 4);
        toolStrip.Items.Add(openButton);
        openButton.ToolTipText = "打开 PDF (Ctrl+O)";
        saveButton.ToolTipText = "保存数据 (Ctrl+S)";
        zoomInButton.ToolTipText = "放大 (Ctrl++ / Ctrl+滚轮)";
        zoomOutButton.ToolTipText = "缩小 (Ctrl+- / Ctrl+滚轮)";
        highlightButton.ToolTipText = "区域高亮 (Ctrl+Shift+H)，Esc 返回阅读";
        pageBox.ToolTipText = "跳转到 PDF 页码 (Ctrl+G)，输入后按 Enter";
        helpButton.Click += delegate { ShowShortcutHelp(); };
        toolStrip.Dock = DockStyle.Top;
        toolStrip.BackColor = Color.White;

        InitializeNavigation();
        Controls.Add(navigationSplit);
        Controls.Add(toolStrip);
        InitializeReadingTools();
        InitializeAnnotationTools();
        InitializeDataTools();
        InitializeDocumentTabs();

        openButton.Click += delegate { ShowOpenDialog(); };
        previousButton.Click += delegate { NavigateActive(-1); };
        nextButton.Click += delegate { NavigateActive(1); };
        zoomOutButton.Click += delegate { ChangeActiveZoom(-0.1f); };
        zoomInButton.Click += delegate { ChangeActiveZoom(0.1f); };
        saveButton.Click += delegate { DocumentSession session = ActiveSession; if (session != null) SaveSessionWithPrompt(session,true); };
        highlightButton.CheckedChanged += HighlightModeChanged;
        pageBox.KeyDown += PageBoxKeyDown;
        documentTabs.SelectedIndexChanged += SelectedTabChanged;
        progressSaveTimer.Tick += ProgressSaveTimerTick;
        FormClosing += OnFormClosing;
        FormClosed += delegate { DisposeSessions(); };
        Shown += delegate
        {
            foreach (string path in initialPaths)
                if (!String.IsNullOrWhiteSpace(path) && File.Exists(path)) OpenPdf(path);
        };
        UpdateToolbar(null);
    }

    private DocumentSession ActiveSession
    {
        get { return documentTabs.SelectedTab == null ? null : documentTabs.SelectedTab.Tag as DocumentSession; }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        DocumentSession s = ActiveSession;
        if(s!=null && s.Viewer.HandleInlineKey(keyData))return true;
        switch (keyData)
        {
            case Keys.Control | Keys.O: ShowOpenDialog(); return true;
            case Keys.F1: ShowShortcutHelp(); return true;
            case Keys.Control | Keys.W: CloseSelectedTab(); return true;
            case Keys.Control | Keys.S: if (s != null) SaveSessionWithPrompt(s,true); return true;
            case Keys.Control | Keys.Shift | Keys.S: SaveDataElsewhere(s,true); return true;
            case Keys.Control | Keys.Tab:
            case Keys.Control | Keys.Shift | Keys.Tab:
                if (documentTabs.TabCount > 0) documentTabs.SelectedIndex = (documentTabs.SelectedIndex +
                    ((keyData & Keys.Shift) != 0 ? documentTabs.TabCount - 1 : 1)) % documentTabs.TabCount;
                return true;
        }
        if (s != null)
        {
            if(HandleAnnotationShortcut(s,keyData))return true;
            switch (keyData)
            {
                case Keys.Control | Keys.D: AddBookmark(false); return true;
                case Keys.Control | Keys.B: ToggleNavigation(navigationTabs.SelectedIndex); return true;
                case Keys.Control | Keys.G: FocusPageInput(); return true;
                case Keys.Control | Keys.Oemplus:
                case Keys.Control | Keys.Shift | Keys.Oemplus:
                case Keys.Control | Keys.Add: ChangeActiveZoom(0.1f); return true;
                case Keys.Control | Keys.OemMinus:
                case Keys.Control | Keys.Subtract: ChangeActiveZoom(-0.1f); return true;
                case Keys.Control | Keys.D0:
                case Keys.Control | Keys.NumPad0: s.Viewer.SetZoom(1f); return true;
                case Keys.Control | Keys.D1: s.Viewer.FitPage(false); return true;
                case Keys.Control | Keys.D2: s.Viewer.FitPage(true); return true;
                case Keys.Control | Keys.Home: s.Viewer.GoToPage(0, 0); return true;
                case Keys.Control | Keys.End: s.Viewer.GoToPage(s.PageCount - 1, 0); return true;
                case Keys.Control | Keys.PageUp: NavigateActive(-1); return true;
                case Keys.Control | Keys.PageDown: NavigateActive(1); return true;
                case Keys.Control | Keys.Shift | Keys.H: highlightButton.Checked = !highlightButton.Checked; return true;
                case Keys.Escape: documentTabs.CancelDrag(); highlightButton.Checked = false; s.Viewer.CancelAnnotationOperation(); s.Viewer.Focus(); return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ShowShortcutHelp()
    {
        MessageBox.Show(this, "Ctrl+O 打开 PDF\r\nCtrl+S 保存独立数据；Ctrl+Shift+S 另存数据\r\nCtrl+W 关闭标签\r\nCtrl+Tab / Ctrl+Shift+Tab 切换标签\r\nCtrl+G 输入 PDF 页码，Enter 跳转\r\nCtrl+滚轮 / Ctrl++ / Ctrl+- 缩放（35%～220%）\r\nCtrl+0 原始大小；Ctrl+1 适合整页；Ctrl+2 适合宽度\r\nCtrl+PageUp / Ctrl+PageDown 上一页 / 下一页\r\nCtrl+Home / Ctrl+End 第一页 / 最后一页\r\nCtrl+Shift+A 选择/移动；Ctrl+Shift+H 高亮\r\nCtrl+Shift+U 下划线；Ctrl+Shift+T 文本批注\r\nCtrl+Shift+P 手写；Ctrl+Shift+R 矩形\r\nCtrl+Shift+E 椭圆；Ctrl+Shift+L 直线\r\nCtrl+Shift+N 页面便签（含副文本）\r\n文本框内直接输入；Ctrl+Enter 完成；Esc 取消待保存编辑\r\nF2 专业属性；Delete 删除；Ctrl+Z/Y 撤销/重做\r\nCtrl+D 添加用户书签；Ctrl+B 显示/隐藏导航栏\r\n书签树中 F2 重命名，Delete 删除，Enter 跳转\r\nF1 显示快捷键", "快捷键", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowOpenDialog()
    {
        using (OpenFileDialog dialog = new OpenFileDialog())
        {
            dialog.Filter = "PDF 文件 (*.pdf)|*.pdf|所有文件 (*.*)|*.*";
            dialog.Multiselect = true;
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            foreach (string path in dialog.FileNames) OpenPdf(path);
        }
    }

    private void OpenPdf(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            string fullPath = Path.GetFullPath(path);
            PdfReaderForm otherWindow = WindowHost?.FindDocument(fullPath);
            if (otherWindow != null && otherWindow != this)
            {
                if (otherWindow.WindowState == FormWindowState.Minimized) otherWindow.WindowState = FormWindowState.Normal;
                otherWindow.Activate();
                otherWindow.OpenPdf(fullPath);
                return;
            }
            string hash = DocumentDataRepository.CalculateSha256(fullPath);
            PdfReaderForm hashWindow = WindowHost?.Windows.FirstOrDefault(w => w.sessions.Any(s => s.PdfHash == hash));
            DocumentSession sameHash = (hashWindow ?? this).sessions.FirstOrDefault(s => s.PdfHash == hash);
            if (sameHash != null)
            {
                sameHash.KnownPaths.Add(fullPath);
                if (!sameHash.DataReadOnly && File.Exists(sameHash.SidecarPath)) DocumentDataRepository.RegisterLocation(fullPath, hash,sameHash.SidecarPath);
                var owner = hashWindow ?? this;
                owner.documentTabs.SelectedTab = sameHash.Tab; owner.Activate(); return;
            }
            DocumentSession samePath = sessions.FirstOrDefault(s => String.Equals(s.PdfPath, fullPath, StringComparison.OrdinalIgnoreCase));
            if (samePath != null && String.Equals(samePath.PdfHash, hash, StringComparison.OrdinalIgnoreCase))
            {
                documentTabs.SelectedTab = samePath.Tab;
                return;
            }
            if (samePath != null)
            {
                samePath.Viewer.FinishInlineEditing();
                if(samePath.Dirty && !SaveSessionWithPrompt(samePath,true))return;
            }

            object pdf = WindowsPdfRenderer.Open(fullPath, out int count);
            SidecarLoadResult loaded = DocumentDataRepository.LoadForPdf(fullPath, hash, count, this,DataPromptOverride);
            if (loaded == null)
            {
                WindowsPdfRenderer.DisposeDocument(pdf);
                return;
            }

            if(samePath!=null && !CloseSession(samePath)){WindowsPdfRenderer.DisposeDocument(pdf);return;}

            DocumentSession session = new DocumentSession
            {
                PdfDocument = pdf,
                PdfPath = fullPath,
                PdfHash = hash,
                SidecarPath = loaded.TargetPath,
                DataReadOnly = loaded.ReadOnly,
                ExpectedDataHash = loaded.ExpectedDataHash,
                DataBaselineCaptured = true,
                PageCount = count,
                Data = loaded.Data,
                Record = loaded.Record,
                Annotations = loaded.Record.annotations ?? new List<AreaHighlight>(),
                Dirty = loaded.NeedsSave,
                ModifiedByRecoveryOrMigration = loaded.NeedsSave,
                Restoring = true
            };
            session.KnownPaths.Add(fullPath);
            session.Viewer = new ContinuousPdfViewer { Dock = DockStyle.Fill };
            session.Viewer.Configure(session.PdfDocument, count, session.Record.zoom, session.Annotations);
            session.Navigation = PdfNavigationReader.Read(fullPath);
            session.Thumbnails = new DocumentThumbnails(session.PdfDocument, count);
            session.Viewer.Links = session.Navigation.Links;
            session.TextSource=new PdfTextRegions(fullPath);
            session.Viewer.AnnotationLayer.TextRegions=session.TextSource.GetRegions;
            BindSession(session);
            session.Viewer.SetHighlightMode(highlightButton.Checked);

            session.Tab = new TabPage(Path.GetFileName(fullPath));
            session.Tab.Tag = session;
            session.Tab.ToolTipText = fullPath + "\r\n点击 × 或双击标签关闭；按住拖动排序或拖出窗口";
            session.Tab.Controls.Add(session.Viewer);
            sessions.Add(session);
            documentTabs.TabPages.Add(session.Tab);
            documentTabs.SelectedTab = session.Tab;
            DocumentSession active = ActiveSession;
            foreach (DocumentSession openSession in sessions)
                openSession.Viewer.SetActive(openSession == active);
            UpdateTabTitle(session);
            UpdateToolbar(session);

            BeginInvoke((Action)delegate
            {
                if (!sessions.Contains(session)) return;
                RestoreSessionPosition(session);
                if (session.ModifiedByRecoveryOrMigration) SaveSession(session);
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法打开 PDF。\r\n\r\n" + Unwrap(ex).Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ReadingPositionChanged(DocumentSession session, ReadingPositionEventArgs e)
    {
        if (movingSession || documentTabs.Reordering || session.Restoring || ActiveSession != session) return;
        if (ActiveSession == session) zoomLabel.Text = Math.Round(session.Viewer.Zoom * 100) + "%";
        bool changed = e.PageIndex != session.Record.lastPage ||
            Math.Abs(e.PageOffset - session.Record.lastPageOffset) > 0.001 ||
            Math.Abs(session.Viewer.Zoom - session.Record.zoom) > 0.001f ||
            Math.Abs(session.Viewer.CaptureHorizontalPosition() - session.Record.lastPageHorizontal) > 0.001;
        if (!changed) return;
        session.Record.lastPage = e.PageIndex;
        session.Record.lastPageOffset = e.PageOffset;
        session.Record.zoom = session.Viewer.Zoom;
        session.Record.lastPageHorizontal = session.Viewer.CaptureHorizontalPosition();
        if (session.DataReadOnly) return;
        if (!session.Dirty) MarkDirty(session);
        pendingProgressSaves.Add(session);
        progressSaveTimer.Stop();
        progressSaveTimer.Start();
    }

    private void ActivePageChanged(DocumentSession session, int page)
    {
        if (ActiveSession != session) return;
        pageBox.Text = (page + 1).ToString();
        pageTotalLabel.Text = "/ " + session.PageCount;
        previousButton.Enabled = page > 0;
        nextButton.Enabled = page < session.PageCount - 1;
        thumbnailView.SelectPage(page, true);
    }

    private void SelectedTabChanged(object sender, EventArgs e)
    {
        if (movingSession || documentTabs.Reordering) return;
        foreach(var open in sessions)open.Viewer.FinishInlineEditing();
        FlushPendingProgress();
        DocumentSession active = ActiveSession;
        foreach (DocumentSession session in sessions) session.Viewer.SetActive(session == active);
        UpdateToolbar(active);
        if (active != null)
        {
            if (!active.Restoring) RestoreSessionPosition(active);
            active.Viewer.Focus();
        }
    }

    private void HighlightModeChanged(object sender, EventArgs e)
    {
        if(!syncingAnnotationTools) ActiveSession?.Viewer.SetAnnotationTool(highlightButton.Checked?AnnotationTool.Highlight:AnnotationTool.Select);
    }

    private void RestoreSessionPosition(DocumentSession session)
    {
        int page = session.Record.lastPage;
        double offset = session.Record.lastPageOffset, horizontal = session.Record.lastPageHorizontal;
        float zoom = session.Record.zoom;
        session.Restoring = true;
        try
        {
            if (Math.Abs(session.Viewer.Zoom - zoom) > .001f) session.Viewer.SetZoom(zoom);
            session.Viewer.GoToLocation(page, offset, horizontal);
        }
        finally { session.Restoring = false; }
        if (session == ActiveSession) UpdateToolbar(session);
    }

    private void NavigateActive(int delta)
    {
        DocumentSession session = ActiveSession;
        if (session == null) return;
        session.Viewer.GoToPage(session.Viewer.CurrentPage + delta, 0);
    }

    private void ChangeActiveZoom(float delta)
    {
        DocumentSession session = ActiveSession;
        if (session == null) return;
        session.Viewer.SetZoom(session.Viewer.Zoom + delta);
        session.Record.zoom = session.Viewer.Zoom;
        if(session.DataReadOnly){zoomLabel.Text=Math.Round(session.Viewer.Zoom*100)+"%";return;}
        MarkDirty(session);
        pendingProgressSaves.Add(session);
        progressSaveTimer.Stop();
        progressSaveTimer.Start();
        zoomLabel.Text = Math.Round(session.Viewer.Zoom * 100).ToString() + "%";
    }

    private void PageBoxKeyDown(object sender, KeyEventArgs e)
    {
        DocumentSession session = ActiveSession;
        if (e.KeyCode != Keys.Enter || session == null) return;
        if (Int32.TryParse(pageBox.Text, out int requested))
            session.Viewer.GoToPage(Math.Max(0, Math.Min(session.PageCount - 1, requested - 1)), 0);
        session.Viewer.Focus();
        e.SuppressKeyPress = true;
    }

    private void ProgressSaveTimerTick(object sender, EventArgs e)
    {
        progressSaveTimer.Stop();
        FlushPendingProgress();
    }

    private void FlushPendingProgress()
    {
        progressSaveTimer.Stop();
        foreach (DocumentSession session in pendingProgressSaves.ToArray())
            if (sessions.Contains(session) && session.Dirty) SaveSession(session);
        pendingProgressSaves.Clear();
    }

    private void MarkDirty(DocumentSession session)
    {
        session.Dirty = true;
        UpdateTabTitle(session);
        if (ActiveSession == session) SetStatus("未保存", Color.FromArgb(180, 83, 9));
    }

    private void UpdateTabTitle(DocumentSession session)
    {
        if (session.Tab == null) return;
        string name = Path.GetFileName(session.PdfPath);
        session.Tab.Text = session.Dirty || session.Viewer.InlineEditor.HasPendingChanges ? name + " *" : name;
    }

    private void UpdateToolbar(DocumentSession session)
    {
        RefreshNavigation(session);
        bool open = session != null;
        pageBox.Enabled = open;
        previousButton.Enabled = open && session.Viewer.CurrentPage > 0;
        nextButton.Enabled = open && session.Viewer.CurrentPage < session.PageCount - 1;
        zoomOutButton.Enabled = open;
        zoomInButton.Enabled = open;
        highlightButton.Enabled = open;
        saveButton.Enabled = open;
        dataButton.Enabled = open;
        if (!open)
        {
            pageBox.Text = String.Empty;
            pageTotalLabel.Text = "/ -";
            zoomLabel.Text = "82%";
            AccessibleDescription = "请打开 PDF";
            SyncAnnotationControls();
            SetStatus("未打开文档", Color.FromArgb(82, 96, 109));
            Text = "PDF 阅读器";
            return;
        }
        int page;
        double offset;
        session.Viewer.CaptureReadingPosition(out page, out offset);
        pageBox.Text = (page + 1).ToString();
        pageTotalLabel.Text = "/ " + session.PageCount;
        zoomLabel.Text = Math.Round(session.Viewer.Zoom * 100).ToString() + "%";
        AccessibleDescription = session.PdfPath;
        SyncAnnotationControls();
        Text = "PDF 阅读器 - " + Path.GetFileName(session.PdfPath);
        ShowSaveState(session);
        previousButton.Enabled = page > 0;
        nextButton.Enabled = page < session.PageCount - 1;
    }

    private void SetStatus(string text, Color color)
    {
        statusLabel.Text = text;
        statusLabel.ForeColor = color;
        statusLabel.ToolTipText = text;
    }

    private void CloseSelectedTab()
    {
        DocumentSession session = ActiveSession;
        if (session != null) CloseSession(session);
    }

    private bool CloseSession(DocumentSession session)
    {
        session.Viewer.FinishInlineEditing();
        if (session.Dirty && !SaveSessionWithPrompt(session,true)) return false;
        UnbindSession(session);
        pendingProgressSaves.Remove(session);
        sessions.Remove(session);
        documentTabs.TabPages.Remove(session.Tab);
        session.Viewer.Dispose();
        session.Thumbnails?.Dispose();
        session.TextSource?.Dispose();
        WindowsPdfRenderer.DisposeDocument(session.PdfDocument);
        session.Tab.Dispose();
        UpdateToolbar(ActiveSession);
        return true;
    }

    private void OnFormClosing(object sender, FormClosingEventArgs e)
    {
        if (isClosing) return;
        foreach(var session in sessions)session.Viewer.FinishInlineEditing();
        FlushPendingProgress();
        foreach (DocumentSession session in sessions.ToArray())
        {
            if (session.Dirty && !SaveSessionWithPrompt(session,true))
            {
                e.Cancel = true;
                return;
            }
        }
        isClosing = true;
    }

    private void DisposeSessions()
    {
        if (sessionResourcesDisposed) return;
        sessionResourcesDisposed = true;
        progressSaveTimer.Stop();
        progressSaveTimer.Dispose();
        foreach (DocumentSession session in sessions.ToArray())
        {
            UnbindSession(session);
            session.Viewer.Dispose();
            session.Thumbnails?.Dispose();
            session.TextSource?.Dispose();
            WindowsPdfRenderer.DisposeDocument(session.PdfDocument);
        }
        sessions.Clear();
    }

    private static Exception Unwrap(Exception ex)
    {
        AggregateException aggregate = ex as AggregateException;
        if (aggregate != null && aggregate.InnerException != null) return aggregate.InnerException;
        return ex.InnerException ?? ex;
    }
}
