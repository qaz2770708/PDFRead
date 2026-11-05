using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal sealed partial class PdfReaderForm
{
    private readonly SplitContainer navigationSplit = new SplitContainer { Dock = DockStyle.Fill, Width = 1000, SplitterDistance = 260, Panel1MinSize = 180, Panel2MinSize = 300 };
    private readonly TabControl navigationTabs = new TabControl { Dock = DockStyle.Fill };
    private readonly TreeView outlineTree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, ShowNodeToolTips = true };
    private readonly TreeView bookmarkTree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, LabelEdit = true, ShowNodeToolTips = true };
    private readonly Label outlineMessage = new Label { Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(260,0), Padding = new Padding(6) };
    private readonly ToolStrip bookmarkTools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
    private DocumentSession navigationSession;

    private void InitializeNavigation()
    {
        navigationSplit.Panel2.Controls.Add(documentTabs);
        navigationSplit.Panel1.Controls.Add(navigationTabs);
        TabPage pdf = new TabPage("PDF 目录");
        pdf.Controls.Add(outlineTree); pdf.Controls.Add(outlineMessage);
        TabPage user = new TabPage("用户书签");
        user.Controls.Add(bookmarkTree); user.Controls.Add(bookmarkTools);
        navigationTabs.TabPages.Add(pdf); navigationTabs.TabPages.Add(user);
        AddBookmarkTool("添加", "添加当前位置 (Ctrl+D)", () => AddBookmark(false));
        AddBookmarkTool("子级", "在选中书签下添加子书签", () => AddBookmark(true));
        AddBookmarkTool("改名", "重命名 (F2)", () => { if(bookmarkTree.SelectedNode != null) bookmarkTree.SelectedNode.BeginEdit(); });
        AddBookmarkTool("更新位置", "把选中书签定位更新为当前阅读位置", UpdateBookmarkPosition);
        AddBookmarkTool("删除", "删除选中书签及子级 (Delete)", DeleteSelectedBookmark);
        AddBookmarkTool("↑", "同级上移", () => MoveBookmark(-1));
        AddBookmarkTool("↓", "同级下移", () => MoveBookmark(1));
        AddBookmarkTool("→", "降一级：移入前一个同级书签", () => ChangeBookmarkLevel(true));
        AddBookmarkTool("←", "升一级：移到上一级", () => ChangeBookmarkLevel(false));
        outlineTree.NodeMouseClick += delegate(object sender, TreeNodeMouseClickEventArgs e) { if(e.Button == MouseButtons.Left) ActivateOutline(e.Node); };
        outlineTree.KeyDown += delegate(object sender, KeyEventArgs e) { if(e.KeyCode==Keys.Enter) { ActivateOutline(outlineTree.SelectedNode); e.SuppressKeyPress=true; } };
        bookmarkTree.NodeMouseClick += delegate(object sender, TreeNodeMouseClickEventArgs e) { if(e.Button==MouseButtons.Left) ActivateBookmark(e.Node); };
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
        session.Viewer.GoToPage(target.PageIndex,0);
        if(target.Zoom.HasValue) session.Viewer.SetZoom(target.Zoom.Value);
        else if(target.Fit!=null) session.Viewer.FitPage(target.Fit=="width");
        session.Viewer.GoToLocation(target.PageIndex,target.PageOffset,target.HorizontalOffset);
        session.Viewer.Focus();
    }
    private void RefreshBookmarks()
    {
        string selected=(bookmarkTree.SelectedNode?.Tag as UserBookmark)?.id;
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
        bookmarkTree.ExpandAll();bookmarkTree.EndUpdate();
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
        navigationTabs.SelectedIndex=1; RefreshBookmarks();
        var found=FindBookmarkNode(bookmarkTree.Nodes,b.id);bookmarkTree.SelectedNode=found;found?.EnsureVisible();
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
        if(MessageBox.Show(this,"删除书签“"+b.title+"”及其所有子书签？","删除书签",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        RemoveBookmarkTree(s,b);MarkDirty(s);SaveSession(s);RefreshBookmarks();
    }
    internal static void RemoveBookmarkTree(DocumentSession s,UserBookmark b)
    {
        var remove=new HashSet<UserBookmark> { b };bool changed;
        do { changed=false;foreach(var item in s.Record.bookmarks) if(!remove.Contains(item) && !String.IsNullOrEmpty(item.parentId) && remove.Any(p=>p.id==item.parentId))changed|=remove.Add(item); }while(changed);
        s.Record.bookmarks.RemoveAll(x=>remove.Contains(x));
    }
    private void ChangeBookmarkLevel(bool indent)
    {
        var s=ActiveSession;var b=bookmarkTree.SelectedNode?.Tag as UserBookmark;if(s==null || b==null)return;
        if(indent)
        {
            var siblings=s.Record.bookmarks.Where(x=>x.parentId==b.parentId).ToList();int i=siblings.IndexOf(b);
            if(i<=0)return;b.parentId=siblings[i-1].id;
        }
        else
        {
            var parent=s.Record.bookmarks.FirstOrDefault(x=>x.id==b.parentId);if(parent==null)return;b.parentId=parent.parentId;
        }
        MarkDirty(s);SaveSession(s);RefreshBookmarks();
    }
    private void MoveBookmark(int direction)
    {
        var s=ActiveSession;var b=bookmarkTree.SelectedNode?.Tag as UserBookmark;if(s==null || b==null)return;
        var siblings=s.Record.bookmarks.Where(x=>x.parentId==b.parentId).ToList();int i=siblings.IndexOf(b),next=i+direction;
        if(next<0 || next>=siblings.Count)return;
        int a=s.Record.bookmarks.IndexOf(b),c=s.Record.bookmarks.IndexOf(siblings[next]);
        s.Record.bookmarks[a]=siblings[next];s.Record.bookmarks[c]=b;
        MarkDirty(s);SaveSession(s);RefreshBookmarks();
    }
}
