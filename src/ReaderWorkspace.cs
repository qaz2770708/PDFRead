using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal sealed class ReaderApplicationContext : ApplicationContext
{
    internal readonly List<PdfReaderForm> Windows = new List<PdfReaderForm>();
    public ReaderApplicationContext(string[] paths) { CreateWindow(paths); }
    internal PdfReaderForm CreateWindow(string[] paths)
    {
        var form = new PdfReaderForm(paths) { WindowHost = this };
        Windows.Add(form);
        form.FormClosed += delegate { Windows.Remove(form); if (Windows.Count == 0) ExitThread(); };
        form.Show();
        return form;
    }
    internal PdfReaderForm FindDocument(string path)
    {
        return Windows.FirstOrDefault(w => w.HasDocument(path));
    }
}

internal sealed partial class PdfReaderForm
{
    internal ReaderApplicationContext WindowHost;
    private bool movingSession;
    internal bool HasDocument(string path) { return sessions.Any(s => String.Equals(s.PdfPath, path, StringComparison.OrdinalIgnoreCase)); }
    private void InitializeDocumentTabs()
    {
        documentTabs.CloseRequested += delegate(object sender, TabPage tab) { var s = tab.Tag as DocumentSession; if (s != null) CloseSession(s); };
        documentTabs.DetachRequested += delegate(object sender, TabDetachEventArgs e) { DetachSession(e.Tab.Tag as DocumentSession, e.ScreenLocation); };
    }
    private void BindSession(DocumentSession s)
    {
        s.DraftHandler=delegate{UpdateTabTitle(s);ShowSaveState(s);};
        s.Viewer.AnnotationDraftStateChanged+=s.DraftHandler;
        s.AnnotationHandler = delegate { MarkDirty(s); SaveSession(s); SyncAnnotationControls(); };
        s.SelectionHandler = delegate { if(ActiveSession==s)SyncAnnotationControls(); };
        s.EditHandler = delegate(object sender,AnnotationEditEventArgs e) { e.Cancel=!EditAnnotationProperties(s,e.Annotation); };
        s.PageHandler = delegate(object sender, int page) { ActivePageChanged(s, page); };
        s.PositionHandler = delegate(object sender, ReadingPositionEventArgs e) { ReadingPositionChanged(s, e); };
        s.LinkHandler = delegate(object sender, NavigationTarget target) { NavigateTarget(s, target); };
        s.Viewer.AnnotationsChanged += s.AnnotationHandler;
        s.Viewer.AnnotationSelectionChanged += s.SelectionHandler;
        s.Viewer.AnnotationEditRequested += s.EditHandler;
        s.Viewer.ActivePageChanged += s.PageHandler;
        s.Viewer.ReadingPositionChanged += s.PositionHandler;
        s.Viewer.LinkActivated += s.LinkHandler;
    }
    private void UnbindSession(DocumentSession s)
    {
        s.Viewer.AnnotationsChanged -= s.AnnotationHandler;
        s.Viewer.AnnotationDraftStateChanged-=s.DraftHandler;
        s.Viewer.AnnotationSelectionChanged -= s.SelectionHandler;
        s.Viewer.AnnotationEditRequested -= s.EditHandler;
        s.Viewer.ActivePageChanged -= s.PageHandler;
        s.Viewer.ReadingPositionChanged -= s.PositionHandler;
        s.Viewer.LinkActivated -= s.LinkHandler;
    }
    internal PdfReaderForm DetachSession(DocumentSession s, Point screenLocation)
    {
        if (s == null || !sessions.Contains(s)) return null;
        PdfReaderForm target = WindowHost != null ? WindowHost.CreateWindow(new string[0]) : new PdfReaderForm(new string[0]);
        Rectangle area = Screen.FromPoint(screenLocation).WorkingArea;
        target.StartPosition = FormStartPosition.Manual;
        target.Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        target.Location = new Point(Math.Max(area.Left, Math.Min(area.Right - target.Width, screenLocation.X - 100)),
            Math.Max(area.Top, Math.Min(area.Bottom - target.Height, screenLocation.Y - 25)));
        if (!target.Visible) target.Show();
        if (!TransferSessionTo(s, target)) { target.Close(); return null; }
        target.Activate();
        return target;
    }
    internal bool TransferSessionTo(DocumentSession s, PdfReaderForm target)
    {
        if (s == null || target == null || target == this || !sessions.Contains(s) || target.HasDocument(s.PdfPath)) return false;
        s.Viewer.FinishInlineEditing();
        int page; double offset;
        if(UsesModernUi){page=s.Record.lastPage;offset=s.Record.lastPageOffset;}else s.Viewer.CaptureReadingPosition(out page, out offset);
        double horizontal = UsesModernUi?s.Record.lastPageHorizontal:s.Viewer.CaptureHorizontalPosition();
        float zoom = UsesModernUi?s.Record.zoom:s.Viewer.Zoom;
        int originalIndex = documentTabs.TabPages.IndexOf(s.Tab);
        movingSession = target.movingSession = true;
        bool transferred = false;
        try
        {
            UnbindSession(s);
            pendingProgressSaves.Remove(s);
            sessions.Remove(s);
            documentTabs.TabPages.Remove(s.Tab);
            target.sessions.Add(s);
            target.documentTabs.TabPages.Add(s.Tab);
            target.BindSession(s);
            target.documentTabs.SelectedTab = s.Tab;
            transferred = true;
        }
        catch (Exception ex)
        {
            target.UnbindSession(s);
            target.sessions.Remove(s);
            target.documentTabs.TabPages.Remove(s.Tab);
            if (!sessions.Contains(s)) sessions.Add(s);
            documentTabs.TabPages.Insert(Math.Min(originalIndex, documentTabs.TabCount), s.Tab);
            BindSession(s);
            documentTabs.SelectedTab = s.Tab;
            MessageBox.Show(this, "标签移动失败，文档仍保留在原窗口。\r\n" + ex.Message, "移动标签", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { movingSession = target.movingSession = false; }
        foreach (var item in sessions) item.Viewer.SetActive(item == ActiveSession);
        foreach (var item in target.sessions) item.Viewer.SetActive(item == target.ActiveSession);
        UpdateToolbar(ActiveSession); target.UpdateToolbar(target.ActiveSession);
        s.Restoring=true;try{s.Viewer.SetZoom(zoom);s.Viewer.GoToLocation(page, offset, horizontal);}finally{s.Restoring=false;}
        PdfReaderForm owner = transferred ? target : this;
        if (s.Dirty) { owner.pendingProgressSaves.Add(s); owner.progressSaveTimer.Start(); }
        s.Viewer.Focus();
        return transferred;
    }
}
