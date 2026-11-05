using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal sealed partial class PdfReaderForm
{
    private readonly SplitContainer navigationSplit = new SplitContainer { Dock = DockStyle.Fill, Width = 1120, SplitterDistance = 810, Panel1MinSize = 300, Panel2MinSize = 260, Panel2Collapsed = true, FixedPanel = FixedPanel.Panel2 };
    private readonly TabControl navigationTabs = new TabControl { Dock = DockStyle.Fill };
    private readonly TreeView outlineTree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, ShowNodeToolTips = true };
    private readonly BookmarkTreeView bookmarkTree = new BookmarkTreeView { Dock = DockStyle.Fill, HideSelection = false, LabelEdit = true, ShowNodeToolTips = true };
    private readonly Label outlineMessage = new Label { Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(260,0), Padding = new Padding(6) };
    private readonly ToolStrip bookmarkTools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
    private DocumentSession navigationSession;
    private readonly PageThumbnailView thumbnailView = new PageThumbnailView();
    private readonly Timer thumbnailTimer = new Timer { Interval = 90 };

    private void InitializeNavigation()
    {
        navigationSplit.Panel1.Controls.Add(documentTabs);
        navigationSplit.Panel2.Controls.Add(navigationTabs);
        var header = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        header.Items.Add(new ToolStripLabel("导航"));
        var hide = new ToolStripButton("收起 ×") { Alignment = ToolStripItemAlignment.Right, ToolTipText = "隐藏导航栏 (Ctrl+B)" };
        hide.Click += delegate { SetNavigationVisible(false); };
        header.Items.Add(hide);
        navigationSplit.Panel2.Controls.Add(header);
        TabPage pdf = new TabPage("PDF 目录");
        pdf.Controls.Add(outlineTree); pdf.Controls.Add(outlineMessage);
        TabPage user = new TabPage("用户书签");
        user.Controls.Add(bookmarkTree); user.Controls.Add(bookmarkTools);
        navigationTabs.TabPages.Add(pdf); navigationTabs.TabPages.Add(user);
        TabPage pages = new TabPage("缩略图"); pages.Controls.Add(thumbnailView); navigationTabs.TabPages.Add(pages);
        thumbnailView.PageSelected += delegate(object sender, int page) { var s = ActiveSession; if (s != null) { s.Viewer.GoToPage(page, 0); s.Viewer.Focus(); } };
        thumbnailTimer.Tick += delegate { RenderNavigationThumbnail(); };
        thumbnailTimer.Start();
        navigationTabs.SelectedIndexChanged += delegate { UpdateNavigationButtons(); };
        AddBookmarkTool("添加", "添加当前位置 (Ctrl+D)", () => AddBookmark(false));
        AddBookmarkTool("删除", "删除选中书签及子级 (Delete)", DeleteSelectedBookmark);
        InitializeBookmarkInteraction();
        outlineTree.NodeMouseClick += delegate(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (e.X >= e.Node.Bounds.Left) ActivateOutline(e.Node);
        };
        outlineTree.KeyDown += delegate(object sender, KeyEventArgs e) { if(e.KeyCode==Keys.Enter) { ActivateOutline(outlineTree.SelectedNode); e.SuppressKeyPress=true; } };
        bookmarkTree.NodeMouseClick += delegate(object sender, TreeNodeMouseClickEventArgs e) { if(e.Button==MouseButtons.Left && !bookmarkDragged && e.X>=e.Node.Bounds.Left) ActivateBookmark(e.Node); };
        bookmarkTree.KeyDown += delegate(object sender, KeyEventArgs e)
        {
            if(e.KeyCode==Keys.Enter) { ActivateBookmark(bookmarkTree.SelectedNode); e.SuppressKeyPress=true; }
            else if(e.KeyCode==Keys.F2 && bookmarkTree.SelectedNode!=null) { bookmarkTree.SelectedNode.BeginEdit();e.SuppressKeyPress=true; }
            else if(e.KeyCode==Keys.Delete) { DeleteSelectedBookmark();e.SuppressKeyPress=true; }
        };
        bookmarkTree.BeforeLabelEdit += delegate(object sender, NodeLabelEditEventArgs e) { var b=e.Node.Tag as UserBookmark; if(b!=null)e.Node.Text=b.title; };
        bookmarkTree.AfterLabelEdit += delegate(object sender, NodeLabelEditEventArgs e)
        {
            e.CancelEdit=true;
            var b=e.Node.Tag as UserBookmark;
            if(b!=null && !String.IsNullOrWhiteSpace(e.Label) && ActiveSession==navigationSession)
            { b.title=e.Label.Trim(); MarkDirty(navigationSession);SaveSession(navigationSession); }
            BeginInvoke((Action)delegate { RefreshBookmarks(); });
        };
    }
    private void AddBookmarkTool(string title,string tip,Action action)
    {
        var b=new ToolStripButton(title) { ToolTipText=tip };
        b.Click += delegate { action(); }; bookmarkTools.Items.Add(b);
    }
    private void RefreshNavigation(DocumentSession session)
    {
        navigationSession=session;
        outlineTree.BeginUpdate(); outlineTree.Nodes.Clear();
        if(session!=null && session.Navigation!=null) foreach(var entry in session.Navigation.Outline) outlineTree.Nodes.Add(OutlineNode(entry));
        outlineTree.EndUpdate();
        outlineMessage.Text=session==null ? "打开 PDF 后显示目录" : session.Navigation?.Error ?? (outlineTree.Nodes.Count==0 ? "此 PDF 没有内嵌目录。可使用用户书签；文档内已有的目录链接仍可点击。" : "点击章节跳转；右侧数字为 PDF 实际页码。打印页码可能不同。");
        outlineTree.ExpandAll();
        bookmarkTools.Enabled=session!=null;
        RefreshBookmarks();
        thumbnailView.Configure(session?.Thumbnails, session == null ? 0 : session.PageCount, session == null ? 0 : session.Viewer.CurrentPage);
    }
    private TreeNode OutlineNode(NavigationTarget entry)
    {
        TreeNode n=new TreeNode(entry.Title+(entry.PageIndex<0 ? "（无有效页目标）" : "  · "+(entry.PageIndex+1))) { Tag=entry };
        if(entry.PageIndex<0)n.ForeColor=Color.Gray;
        n.ToolTipText=entry.PageIndex<0?"该章节没有有效的文档内页目标；可展开子章节。":"跳转到 PDF 第 "+(entry.PageIndex+1)+" 页";
        foreach(var child in entry.Children)n.Nodes.Add(OutlineNode(child));
        return n;
    }
    private void ActivateOutline(TreeNode node)
    {
        var target=node?.Tag as NavigationTarget;
        if(target!=null) NavigateTarget(ActiveSession,target);
    }
    private void NavigateTarget(DocumentSession session, NavigationTarget target)
    {
        if(session==null || target==null || target.PageIndex<0 || target.PageIndex>=session.PageCount) return;
        session.Viewer.FinishInlineEditing();
        session.Viewer.GoToPage(target.PageIndex,0);
        if(target.Zoom.HasValue) session.Viewer.SetZoom(target.Zoom.Value);
        else if(target.Fit!=null) session.Viewer.FitPage(target.Fit=="width");
        session.Viewer.GoToLocation(target.PageIndex,target.PageOffset,target.HorizontalOffset);
        session.Viewer.Focus();
    }
    private void RefreshBookmarks()
    {
        string selected=(bookmarkTree.SelectedNode?.Tag as UserBookmark)?.id;
        var known=new HashSet<string>();var expanded=new HashSet<string>();Action<TreeNodeCollection> remember=null;
        remember=nodes=>{foreach(TreeNode n in nodes){var id=(n.Tag as UserBookmark)?.id;if(id!=null){known.Add(id);if(n.IsExpanded)expanded.Add(id);}remember(n.Nodes);}};remember(bookmarkTree.Nodes);
        bookmarkTree.BeginUpdate(); bookmarkTree.Nodes.Clear();
        if(navigationSession!=null)
        {
            var all=navigationSession.Record.bookmarks ?? new List<UserBookmark>();
            var added=new HashSet<UserBookmark>();
            Action<UserBookmark,TreeNodeCollection,int> add=null;
            add=(b,nodes,depth)=>
            {
                if(depth>64 || !added.Add(b))return;
                TreeNode n=new TreeNode((b.title??"未命名书签")+"  · "+(b.pageIndex+1)) { Tag=b, ToolTipText="PDF 第 "+(b.pageIndex+1)+" 页，缩放 "+Math.Round(b.zoom*100)+"%" };
                if(b.pageIndex<0 || b.pageIndex>=navigationSession.PageCount)n.ForeColor=Color.Gray;
                nodes.Add(n); if(b.id==selected)bookmarkTree.SelectedNode=n;
                if(!String.IsNullOrEmpty(b.id)) foreach(var c in all.Where(c=>c.parentId==b.id))add(c,n.Nodes,depth+1);
            };
            foreach(var b in all.Where(b=>String.IsNullOrEmpty(b.parentId) || !all.Any(p=>p.id==b.parentId))) add(b,bookmarkTree.Nodes,0);
            foreach(var b in all.Where(b=>!added.Contains(b)).ToArray())add(b,bookmarkTree.Nodes,0);
        }
        Action<TreeNodeCollection> restore=null;restore=nodes=>{foreach(TreeNode n in nodes){var id=(n.Tag as UserBookmark)?.id;if(!known.Contains(id)||expanded.Contains(id))n.Expand();restore(n.Nodes);}};restore(bookmarkTree.Nodes);
        bookmarkTree.EndUpdate();UpdateBookmarkDeleteButton();
    }
    private void CaptureBookmark(DocumentSession s,UserBookmark b)
    {
        int page;double offset;s.Viewer.CaptureReadingPosition(out page,out offset);
        b.pageIndex=page;b.pageOffset=offset;b.horizontalOffset=s.Viewer.CaptureHorizontalPosition();b.zoom=s.Viewer.Zoom;
    }
    private void AddBookmark(bool child)
    {
        var s=ActiveSession;if(s==null)return;
        var parent=child ? bookmarkTree.SelectedNode?.Tag as UserBookmark : null;
        var b=new UserBookmark { id=Guid.NewGuid().ToString("N"),title="第 "+(s.Viewer.CurrentPage+1)+" 页",parentId=parent?.id,createdUtc=DateTime.UtcNow.ToString("o") };
        CaptureBookmark(s,b);s.Record.bookmarks.Add(b);MarkDirty(s);SaveSession(s);
        ShowNavigation(1); RefreshBookmarks();
        var found=FindBookmarkNode(bookmarkTree.Nodes,b.id);bookmarkTree.SelectedNode=found;found?.Parent?.Expand();found?.EnsureVisible();
    }
    private static TreeNode FindBookmarkNode(TreeNodeCollection nodes,string id)
    {
        foreach(TreeNode n in nodes) { if((n.Tag as UserBookmark)?.id==id)return n;var child=FindBookmarkNode(n.Nodes,id);if(child!=null)return child; }
        return null;
    }
    private void ActivateBookmark(TreeNode node)
    {
        var b=node?.Tag as UserBookmark;
        if(b==null)return;
        NavigateTarget(ActiveSession,new NavigationTarget { PageIndex=b.pageIndex,PageOffset=b.pageOffset,HorizontalOffset=b.horizontalOffset,Zoom=b.zoom>0?(float?)b.zoom:null });
    }
    private void UpdateBookmarkPosition()
    {
        var s=ActiveSession;var b=bookmarkTree.SelectedNode?.Tag as UserBookmark;if(s==null || b==null)return;
        CaptureBookmark(s,b);MarkDirty(s);SaveSession(s);RefreshBookmarks();
    }
    private void DeleteSelectedBookmark()
    {
        var s=ActiveSession;var b=bookmarkTree.SelectedNode?.Tag as UserBookmark;if(s==null || b==null)return;
        if(s.Record.bookmarks.Any(x=>x.parentId==b.id))
        {
            string question="删除书签“"+b.title+"”及其所有子书签？";
            bool confirmed=BookmarkDeleteConfirmationOverride!=null?BookmarkDeleteConfirmationOverride(question):MessageBox.Show(this,question,"删除书签",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes;
            if(!confirmed)return;
        }
        RemoveBookmarkTree(s,b);MarkDirty(s);SaveSession(s);RefreshBookmarks();
    }
    internal static void RemoveBookmarkTree(DocumentSession s,UserBookmark b)
    {
        var remove=new HashSet<UserBookmark> { b };bool changed;
        do { changed=false;foreach(var item in s.Record.bookmarks) if(!remove.Contains(item) && !String.IsNullOrEmpty(item.parentId) && remove.Any(p=>p.id==item.parentId))changed|=remove.Add(item); }while(changed);
        s.Record.bookmarks.RemoveAll(x=>remove.Contains(x));
    }
}
