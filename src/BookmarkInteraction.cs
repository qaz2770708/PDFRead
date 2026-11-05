using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

internal enum BookmarkDropPosition { Before,Inside,After,Root }
internal sealed class BookmarkDragData
{
    internal DocumentSession Session;
    internal string Id;
}
internal sealed class BookmarkTreeView : TreeView
{
    internal TreeNode DropTarget;
    internal bool RootDropActive;
    internal BookmarkDropPosition DropPosition;
    internal void ClearDrop(){DropTarget=null;RootDropActive=false;Invalidate();}
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if(m.Msg!=0x000F || (DropTarget==null&&!RootDropActive) || !IsHandleCreated)return;
        using(var g=Graphics.FromHwnd(Handle))using(var pen=new Pen(Color.RoyalBlue,2))
        {
            if(RootDropActive)
            {
                TreeNode last=Nodes.Count>0?Nodes[Nodes.Count-1]:null;
                while(last!=null&&last.IsExpanded&&last.Nodes.Count>0)last=last.Nodes[last.Nodes.Count-1];
                int y=Math.Max(3,Math.Min(ClientSize.Height-4,last==null?4:last.Bounds.Bottom+3));
                g.DrawLine(pen,8,y,ClientSize.Width-6,y);return;
            }
            var rect=DropTarget.Bounds;
            if(DropPosition==BookmarkDropPosition.Inside){pen.DashStyle=DashStyle.Dash;g.DrawRectangle(pen,Math.Max(2,rect.Left-3),rect.Top,Math.Max(1,ClientSize.Width-rect.Left-8),Math.Max(1,rect.Height-1));}
            else{int y=DropPosition==BookmarkDropPosition.Before?rect.Top:rect.Bottom;g.DrawLine(pen,Math.Max(8,rect.Left),y,ClientSize.Width-6,y);}
        }
    }
}
internal sealed partial class PdfReaderForm
{
    private const string BookmarkDragFormat="PDFRead.BookmarkMove.v1";
    private string bookmarkDragToken;
    private BookmarkDragData bookmarkDragModel;
    internal IDataObject CreateBookmarkDragData(DocumentSession session,string id)
    {
        bookmarkDragToken=Guid.NewGuid().ToString("N");bookmarkDragModel=new BookmarkDragData{Session=session,Id=id};
        return new DataObject(BookmarkDragFormat,bookmarkDragToken);
    }
    private BookmarkDragData ReadBookmarkDrag(IDataObject data)
    {
        try{return data!=null && data.GetDataPresent(BookmarkDragFormat) && data.GetData(BookmarkDragFormat) as string==bookmarkDragToken?bookmarkDragModel:null;}
        catch(Exception ex){System.Diagnostics.Debug.WriteLine(ex.Message);return null;}
    }
    private readonly ContextMenuStrip bookmarkMenu=new ContextMenuStrip();
    private ToolStripMenuItem bookmarkAddChild,bookmarkRename,bookmarkDelete,bookmarkUpdate;
    private bool bookmarkDragged;
    private DateTime bookmarkHoverSince,bookmarkLastScroll;
    private TreeNode bookmarkHoverNode;
    internal Func<string,bool> BookmarkDeleteConfirmationOverride=null;
    private void InitializeBookmarkInteraction()
    {
        bookmarkTree.AllowDrop=true;bookmarkTree.FullRowSelect=true;bookmarkTree.ShowLines=false;
        bookmarkTree.AccessibleDescription="拖动书签：上/下边缘排序，中间移入子级，空白处移到根级；右键管理书签。";
        bookmarkMenu.Items.Add("添加当前位置书签",null,delegate{AddBookmark(false);});
        bookmarkAddChild=new ToolStripMenuItem("添加子书签",null,delegate{AddBookmark(true);});
        bookmarkRename=new ToolStripMenuItem("重命名",null,delegate{bookmarkTree.SelectedNode?.BeginEdit();}){ShortcutKeyDisplayString="F2"};
        bookmarkDelete=new ToolStripMenuItem("删除",null,delegate{DeleteSelectedBookmark();}){ShortcutKeyDisplayString="Delete"};
        bookmarkUpdate=new ToolStripMenuItem("更新为当前阅读位置",null,delegate{UpdateBookmarkPosition();});
        bookmarkMenu.Items.AddRange(new ToolStripItem[]{bookmarkAddChild,new ToolStripSeparator(),bookmarkRename,bookmarkDelete,bookmarkUpdate});
        bookmarkMenu.Opening+=delegate(System.Object sender,System.ComponentModel.CancelEventArgs e)
        {
            if(ActiveSession==null){e.Cancel=true;return;}
            bool selected=bookmarkTree.SelectedNode?.Tag is UserBookmark;
            bookmarkAddChild.Enabled=bookmarkRename.Enabled=bookmarkDelete.Enabled=bookmarkUpdate.Enabled=selected;
        };
        bookmarkTree.ContextMenuStrip=bookmarkMenu;
        bookmarkTree.MouseDown+=delegate(object sender,MouseEventArgs e)
        {
            if(e.Button==MouseButtons.Left)bookmarkDragged=false;
            if(e.Button==MouseButtons.Right){bookmarkTree.SelectedNode=bookmarkTree.GetNodeAt(e.Location);UpdateBookmarkDeleteButton();bookmarkTree.Focus();}
        };
        bookmarkTree.KeyDown+=delegate(object sender,KeyEventArgs e)
        {
            if(e.KeyCode==Keys.Apps || (e.Shift&&e.KeyCode==Keys.F10)){bookmarkMenu.Show(bookmarkTree,bookmarkTree.SelectedNode==null?new Point(10,10):new Point(bookmarkTree.SelectedNode.Bounds.Left,bookmarkTree.SelectedNode.Bounds.Bottom));e.SuppressKeyPress=true;}
        };
        bookmarkTree.AfterSelect+=delegate{UpdateBookmarkDeleteButton();};
        bookmarkTree.ItemDrag+=delegate(object sender,ItemDragEventArgs e)
        {
            var node=e.Item as TreeNode;var mark=node?.Tag as UserBookmark;var s=ActiveSession;
            if(e.Button!=MouseButtons.Left||s==null||mark==null)return;
            node.EndEdit(false);bookmarkTree.SelectedNode=node;bookmarkDragged=true;
            try{bookmarkTree.DoDragDrop(CreateBookmarkDragData(s,mark.id),DragDropEffects.Move);}
            finally{bookmarkTree.ClearDrop();bookmarkHoverNode=null;bookmarkDragModel=null;bookmarkDragToken=null;}
        };
        bookmarkTree.DragEnter+=BookmarkDragOver;bookmarkTree.DragOver+=BookmarkDragOver;
        bookmarkTree.DragLeave+=delegate{bookmarkTree.ClearDrop();bookmarkHoverNode=null;};
        bookmarkTree.DragDrop+=delegate(object sender,DragEventArgs e)
        {
            var data=ReadBookmarkDrag(e.Data);
            var point=bookmarkTree.PointToClient(new Point(e.X,e.Y));var target=bookmarkTree.GetNodeAt(point);var position=BookmarkPlacement(target,point);
            bookmarkTree.ClearDrop();bookmarkHoverNode=null;
            if(data==null||data.Session!=ActiveSession)return;
            var targetId=(target?.Tag as UserBookmark)?.id;
            TryMoveBookmark(data.Id,targetId,position);bookmarkDragged=true;
        };
    }
    private void UpdateBookmarkDeleteButton()
    {
        if(bookmarkTools.Items.Count==2)bookmarkTools.Items[1].Enabled=ActiveSession!=null && bookmarkTree.SelectedNode!=null;
    }
    private static BookmarkDropPosition BookmarkPlacement(TreeNode node,Point point)
    {
        if(node==null)return BookmarkDropPosition.Root;
        int margin=Math.Max(4,node.Bounds.Height/4);
        return point.Y<node.Bounds.Top+margin?BookmarkDropPosition.Before:point.Y>=node.Bounds.Bottom-margin?BookmarkDropPosition.After:BookmarkDropPosition.Inside;
    }
    private static string EmptyParent(string parent){return String.IsNullOrEmpty(parent)?null:parent;}
    private bool CanMoveBookmark(string id,string targetId,BookmarkDropPosition position)
    {
        var all=ActiveSession?.Record.bookmarks;if(all==null)return false;
        var source=all.FirstOrDefault(b=>b.id==id);var target=all.FirstOrDefault(b=>b.id==targetId);
        if(source==null || (position!=BookmarkDropPosition.Root && target==null))return false;
        if(target==source)return false;
        for(var parent=target;parent!=null;parent=all.FirstOrDefault(b=>b.id==parent.parentId))if(parent==source)return false;
        string parentId=position==BookmarkDropPosition.Root?null:position==BookmarkDropPosition.Inside?target.id:EmptyParent(target.parentId);
        int depth=0;for(var parent=all.FirstOrDefault(b=>b.id==parentId);parent!=null;parent=all.FirstOrDefault(b=>b.id==parent.parentId))if(++depth>63)return false;
        Func<string,int,int> height=null;height=(key,limit)=>limit<=0?65:1+all.Where(b=>b.parentId==key).Select(b=>height(b.id,limit-1)).DefaultIfEmpty(0).Max();
        return depth+height(source.id,64)<=64;
    }
    private void BookmarkDragOver(object sender,DragEventArgs e)
    {
        var data=ReadBookmarkDrag(e.Data);var point=bookmarkTree.PointToClient(new Point(e.X,e.Y));var target=bookmarkTree.GetNodeAt(point);var position=BookmarkPlacement(target,point);
        bool valid=data!=null && data.Session==ActiveSession && CanMoveBookmark(data.Id,(target?.Tag as UserBookmark)?.id,position);
        e.Effect=valid?DragDropEffects.Move:DragDropEffects.None;
        bookmarkTree.DropTarget=valid?target:null;bookmarkTree.RootDropActive=valid&&position==BookmarkDropPosition.Root;bookmarkTree.DropPosition=position;bookmarkTree.Invalidate();
        if(!valid)return;
        if(bookmarkHoverNode!=target){bookmarkHoverNode=target;bookmarkHoverSince=DateTime.UtcNow;}
        else if(target!=null && position==BookmarkDropPosition.Inside && (DateTime.UtcNow-bookmarkHoverSince).TotalMilliseconds>650)target.Expand();
        if((DateTime.UtcNow-bookmarkLastScroll).TotalMilliseconds>100)
        {
            if(point.Y<24)bookmarkTree.TopNode=bookmarkTree.TopNode?.PrevVisibleNode??bookmarkTree.TopNode;
            else if(point.Y>bookmarkTree.ClientSize.Height-24)bookmarkTree.TopNode=bookmarkTree.TopNode?.NextVisibleNode??bookmarkTree.TopNode;
            bookmarkLastScroll=DateTime.UtcNow;
        }
    }
    internal bool TryMoveBookmark(string id,string targetId,BookmarkDropPosition position)
    {
        if(!CanMoveBookmark(id,targetId,position))return false;
        var s=ActiveSession;var all=s.Record.bookmarks;var source=all.First(b=>b.id==id);var target=all.FirstOrDefault(b=>b.id==targetId);
        string oldParent=source.parentId,parent=position==BookmarkDropPosition.Root?null:position==BookmarkDropPosition.Inside?target.id:EmptyParent(target.parentId);
        var order=all.Select(b=>b.id+":"+b.parentId).ToArray();
        var siblings=all.Where(b=>b!=source&&EmptyParent(b.parentId)==parent).ToList();int index=siblings.Count;
        if(position==BookmarkDropPosition.Before || position==BookmarkDropPosition.After)index=siblings.IndexOf(target)+(position==BookmarkDropPosition.After?1:0);
        siblings.Insert(index,source);source.parentId=parent;
        var sorted=new List<UserBookmark>();Action<string> append=null;
        append=key=>{foreach(var b in (key==parent?siblings:all.Where(b=>EmptyParent(b.parentId)==key).ToList())){sorted.Add(b);append(b.id);}};append(null);
        if(sorted.Count!=all.Count){source.parentId=oldParent;return false;}
        if(order.SequenceEqual(sorted.Select(b=>b.id+":"+b.parentId)))return false;
        all.Clear();all.AddRange(sorted);MarkDirty(s);SaveSession(s);RefreshBookmarks();
        var moved=FindBookmarkNode(bookmarkTree.Nodes,id);bookmarkTree.SelectedNode=moved;
        if(position==BookmarkDropPosition.Inside)moved?.Parent?.Expand();moved?.EnsureVisible();UpdateBookmarkDeleteButton();return true;
    }
}
