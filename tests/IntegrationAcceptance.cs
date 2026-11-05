using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using System.Web.Script.Serialization;
internal static class IntegrationAcceptance
{
    static void Check(bool ok,string message) { if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message); }
    static object Field(object obj,string name) { return obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj); }
    static object Call(object obj,string name,params object[] args) { return obj.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,args); }
    static void Press(PdfReaderForm f,Keys key) { object[] args={new Message(),key};Check((bool)Call(f,"ProcessCmdKey",args),"key "+key); }
    [STAThread] static int Main(string[] args)
    {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        string dir=Path.GetFullPath(Path.Combine("work","acceptance-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(dir);
        string path=Path.Combine(dir,"navigation.pdf"),other=Path.Combine(dir,"other.pdf");File.Copy(args[0],path);File.Copy(args[0],other);
        string hash=DocumentDataRepository.CalculateSha256(path);
        try
        {
            var nav=PdfNavigationReader.Read(path);
            Check(nav.Error==null,"PDF metadata parses: "+nav.Error);
            Check(nav.Outline.Count==2 && nav.Outline[0].Children.Count==3,"outline hierarchy");
            Check(nav.Outline[0].PageIndex==1 && Math.Abs(nav.Outline[0].PageOffset-0.25)<0.01,"direct XYZ page and coordinates");
            Check(nav.Outline[0].Children[0].PageIndex==0,"legacy named destination");
            Check(nav.Outline[1].PageIndex==3,"name tree GoTo destination");
            Check(nav.Outline[0].Children[1].PageIndex==2 && Math.Abs(nav.Outline[0].Children[1].PageOffset-100.0/560)<0.01,"indirect destination rotated crop coordinates");
            Check(nav.Outline[0].Children[2].PageIndex==-1,"external target has no false page fallback");
            Check(nav.Links.Count==1 && nav.Links[0].Target.PageIndex==3,"page link resolves named destination");
            var legacy=new SidecarFile { formatVersion=2 };
            legacy.versions.Add(new VersionRecord {sourceHash=hash,pageCount=4,zoom=1f});
            File.WriteAllText(Path.ChangeExtension(other,".pdfdata"),new JavaScriptSerializer().Serialize(legacy));
            var migrated=DocumentDataRepository.LoadForPdf(other,hash,4,null,(a,b)=>DialogResult.Yes);
            Check(migrated.NeedsSave && migrated.Data.formatVersion==3 && migrated.Record.bookmarks.Count==0,"v2 to v3 migration");
        }
        catch(Exception e) { Console.WriteLine(e);return 1; }
        var form=new PdfReaderForm(new[]{path,other});
        var timer=new Timer {Interval=1200};
        timer.Tick+=delegate
        {
            timer.Stop();
            try
            {
                var sessions=(List<DocumentSession>)Field(form,"sessions");Check(sessions.Count==2,"multiple tabs open");
                Press(form,Keys.Control|Keys.Tab);var s=sessions[0];var viewer=s.Viewer;
                var tabs=(TabControl)Field(form,"documentTabs");Check(tabs.SelectedTab==s.Tab,"next tab shortcut");
                Press(form,Keys.Control|Keys.Shift|Keys.Tab);Check(tabs.SelectedTab==sessions[1].Tab,"previous tab shortcut");
                Press(form,Keys.Control|Keys.Tab);
                var split=(SplitContainer)Field(form,"navigationSplit");bool collapsed=split.Panel1Collapsed;
                Press(form,Keys.Control|Keys.B);Check(split.Panel1Collapsed!=collapsed,"toggle navigation sidebar");Press(form,Keys.Control|Keys.B);
                Press(form,Keys.Control|Keys.G);var input=(ToolStripTextBox)Field(form,"pageBox");Check(input.TextBox.Focused,"page number input focus");
                input.Text="3";Call(form,"PageBoxKeyDown",input,new KeyEventArgs(Keys.Enter));Check(viewer.CurrentPage==2,"typed page number jump");
                Press(form,Keys.Control|Keys.D0);Check(Math.Abs(viewer.Zoom-1)<0.001,"100 percent shortcut");
                Press(form,Keys.Control|Keys.Oemplus);Check(Math.Abs(viewer.Zoom-1.1)<0.001,"zoom in shortcut");
                Press(form,Keys.Control|Keys.Subtract);Check(Math.Abs(viewer.Zoom-1)<0.001,"keypad zoom out");
                viewer.GoToPage(1,0.2);Point anchor=new Point(viewer.ClientSize.Width/2,240);Rectangle before=viewer.PageRectangles[1];
                double y=(anchor.Y-viewer.AutoScrollPosition.Y-before.Top)/(double)before.Height;
                var e=new HandledMouseEventArgs(MouseButtons.None,0,anchor.X,anchor.Y,120);viewer.HandleZoomWheel(e,Keys.Control);
                Rectangle after=viewer.PageRectangles[1];double y2=(anchor.Y-viewer.AutoScrollPosition.Y-after.Top)/(double)after.Height;
                Check(e.Handled && Math.Abs(viewer.Zoom-1.1)<0.001 && Math.Abs(y-y2)<0.003,"Ctrl wheel consumes scroll and preserves anchor");
                viewer.HandleZoomWheel(new MouseEventArgs(MouseButtons.None,0,anchor.X,anchor.Y,60),Keys.Control);
                Check(Math.Abs(viewer.Zoom-1.1)<0.001,"precision wheel partial delta");
                viewer.HandleZoomWheel(new MouseEventArgs(MouseButtons.None,0,anchor.X,anchor.Y,60),Keys.Control);
                Check(Math.Abs(viewer.Zoom-1.2)<0.001,"precision wheel accumulation");
                viewer.HandleZoomWheel(new MouseEventArgs(MouseButtons.None,0,anchor.X,anchor.Y,12000),Keys.Control);Check(Math.Abs(viewer.Zoom-2.2)<0.001,"upper zoom bound");
                viewer.HandleZoomWheel(new MouseEventArgs(MouseButtons.None,0,anchor.X,anchor.Y,-12000),Keys.Control);Check(Math.Abs(viewer.Zoom-.35)<0.001,"lower zoom bound");
                Press(form,Keys.Control|Keys.End);Check(viewer.CurrentPage==3,"last page at minimum zoom");
                Press(form,Keys.Control|Keys.Home);Check(viewer.CurrentPage==0,"first page shortcut");
                Press(form,Keys.Control|Keys.D1);Press(form,Keys.Control|Keys.D2);
                Press(form,Keys.Control|Keys.Shift|Keys.H);Check(viewer.HighlightMode,"highlight shortcut");Press(form,Keys.Escape);Check(!viewer.HighlightMode,"Escape reading mode");
                viewer.SetZoom(1f);viewer.GoToPage(0,0);
                float normalZoom=viewer.Zoom;
                for(int tick=0;tick<12;tick++) { Call(viewer,"OnMouseWheel",new MouseEventArgs(MouseButtons.None,0,200,200,-120)); Application.DoEvents(); }
                Check(viewer.CurrentPage>0 && Math.Abs(viewer.Zoom-normalZoom)<.001 && ((ToolStripTextBox)Field(form,"pageBox")).Text==(viewer.CurrentPage+1).ToString(),"ordinary wheel scrolls and synchronizes page number");
                viewer.AutoScrollPosition=new Point(0,viewer.PageRectangles[2].Top);
                Call(viewer,"OnScroll",new ScrollEventArgs(ScrollEventType.ThumbPosition,0,-viewer.AutoScrollPosition.Y,ScrollOrientation.VerticalScroll));
                Check(viewer.CurrentPage==2 && ((ToolStripTextBox)Field(form,"pageBox")).Text=="3","scrollbar synchronizes page number");
                viewer.GoToPage(0,0);viewer.SetHighlightMode(true);
                Rectangle firstPage=viewer.PageRectangles[0];
                Call(viewer,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,firstPage.Left+30,firstPage.Top+100+viewer.AutoScrollPosition.Y,0));
                Press(form,Keys.Escape);
                Call(viewer,"OnMouseUp",new MouseEventArgs(MouseButtons.Left,1,firstPage.Left+130,firstPage.Top+150+viewer.AutoScrollPosition.Y,0));
                Check(s.Annotations.Count==0,"Escape cancels unfinished highlight");
                var outline=(TreeView)Field(form,"outlineTree");
                Call(outline,"OnNodeMouseClick",new TreeNodeMouseClickEventArgs(outline.Nodes[1],MouseButtons.Left,1,5,5));
                Check(viewer.CurrentPage==3 && ((ToolStripTextBox)Field(form,"pageBox")).Text=="4","outline click matches actual PDF page number");
                Call(outline,"OnNodeMouseClick",new TreeNodeMouseClickEventArgs(outline.Nodes[0],MouseButtons.Left,1,5,5));
                Check(viewer.CurrentPage==1 && Math.Abs(viewer.Zoom-1.2)<.001,"outline XYZ zoom and page");
                viewer.SetZoom(1.4f);viewer.GoToPage(1,.3);Press(form,Keys.Control|Keys.D);
                Check(s.Record.bookmarks.Count==1 && sessions[1].Record.bookmarks.Count==0,"bookmarks isolated per document");
                var parent=s.Record.bookmarks[0];double offset=parent.pageOffset;
                Call(form,"AddBookmark",true);Check(s.Record.bookmarks[1].parentId==parent.id,"child bookmark hierarchy");
                var tree=(TreeView)Field(form,"bookmarkTree");
                Call(tree,"OnAfterLabelEdit",new NodeLabelEditEventArgs(tree.SelectedNode,"Renamed section"));Application.DoEvents();
                Check(s.Record.bookmarks[1].title=="Renamed section","bookmark label editing");
                Call(form,"ChangeBookmarkLevel",false);Check(String.IsNullOrEmpty(s.Record.bookmarks[1].parentId),"bookmark outdent");
                string childId=s.Record.bookmarks[1].id;Call(form,"MoveBookmark",-1);Check(s.Record.bookmarks[0].id==childId,"bookmark sibling reorder");
                Call(form,"MoveBookmark",1);Call(form,"ChangeBookmarkLevel",true);Check(s.Record.bookmarks[1].parentId==parent.id,"bookmark indent");
                viewer.GoToPage(2,.1);Call(form,"UpdateBookmarkPosition");Check(s.Record.bookmarks[1].pageIndex==2,"bookmark update target");
                Call(form,"ActivateBookmark",tree.Nodes[0]);Check(viewer.CurrentPage==1 && Math.Abs(viewer.Zoom-1.4)<.001,"bookmark restores page and zoom");
                viewer.CaptureReadingPosition(out int p,out double off);Check(Math.Abs(off-offset)<.005,"bookmark restores page offset");
                Press(form,Keys.Control|Keys.S);Check(!s.Dirty,"manual save shortcut");
                string portable=Path.Combine(dir,"portable");Directory.CreateDirectory(portable);
                string portablePdf=Path.Combine(portable,"copied.pdf");File.Copy(path,portablePdf);File.Copy(Path.ChangeExtension(path,".pdfdata"),Path.ChangeExtension(portablePdf,".pdfdata"));
                var paired=DocumentDataRepository.LoadForPdf(portablePdf,hash,4,null,(a,b)=>throw new Exception("Copied hash should not prompt"));
                Check(paired.Record.bookmarks.Count==2 && paired.Record.bookmarks[1].parentId==parent.id,"copied PDF and sidecar remain paired");
                Press(form,Keys.Control|Keys.W);Check(sessions.Count==1,"close tab shortcut");Call(form,"OpenPdf",path);
                sessions=(List<DocumentSession>)Field(form,"sessions");s=sessions.Last();viewer=s.Viewer;
                Check(s.Record.bookmarks.Count==2 && s.Record.bookmarks[1].title=="Renamed section" && s.Record.bookmarks[1].parentId==s.Record.bookmarks[0].id,"bookmarks survive close and reopen");
                tree=(TreeView)Field(form,"bookmarkTree");Call(form,"ActivateBookmark",tree.Nodes[0]);Check(viewer.CurrentPage==1,"reopened bookmark jumps");
                PdfReaderForm.RemoveBookmarkTree(s,s.Record.bookmarks[0]);Call(form,"MarkDirty",s);Call(form,"SaveSession",s);
                Check(s.Record.bookmarks.Count==0,"subtree deletion");
                var reopened=DocumentDataRepository.LoadForPdf(path,hash,4,null,(a,b)=>DialogResult.Yes);Check(reopened.Record.bookmarks.Count==0,"deletion persists");
                viewer.SetZoom(1f);viewer.GoToPage(0,0);
                Rectangle page=viewer.PageRectangles[0];Point scroll=viewer.AutoScrollPosition;
                Call(viewer,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,page.Left+scroll.X+(int)(page.Width*.3),page.Top+scroll.Y+(int)(page.Height*.14),0));
                Check(viewer.CurrentPage==3,"click on PDF table-of-contents link");
                Call(form,"RefreshBookmarks");
                ((TabControl)Field(form,"navigationTabs")).SelectedIndex=0;
                Call(form,"ActivateOutline",((TreeView)Field(form,"outlineTree")).Nodes[0]);
                viewer.RenderPagesNearViewport();
                Check(((Dictionary<int,Bitmap>)Field(viewer,"renderedPages")).Count>0,"navigation renders target page");
                Check(DocumentDataRepository.CalculateSha256(path)==hash,"original PDF unchanged");
                using(var shot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(dir,"acceptance.png"));}
                Console.WriteLine("SCREENSHOT "+Path.Combine(dir,"acceptance.png"));Console.WriteLine("ALL_ACCEPTANCE_PASSED");
            }
            catch(Exception e) { Console.WriteLine(e);Environment.ExitCode=1; }
            form.Close();
        };
        form.Shown+=delegate {timer.Start();};Application.Run(form);return Environment.ExitCode;
    }
}
