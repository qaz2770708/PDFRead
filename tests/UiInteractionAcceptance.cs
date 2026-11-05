using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

internal static class UiInteractionAcceptance
{
    private static object Field(object o, string n) { return o.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o); }
    private static object Call(object o, string n, params object[] args) { return o.GetType().GetMethod(n, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(o, args); }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
    private static Point Center(Rectangle r) { return new Point(r.Left + r.Width / 2, r.Top + r.Height / 2); }
    private static MouseEventArgs Mouse(Point p) { return new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0); }
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string folder = Path.GetFullPath(Path.Combine("work", "ui-acceptance-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        string first = Path.Combine(folder, "first.pdf"), second = Path.Combine(folder, "second.pdf"), third = Path.Combine(folder, "third.pdf");
        foreach (var path in new[] { first, second, third }) File.Copy(args[0], path);
        string hash = DocumentDataRepository.CalculateSha256(first);
        var context = new ReaderApplicationContext(new[] { first, second, third });
        var source = context.Windows[0];
        PdfReaderForm detached = null;
        DocumentSession moved = null;
        var timer = new Timer { Interval = 1400 };
        int stage = 0;
        timer.Tick += delegate
        {
            timer.Stop();
            try
            {
                if (stage == 1)
                {
                    Check(context.Windows.Count == 1 && detached.Visible && !detached.IsDisposed, "message loop survives closing original window");
                    moved.Viewer.RenderPagesNearViewport();
                    Check(((Dictionary<int, Bitmap>)Field(moved.Viewer, "renderedPages")).Count > 0, "detached window renders after original closes");
                    Check(DocumentDataRepository.CalculateSha256(first) == hash, "window operations preserve original PDF bytes");
                    foreach (var window in context.Windows.ToArray()) window.Close();
                    Console.WriteLine("ALL_UI_ACCEPTANCE_PASSED");
                    return;
                }
                var sessions = (List<DocumentSession>)Field(source, "sessions");
                var tabs = (DocumentTabControl)Field(source, "documentTabs");
                var split = (SplitContainer)Field(source, "navigationSplit");
                var strip = (ToolStrip)Field(source, "toolStrip");
                var tools = (ReadingToolsWindow)Field(source, "rightTools");
                var readingStrip = (ToolStrip)Field(source, "readingStrip");
                Check(strip.Items.Count == 1 && strip.Items[0].Text == "打开 PDF", "top toolbar contains only open PDF");
                Check(ReadingToolsWindow.RestingOpacity == .4 && tools.Owner == source && tools.Visible, "owned right tools use sixty percent transparency");
                Check(((ToolStripItem)Field(source, "pageBox")).Owner == readingStrip && ((ToolStripItem)Field(source, "saveButton")).Owner == readingStrip, "page zoom highlight and save live in right tools");
                Check(((ToolStripItem)Field(source, "statusLabel")).Visible && readingStrip.ClientRectangle.Contains(((ToolStripItem)Field(source, "statusLabel")).Bounds), "save status is visible inside compact tools");
                Check(((ToolStripItem)Field(source, "pageBox")).Bounds.Top == ((ToolStripItem)Field(source, "previousButton")).Bounds.Top + 1, "page input stays in a compact single row");
                Check(split.Panel2Collapsed, "navigation starts hidden");
                ((ToolStripButton)Field(source, "directoryButton")).PerformClick();
                Check(!split.Panel2Collapsed && ((TabControl)Field(source, "navigationTabs")).SelectedIndex == 0, "directory button opens right navigation");
                for (int i = 0; i < 8; i++) Call(source, "RenderNavigationThumbnail");
                DocumentSession selected = (DocumentSession)tabs.SelectedTab.Tag;
                Check(selected.Thumbnails.Peek(1) != null && selected.Thumbnails.Peek(0) != null, "directory entries have rendered target page previews");
                var outline = (TreeView)Field(source, "outlineTree"); int beforeFold = selected.Viewer.CurrentPage;
                Call(outline, "OnNodeMouseClick", new TreeNodeMouseClickEventArgs(outline.Nodes[0], MouseButtons.Left, 1, 5, 20));
                Check(!outline.Nodes[0].IsExpanded && selected.Viewer.CurrentPage == beforeFold, "directory fold button collapses hierarchy without navigating");
                Call(outline, "OnNodeMouseClick", new TreeNodeMouseClickEventArgs(outline.Nodes[0], MouseButtons.Left, 1, 5, 20));
                Check(outline.Nodes[0].IsExpanded, "directory fold button expands hierarchy");
                ((ToolStripButton)Field(source, "directoryButton")).PerformClick();
                Check(split.Panel2Collapsed, "same directory button hides navigation");
                ((ToolStripButton)Field(source, "thumbnailsButton")).PerformClick();
                var thumbnails = (PageThumbnailView)Field(source, "thumbnailView");
                for (int i = 0; i < 8; i++) Call(source, "RenderNavigationThumbnail");
                Check(selected.Thumbnails.CachedCount > 0, "all-page thumbnail list renders lazily");
                thumbnails.AutoScrollPosition = Point.Empty;
                Call(thumbnails, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 90, 240, 0));
                Check(selected.Viewer.CurrentPage == 1 && ((ToolStripTextBox)Field(source, "pageBox")).Text == "2", "thumbnail click jumps and updates page indicator");
                Bitmap preview = selected.Thumbnails.Peek(1);
                bool ink = false;
                for (int y = 0; y < preview.Height; y++) for (int x = 0; x < preview.Width; x++) if (preview.GetPixel(x, y).R < 200) ink = true;
                Check(ink, "thumbnail contains actual PDF content");
                using (var shot = new Bitmap(source.Width, source.Height)) { source.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size)); shot.Save(Path.Combine(folder, "thumbnails.png")); }
                using (var shot = new Bitmap(tools.Width, tools.Height)) { tools.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size)); shot.Save(Path.Combine(folder, "right-tools.png")); }

                TabPage reordered = tabs.TabPages[2];
                Point from = Center(tabs.GetTabRect(2)), to = Center(tabs.GetTabRect(0));
                Call(tabs, "OnMouseDown", Mouse(from)); Call(tabs, "OnMouseMove", Mouse(to)); Call(tabs, "OnMouseUp", Mouse(to));
                Check(tabs.TabPages[0] == reordered && tabs.TabCount == 3, "drag reorders tabs without closing documents");

                var closeSession = (DocumentSession)tabs.TabPages[2].Tag;
                closeSession.Record.bookmarks.Add(new UserBookmark { id = "close-test", title = "Saved on X", pageIndex = 1, zoom = 1f });
                Call(source, "MarkDirty", closeSession);
                Point close = Center(tabs.CloseBounds(2));
                Call(tabs, "OnMouseDown", Mouse(close)); Call(tabs, "OnMouseUp", Mouse(close));
                Check(tabs.TabCount == 2 && closeSession.Viewer.IsDisposed, "tab X closes only its document");
                var saved = DocumentDataRepository.LoadForPdf(closeSession.PdfPath, closeSession.PdfHash, 4, null, (a, b) => DialogResult.Yes);
                Check(saved.Record.bookmarks.Any(b => b.id == "close-test"), "tab X saves pending data before closing");
                TabPage doubleTab = tabs.TabPages[1];
                Point doublePoint = new Point(tabs.GetTabRect(1).Left + 30, tabs.GetTabRect(1).Top + 10);
                Call(tabs, "WndProc", Message.Create(tabs.Handle, 0x0203, IntPtr.Zero, new IntPtr((doublePoint.Y << 16) | doublePoint.X)));
                Check(tabs.TabCount == 1 && !tabs.TabPages.Contains(doubleTab), "double click closes requested tab");

                moved = (DocumentSession)tabs.TabPages[0].Tag;
                tabs.SelectedTab = moved.Tab;
                moved.Viewer.SetZoom(1.3f); moved.Viewer.GoToPage(1, .25);
                int page; double offset; moved.Viewer.CaptureReadingPosition(out page, out offset);
                object pdfHandle = moved.PdfDocument; var thumbnailCache = moved.Thumbnails;
                moved.Record.bookmarks.Add(new UserBookmark { id = "transfer-test", title = "Pending move", pageIndex = 1, pageOffset = .25, zoom = 1.3f });
                Call(source, "MarkDirty", moved);
                Point dragStart = Center(tabs.GetTabRect(0));
                Point dropScreen = new Point(source.Left + source.Width + 80, source.Top + 130);
                Point drop = tabs.PointToClient(dropScreen);
                Call(tabs, "OnMouseDown", Mouse(dragStart)); Call(tabs, "OnMouseMove", Mouse(drop));
                Call(tabs, "WndProc", Message.Create(tabs.Handle, 0x0202, IntPtr.Zero, new IntPtr(((int)(ushort)drop.Y << 16) | (ushort)drop.X)));
                Check(context.Windows.Count == 2 && tabs.TabCount == 0 && sessions.Count == 0, "drag outside creates separate window");
                detached = context.Windows.First(w => w != source);
                var targetSessions = (List<DocumentSession>)Field(detached, "sessions");
                Check(targetSessions.Count == 1 && Object.ReferenceEquals(targetSessions[0], moved) && Object.ReferenceEquals(moved.PdfDocument, pdfHandle) && Object.ReferenceEquals(moved.Thumbnails, thumbnailCache), "detachment transfers existing session and caches");
                Check(moved.Record.bookmarks.Any(b => b.id == "transfer-test") && !moved.Viewer.IsDisposed, "pending bookmarks survive detachment");
                moved.Viewer.CaptureReadingPosition(out int restoredPage, out double restoredOffset);
                Check(restoredPage == page && Math.Abs(restoredOffset - offset) < .01 && Math.Abs(moved.Viewer.Zoom - 1.3f) < .001, "detachment preserves page offset and zoom");
                Call(source, "OpenPdf", moved.PdfPath);
                Check(sessions.Count == 0 && targetSessions.Count == 1, "same PDF activates its existing window instead of concurrent sidecar writers");
                source.Close();
                int annotations = moved.Annotations.Count;
                moved.Viewer.GoToPage(0, 0); moved.Viewer.SetHighlightMode(true);
                Rectangle bounds = moved.Viewer.PageRectangles[0]; Point scroll = moved.Viewer.AutoScrollPosition;
                var p1 = new Point(bounds.Left + scroll.X + 50, bounds.Top + scroll.Y + 100);
                var p2 = new Point(p1.X + 100, p1.Y + 30);
                Call(moved.Viewer, "OnMouseDown", Mouse(p1)); Call(moved.Viewer, "OnMouseMove", Mouse(p2)); Call(moved.Viewer, "OnMouseUp", Mouse(p2));
                Check(moved.Annotations.Count == annotations + 1, "detached document has exactly one live annotation handler");
                var reopened = DocumentDataRepository.LoadForPdf(moved.PdfPath, moved.PdfHash, moved.PageCount, null, (a, b) => DialogResult.Yes);
                Check(reopened.Record.bookmarks.Any(b => b.id == "transfer-test") && reopened.Record.annotations.Count == annotations + 1, "detached window writes bookmarks and annotations independently");
                Console.WriteLine("UI_SCREENSHOTS " + folder);
                stage = 1; timer.Interval = 350; timer.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex); Environment.ExitCode = 1;
                foreach (var window in context.Windows.ToArray()) window.Close();
            }
        };
        timer.Start(); Application.Run(context); timer.Dispose(); context.Dispose(); return Environment.ExitCode;
    }
}
