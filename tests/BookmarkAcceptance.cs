using System;using System.IO;using System.Linq;using System.Drawing;using System.Collections.Generic;using System.Reflection;using System.Windows.Forms;
internal static class BookmarkAcceptance
{
 static object Field(object o,string n){return o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);}
 static object Call(object o,string n,params object[] args){return o.GetType().GetMethod(n,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,args);}
 static void Check(bool ok,string m){if(!ok)throw new Exception(m);Console.WriteLine("PASS "+m);}
 static TreeNode Node(TreeView t,string id){foreach(TreeNode n in t.Nodes){var found=Find(n,id);if(found!=null)return found;}return null;}
 static TreeNode Find(TreeNode n,string id){if(((UserBookmark)n.Tag).id==id)return n;foreach(TreeNode c in n.Nodes){var f=Find(c,id);if(f!=null)return f;}return null;}
 static void Right(PdfReaderForm f,TreeView t,Point p){Call(t,"OnMouseDown",new MouseEventArgs(MouseButtons.Right,1,p.X,p.Y,0));var menu=(ContextMenuStrip)Field(f,"bookmarkMenu");menu.Show(t,p);}
 static void Menu(PdfReaderForm f,string text){var menu=(ContextMenuStrip)Field(f,"bookmarkMenu");menu.Items.OfType<ToolStripMenuItem>().Single(m=>m.Text==text).PerformClick();menu.Close();}
 static DragEventArgs Drag(PdfReaderForm f,DocumentSession s,TreeView t,string id,Point local){var p=t.PointToScreen(local);return new DragEventArgs(f.CreateBookmarkDragData(s,id),1,p.X,p.Y,DragDropEffects.Move,DragDropEffects.None);}
 [STAThread]static int Main(string[] args)
 {
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  string folder=Path.GetFullPath(Path.Combine("work","bookmark-m6-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(folder);DocumentDataRepository.DataRootOverride=Path.Combine(folder,"data");string pdf=Path.Combine(folder,"bookmarks.pdf");File.Copy(args[0],pdf);string hash=DocumentDataRepository.CalculateSha256(pdf);
  var context=new ReaderApplicationContext(new[]{pdf});var form=context.Windows[0];var timer=new Timer{Interval=1000};int stage=0;string[] order=null;
  timer.Tick+=delegate
  {
   timer.Stop();try
   {
    var s=((List<DocumentSession>)Field(form,"sessions")).Single();var tree=(TreeView)Field(form,"bookmarkTree");
    if(stage==0)
    {
     Call(form,"ShowNavigation",1);var tools=(ToolStrip)Field(form,"bookmarkTools");Check(tools.Items.Count==2 && tools.Items[0].Text=="添加"&&tools.Items[1].Text=="删除","only add and delete buttons remain");
     foreach(var item in new[]{new UserBookmark{id="A",title="章节 A",pageIndex=0,zoom=1},new UserBookmark{id="B",title="章节 B",pageIndex=1,zoom=1.2f},new UserBookmark{id="C",title="章节 C",pageIndex=2,zoom=1},new UserBookmark{id="child",parentId="A",title="A 的子书签",pageIndex=3,zoom=1}})s.Record.bookmarks.Add(item);
     Call(form,"MarkDirty",s);Call(form,"SaveSession",s);Call(form,"RefreshBookmarks");s.Viewer.GoToPage(0,0);
     var b=Node(tree,"B");Right(form,tree,new Point(b.Bounds.Left+10,b.Bounds.Top+b.Bounds.Height/2));Check(((UserBookmark)tree.SelectedNode.Tag).id=="B"&&s.Viewer.CurrentPage==0,"right click selects target without navigating");Menu(form,"添加子书签");Check(s.Record.bookmarks.Last().parentId=="B"&&s.Viewer.CurrentPage==0,"right menu adds child at current reading position");string added=((UserBookmark)tree.SelectedNode.Tag).id;
     Right(form,tree,new Point(tree.SelectedNode.Bounds.Left+5,tree.SelectedNode.Bounds.Top+5));Menu(form,"重命名");Check(tree.SelectedNode.IsEditing,"context rename opens native inline label edit");tree.SelectedNode.EndEdit(true);Call(tree,"OnAfterLabelEdit",new NodeLabelEditEventArgs(tree.SelectedNode,"重命名后的书签"));Application.DoEvents();Check(s.Record.bookmarks.Single(x=>x.id==added).title=="重命名后的书签","rename persisted");
     Right(form,tree,new Point(15,tree.ClientSize.Height-12));var menu=(ContextMenuStrip)Field(form,"bookmarkMenu");Check(tree.SelectedNode==null&&!menu.Items.OfType<ToolStripMenuItem>().Single(m=>m.Text=="删除").Enabled,"blank-area menu clears stale selection and disables node actions");Menu(form,"添加当前位置书签");Check(String.IsNullOrEmpty(s.Record.bookmarks.Last().parentId),"blank area adds root bookmark");string root=((UserBookmark)tree.SelectedNode.Tag).id;
     var a=Node(tree,"A");var drag=Drag(form,s,tree,"C",new Point(a.Bounds.Left+10,a.Bounds.Top+1));Call(tree,"OnDragOver",drag);Check(drag.Effect==DragDropEffects.Move,"real drag-over accepts preceding insertion");Call(tree,"OnDragDrop",drag);Check(tree.Nodes[0].Tag==s.Record.bookmarks.First(x=>x.id=="C"),"drop before reorders root bookmarks");
     b=Node(tree,"B");drag=Drag(form,s,tree,"A",new Point(b.Bounds.Left+10,b.Bounds.Top+b.Bounds.Height/2));Call(tree,"OnDragOver",drag);Call(tree,"OnDragDrop",drag);Check(Node(tree,"A").Parent==Node(tree,"B")&&Node(tree,"child").Parent==Node(tree,"A"),"dragging parent into another bookmark preserves its subtree");
     a=Node(tree,"child");drag=Drag(form,s,tree,"B",new Point(a.Bounds.Left+10,a.Bounds.Top+a.Bounds.Height/2));Call(tree,"OnDragOver",drag);Check(drag.Effect==DragDropEffects.None&&!form.TryMoveBookmark("B","child",BookmarkDropPosition.Inside),"cannot drop a bookmark into its descendant");
     drag=Drag(form,s,tree,"A",new Point(10,tree.ClientSize.Height-8));Call(tree,"OnDragDrop",drag);Check(Node(tree,"A").Parent==null && Node(tree,"child").Parent==Node(tree,"A"),"drop on empty space moves subtree back to root");
     Check(form.TryMoveBookmark("A","C",BookmarkDropPosition.After)&&tree.Nodes[1].Tag==s.Record.bookmarks.First(x=>x.id=="A"),"drop after sibling keeps requested order");
     var wrong=new DocumentSession();drag=Drag(form,wrong,tree,"A",new Point(10,10));Call(tree,"OnDragOver",drag);Check(drag.Effect==DragDropEffects.None,"drag from another document is rejected");
     var c=Node(tree,"C");drag=Drag(form,s,tree,"C",new Point(c.Bounds.Left+10,c.Bounds.Top+c.Bounds.Height/2));Call(tree,"OnDragOver",drag);Check(drag.Effect==DragDropEffects.None,"self-drop is rejected");
     a=Node(tree,"A");a.Collapse();Call(form,"RefreshBookmarks");Check(!Node(tree,"A").IsExpanded,"refresh retains collapsed state");
     var leaf=Node(tree,root);Right(form,tree,new Point(leaf.Bounds.Left+5,leaf.Bounds.Top+5));Menu(form,"删除");Check(!s.Record.bookmarks.Any(x=>x.id==root),"context delete removes leaf immediately");
     form.BookmarkDeleteConfirmationOverride=q=>false;a=Node(tree,"A");Right(form,tree,new Point(a.Bounds.Left+5,a.Bounds.Top+5));Menu(form,"删除");Check(s.Record.bookmarks.Any(x=>x.id=="child"),"cancelling subtree deletion preserves children");
     form.BookmarkDeleteConfirmationOverride=q=>true;Right(form,tree,new Point(a.Bounds.Left+5,a.Bounds.Top+5));Menu(form,"删除");Check(!s.Record.bookmarks.Any(x=>x.id=="A"||x.id=="child"),"confirmed context deletion removes subtree");
     var last=Node(tree,added);s.Viewer.GoToPage(2,.1);Right(form,tree,new Point(last.Bounds.Left+5,last.Bounds.Top+5));Menu(form,"更新为当前阅读位置");Check(s.Record.bookmarks.Single(x=>x.id==added).pageIndex==2,"context update captures current position");
     order=s.Record.bookmarks.Select(x=>x.id+":"+x.parentId).ToArray();s.Viewer.RenderPagesNearViewport();using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(folder,"bookmark-browser-ui.png"));}
     var keep=context.CreateWindow(new string[0]);form.Close();form=keep;Call(form,"OpenPdf",pdf);stage=1;timer.Start();return;
    }
    Check(order.SequenceEqual(s.Record.bookmarks.Select(x=>x.id+":"+x.parentId)),"drag order hierarchy rename and deletion survive reopening");Check(DocumentDataRepository.CalculateSha256(pdf)==hash,"bookmark operations never change PDF");Console.WriteLine("BOOKMARK_ACCEPTANCE_PASS "+folder);foreach(var w in context.Windows.ToArray())w.Close();
   }catch(Exception e){Console.WriteLine(e);Environment.Exit(1);}
  };timer.Start();Application.Run(context);timer.Dispose();return 0;
 }
}
