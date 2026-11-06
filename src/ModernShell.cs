using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class ModernUi
{
    internal static bool Enabled=false;
    internal static string AssetsOverride=null;
    internal static string UserDataOverride=null;
}
internal sealed partial class PdfReaderForm
{
    [DllImport("user32.dll")]private static extern bool ReleaseCapture();
    [DllImport("user32.dll")]private static extern bool PostMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    private WebView2 modernView;
    private Timer modernTimer;
    private bool modernReady,modernGesture,modernFaulted,modernDraftDragging;
    private DocumentSession modernGestureSession;

    private int modernNavigation;
    private string modernGestureDone="";
    private string modernPanel="",modernLastState="";
    private static readonly JavaScriptSerializer ModernJson=new JavaScriptSerializer{MaxJsonLength=Int32.MaxValue,RecursionLimit=128};
    internal string ModernStartupError {get;private set;}
    internal bool UsesModernUi {get{return modernReady && modernView!=null && !modernView.IsDisposed && modernView.CoreWebView2!=null;}}
    internal WebView2 ModernBrowser {get{return modernView;}}
    private async Task InitializeModernShell()
    {
        if(!ModernUi.Enabled||modernView!=null)return;
        Size=new Size(Math.Min(1280,Screen.FromControl(this).WorkingArea.Width),Math.Min(900,Screen.FromControl(this).WorkingArea.Height-40));
        try
        {
            string assets=ModernUi.AssetsOverride??Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"web");
            if(!File.Exists(Path.Combine(assets,"index.html")))throw new FileNotFoundException("HeroUI 界面资源不完整，请使用完整发行包。");
            modernView=new WebView2{Dock=DockStyle.Fill,DefaultBackgroundColor=Color.FromArgb(247,248,245),AllowExternalDrop=false};
            Controls.Add(modernView);modernView.BringToFront();
            Console.WriteLine("MODERN_INIT environment");
            var environment=await CoreWebView2Environment.CreateAsync(null,ModernUi.UserDataOverride??Path.Combine(Path.GetTempPath(),"PDFRead","WebView2"));
            if(IsDisposed)return;
            Console.WriteLine("MODERN_INIT controller");
            await modernView.EnsureCoreWebView2Async(environment);
            Console.WriteLine("MODERN_INIT initialized");
            var core=modernView.CoreWebView2;core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.AreBrowserAcceleratorKeysEnabled=false;core.Settings.AreDevToolsEnabled=false;core.Settings.IsStatusBarEnabled=false;core.Settings.IsZoomControlEnabled=false;
            core.SetVirtualHostNameToFolderMapping("pdfread.local",Path.GetFullPath(assets),CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e){if(!e.Uri.StartsWith("https://pdfread.local/",StringComparison.OrdinalIgnoreCase))e.Cancel=true;};
            core.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;};
            core.DownloadStarting+=delegate(object sender,CoreWebView2DownloadStartingEventArgs e){e.Cancel=true;};
            core.WebMessageReceived+=delegate(object sender,CoreWebView2WebMessageReceivedEventArgs e)
            {
                if(!e.Source.StartsWith("https://pdfread.local/",StringComparison.OrdinalIgnoreCase))return;
                try{HandleModernCommand(ModernJson.Deserialize<Dictionary<string,object>>(e.WebMessageAsJson));}
                catch(Exception ex){PostModern(new{type="error",message=Unwrap(ex).Message});}
            };
            FormBorderStyle=FormBorderStyle.None;MaximizedBounds=Screen.FromControl(this).WorkingArea;
            modernReady=true;toolStrip.Hide();navigationSplit.Hide();rightTools.Hide();
            foreach(var s in sessions)s.Viewer.SetModernPresentation(true);
            modernTimer=new Timer{Interval=180};modernTimer.Tick+=delegate{SendModernState(false);};modernTimer.Start();
            if(ModernUi.UserDataOverride!=null)
            {
                core.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown").DevToolsProtocolEventReceived+=delegate(object sender,CoreWebView2DevToolsProtocolEventReceivedEventArgs e){Console.WriteLine("WEB_EXCEPTION "+e.ParameterObjectAsJson);};
                _=core.CallDevToolsProtocolMethodAsync("Runtime.enable","{}");
            }
            core.NavigationCompleted+=delegate(object sender,CoreWebView2NavigationCompletedEventArgs e){Console.WriteLine("MODERN_NAV "+e.IsSuccess+" "+e.WebErrorStatus);};
            core.ProcessFailed+=delegate(object sender,CoreWebView2ProcessFailedEventArgs e)
            {
                if(e.ProcessFailedKind==CoreWebView2ProcessFailedKind.BrowserProcessExited||e.ProcessFailedKind==CoreWebView2ProcessFailedKind.RenderProcessExited)
                {ModernStartupError="WebView2 渲染进程中断："+e.Reason;BeginInvoke((Action)delegate{FailModernShell(new IOException(ModernStartupError));});}
            };
            core.Navigate("https://pdfread.local/index.html");Console.WriteLine("MODERN_INIT navigated");
        }
        catch(Exception ex)
        {
            FailModernShell(ex);
        }
    }
    private void FailModernShell(Exception ex)
    {
        if(modernFaulted||IsDisposed)return;modernFaulted=true;ModernStartupError=Unwrap(ex).ToString();Console.WriteLine("MODERN_STARTUP_ERROR "+ModernStartupError);
        modernReady=false;FormBorderStyle=FormBorderStyle.Sizable;modernTimer?.Stop();var old=modernView;modernView=null;old?.Dispose();toolStrip.Show();navigationSplit.Show();
        foreach(var session in sessions){session.Viewer.SetModernPresentation(false);RestoreSessionPosition(session);session.Viewer.SetActive(session==ActiveSession);}ShowReadingTools();
        if(ModernUi.UserDataOverride==null)MessageBox.Show(this,"HeroUI 界面无法运行，已保留阅读数据并恢复基础界面。请在正常 Windows 桌面启动，确认 WebView2 Runtime 和发行包 web 资源完整。\r\n\r\n"+Unwrap(ex).Message,"界面运行错误",MessageBoxButtons.OK,MessageBoxIcon.Error);
    }
    private void PostModern(object value){if(UsesModernUi && modernView.CoreWebView2!=null)modernView.CoreWebView2.PostWebMessageAsJson(ModernJson.Serialize(value));}
    private static string Arg(Dictionary<string,object> data,string key,string fallback=""){object value;return data.TryGetValue(key,out value)&&value!=null?Convert.ToString(value):fallback;}
    private static double Number(Dictionary<string,object> data,string key,double fallback=0){object value;double n=data.TryGetValue(key,out value)&&value!=null?Convert.ToDouble(value):fallback;if(Double.IsNaN(n)||Double.IsInfinity(n))throw new ArgumentException("数值无效。");return n;}
    private static bool Flag(Dictionary<string,object> data,string key){object value;return data.TryGetValue(key,out value)&&Convert.ToBoolean(value);}
    private DocumentSession ModernSession(string id){return sessions.FirstOrDefault(s=>s.PdfHash==id);}
    private static object[] ModernOutline(List<NavigationTarget> nodes,string prefix="")
    {
        return nodes.Select((n,i)=>(object)new{key=prefix+i,title=n.Title,page=n.PageIndex,offset=n.PageOffset,horizontal=n.HorizontalOffset,zoom=n.Zoom,children=ModernOutline(n.Children,prefix+i+".")}).ToArray();
    }
    internal object ModernSnapshot()
    {
        var s=ActiveSession;var layer=s?.Viewer.AnnotationLayer;var draft=s?.Viewer.InlineEditor.Draft;
        IEnumerable<AreaHighlight> marks=s?.Annotations??new List<AreaHighlight>();
        var preview=layer?.PresentationPreview;
        if(preview!=null&&!modernGesture)marks=marks.Where(m=>m.id!=preview.id).Concat(new[]{preview});
        if(draft!=null)marks=marks.Where(m=>m.id!=draft.id).Concat(new[]{draft});
        return new{type="state",heroui="3.2.7",maximized=WindowState==FormWindowState.Maximized,navigation=modernNavigation,panel=modernPanel,
            tabs=documentTabs.TabPages.Cast<TabPage>().Select(t=>{var d=(DocumentSession)t.Tag;return new{id=d.PdfHash,name=Path.GetFileName(d.PdfPath),path=d.PdfPath,dirty=d.Dirty||d.Viewer.InlineEditor.HasPendingChanges};}).ToArray(),
            active=s==null?null:(object)new{id=s.PdfHash,name=Path.GetFileName(s.PdfPath),path=s.PdfPath,pages=s.Viewer.DocumentPageSizes.Select(p=>new{width=p.Width,height=p.Height}).ToArray(),zoom=s.Record.zoom,page=s.Record.lastPage,offset=s.Record.lastPageOffset,horizontal=s.Record.lastPageHorizontal,
                gestureDone=modernGestureDone,tool=layer.Tool.ToString(),selected=layer.Selected?.id,marks=marks.ToArray(),draft=draft,gesture=modernGesture,undo=layer.CanUndo,redo=layer.CanRedo,status=statusLabel.Text,failed=s.SaveFailed,temporary=s.DataReadOnly,bookmarks=s.Record.bookmarks,outline=ModernOutline(s.Navigation?.Outline??new List<NavigationTarget>())},
            recent=DocumentDataRepository.RecentPaths().Select(p=>new{path=p,name=Path.GetFileName(p)}).ToArray()};
    }
    private void SendModernState(bool force)
    {
        if(!UsesModernUi||(!force&&(modernGesture||modernDraftDragging)))return;string json=ModernJson.Serialize(ModernSnapshot());if(!force&&json==modernLastState)return;modernLastState=json;modernView.CoreWebView2.PostWebMessageAsJson(json);
    }
    private void CancelModernGesture(){modernDraftDragging=false;if(modernGestureSession!=null)modernGestureSession.Viewer.AnnotationLayer.Cancel();modernGesture=false;modernGestureSession=null;}
    private void ModernGo(DocumentSession s,int page,double offset=0,double horizontal=0,float? zoom=null,bool finishEditor=true)
    {
        if(s==null)return;CancelModernGesture();if(finishEditor)s.Viewer.FinishInlineEditing();s.Record.lastPage=Math.Max(0,Math.Min(s.PageCount-1,page));s.Record.lastPageOffset=Math.Max(0,Math.Min(1,offset));s.Record.lastPageHorizontal=Math.Max(0,Math.Min(1,horizontal));
        if(zoom.HasValue)s.Record.zoom=Math.Max(.35f,Math.Min(2.2f,zoom.Value));
        s.Restoring=true;try{s.Viewer.SetZoom(s.Record.zoom);s.Viewer.GoToLocation(s.Record.lastPage,s.Record.lastPageOffset,s.Record.lastPageHorizontal);}finally{s.Restoring=false;}
        modernNavigation++;if(!s.DataReadOnly){MarkDirty(s);pendingProgressSaves.Add(s);progressSaveTimer.Stop();progressSaveTimer.Start();}
    }
    private void ModernReading(DocumentSession s,Dictionary<string,object> command)
    {
        int page=(int)Number(command,"page");if(page<0||page>=s.PageCount)return;
        double offset=Math.Max(0,Math.Min(1,Number(command,"offset"))),horizontal=Math.Max(0,Math.Min(1,Number(command,"horizontal")));
        bool changed=page!=s.Record.lastPage||Math.Abs(offset-s.Record.lastPageOffset)>.001||Math.Abs(horizontal-s.Record.lastPageHorizontal)>.001;
        s.Record.lastPage=page;s.Record.lastPageOffset=offset;s.Record.lastPageHorizontal=horizontal;
        if(changed&&!s.DataReadOnly){MarkDirty(s);pendingProgressSaves.Add(s);progressSaveTimer.Stop();progressSaveTimer.Start();}
    }
    internal void HandleModernCommand(Dictionary<string,object> c)
    {
        string op=Arg(c,"op");var s=ActiveSession;
        if(op=="windowMin"){WindowState=FormWindowState.Minimized;return;}
        if(op=="windowMax"){MaximizedBounds=Screen.FromControl(this).WorkingArea;WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;SendModernState(true);return;}
        if(op=="windowClose"){BeginInvoke((Action)Close);return;}
        if(op=="windowDrag"||op=="windowResize")
        {
            int edge=2;
            if(op=="windowResize")
            {
                var edges=new Dictionary<string,int>{{"l",10},{"r",11},{"t",12},{"tl",13},{"tr",14},{"b",15},{"bl",16},{"br",17}};
                if(!edges.TryGetValue(Arg(c,"edge"),out edge))return;
            }
            ReleaseCapture();PostMessage(Handle,0x00A1,new IntPtr(edge),IntPtr.Zero);return;
        }
        if(op=="ready"){SendModernState(true);return;}
        if(op=="textPage")
        {
            var source=ModernSession(Arg(c,"session"));int page=(int)Number(c,"page");
            if(source!=null&&page>=0&&page<source.PageCount)LoadModernText(source,page,Flag(c,"retry"));
            return;
        }
        if(op=="open"){CancelModernGesture();ShowOpenDialog();SendModernState(true);return;}
        if(op=="recent"){string path=Arg(c,"path");if(DocumentDataRepository.RecentPaths().Contains(path,StringComparer.OrdinalIgnoreCase))OpenPdf(path);SendModernState(true);return;}
        if(op=="render")
        {
            var render=ModernSession(Arg(c,"session"));if(render==null)return;int page=(int)Number(c,"page");if(page<0||page>=render.PageCount)return;
            float scale=(float)Math.Max(.1,Math.Min(3.5,Number(c,"scale",1)));var dimensions=render.Viewer.DocumentPageSizes[page];scale=Math.Min(scale,(float)Math.Sqrt(12000000/((double)dimensions.Width*dimensions.Height)));
            using(var bitmap=WindowsPdfRenderer.Render(render.PdfDocument,page,scale))using(var stream=new MemoryStream()){bitmap.Save(stream,ImageFormat.Png);PostModern(new{type="page",session=render.PdfHash,page,key=Arg(c,"key"),data="data:image/png;base64,"+Convert.ToBase64String(stream.ToArray())});}return;
        }
        if(op=="selectTab"){var selected=ModernSession(Arg(c,"id"));if(selected!=null){CancelModernGesture();documentTabs.SelectedTab=selected.Tab;modernNavigation++;}SendModernState(true);return;}
        if(op=="closeTab"){var close=ModernSession(Arg(c,"id"));if(close!=null){CancelModernGesture();CloseSession(close);}SendModernState(true);return;}
        if(op=="reorderTab")
        {
            var source=ModernSession(Arg(c,"id"));var target=ModernSession(Arg(c,"target"));
            if(source!=null&&target!=null&&source!=target){var selected=documentTabs.SelectedTab;int index=documentTabs.TabPages.IndexOf(target.Tab);movingSession=true;try{documentTabs.TabPages.Remove(source.Tab);documentTabs.TabPages.Insert(index,source.Tab);documentTabs.SelectedTab=selected;}finally{movingSession=false;}}SendModernState(true);return;
        }
        if(op=="detachTab"){var detached=ModernSession(Arg(c,"id"));if(detached!=null)DetachSession(detached,new Point((int)Number(c,"x"),(int)Number(c,"y")));SendModernState(true);return;}
        if(c.ContainsKey("session") && Arg(c,"session")!=s?.PdfHash)return;
        if(op=="panel"){modernPanel=Arg(c,"panel")==modernPanel?"":Arg(c,"panel");SendModernState(true);return;}
        if(s==null)return;
        if(op=="copyText"){string text=Arg(c,"text");if(text.Length>2000000)throw new InvalidOperationException("选择的文字过多，请分段复制。");if(text.Length>0)Clipboard.SetText(text,TextDataFormat.UnicodeText);return;}
        if(op=="reading"){ModernReading(s,c);return;}
        if(op=="goto"){ModernGo(s,(int)Number(c,"page"),Number(c,"offset"),Number(c,"horizontal"));}
        else if(op=="zoom"){ModernGo(s,s.Record.lastPage,s.Record.lastPageOffset,s.Record.lastPageHorizontal,(float)(c.ContainsKey("delta")?s.Record.zoom+Number(c,"delta"):Number(c,"zoom",s.Record.zoom)),false);}
        else if(op=="fit")
        {
            var page=s.Viewer.DocumentPageSizes[s.Record.lastPage];float width=(float)Number(c,"width",800),height=(float)Number(c,"height",700);
            float zoom=Flag(c,"whole")?Math.Min((width-80)/page.Width,(height-70)/page.Height):(width-80)/page.Width;ModernGo(s,s.Record.lastPage,s.Record.lastPageOffset,s.Record.lastPageHorizontal,zoom,false);
        }
        else if(op=="tool"){if(Enum.TryParse(Arg(c,"tool"),out AnnotationTool tool))s.Viewer.SetAnnotationTool(tool);}
        else if(op=="select")
        {
            s.Viewer.SetAnnotationTool(AnnotationTool.Select);int page=(int)Number(c,"page");if(page>=0&&page<s.PageCount){var dimensions=s.Viewer.DocumentPageSizes[page];s.Viewer.AnnotationLayer.Begin(page,new PointF((float)Number(c,"x"),(float)Number(c,"y")),8/(dimensions.Width*s.Record.zoom),8/(dimensions.Height*s.Record.zoom));s.Viewer.AnnotationLayer.Cancel();}
            modernGesture=false;
        }
        else if(op=="begin")
        {
            s.Viewer.FinishInlineEditing();int page=(int)Number(c,"page");if(page<0||page>=s.PageCount)return;var size=s.Viewer.DocumentPageSizes[page];
            modernGesture=s.Viewer.AnnotationLayer.Begin(page,new PointF((float)Number(c,"x"),(float)Number(c,"y")),8/(size.Width*s.Record.zoom),8/(size.Height*s.Record.zoom));modernGestureSession=s;
        }
        else if(op=="move")
        {
            if(modernGesture&&modernGestureSession==s){object samples;if(c.TryGetValue("points",out samples)&&samples is System.Collections.IEnumerable)foreach(var value in (System.Collections.IEnumerable)samples){var sample=value as Dictionary<string,object>;if(sample!=null)s.Viewer.AnnotationLayer.Move(new PointF((float)Number(sample,"x"),(float)Number(sample,"y")));}else s.Viewer.AnnotationLayer.Move(new PointF((float)Number(c,"x"),(float)Number(c,"y")));}return;
        }
        else if(op=="end")
        {
            if(modernGesture&&modernGestureSession==s)s.Viewer.AnnotationLayer.End(new PointF((float)Number(c,"x"),(float)Number(c,"y")));modernGesture=false;modernGestureSession=null;modernGestureDone=Arg(c,"token");
        }
        else if(op=="quick")s.Viewer.ShowInlineSelection();
        else if(op=="draftDragBegin"){modernDraftDragging=true;s.Viewer.InlineEditor.BeginExternalDrag();}
        else if(op=="draftDragEnd"){modernDraftDragging=false;s.Viewer.InlineEditor.EndExternalDrag();}
        else if(op=="draft")
        {
            var editor=s.Viewer.InlineEditor;if(!editor.IsOpen)return;
            if(c.ContainsKey("text"))editor.Input.Text=Arg(c,"text");if(c.ContainsKey("subtitle"))editor.Subtitle.Text=Arg(c,"subtitle");
            if(c.ContainsKey("color"))editor.SetColor(Arg(c,"color"));if(c.ContainsKey("size"))editor.SizeSlider.Value=Math.Max(editor.SizeSlider.Minimum,Math.Min(editor.SizeSlider.Maximum,(int)Number(c,"size")));
            if(c.ContainsKey("transparency"))editor.TransparencySlider.Value=Math.Max(0,Math.Min(100,(int)Number(c,"transparency")));
            if(c.ContainsKey("x"))editor.SetBounds(new RectangleF((float)Number(c,"x"),(float)Number(c,"y"),(float)Number(c,"width"),(float)Number(c,"height")));
            if(modernDraftDragging)return;
        }
        else if(op=="finish")s.Viewer.FinishInlineEditing();
        else if(op=="cancel"){modernDraftDragging=false;modernGesture=false;modernGestureSession=null;s.Viewer.CancelAnnotationOperation();}
        else if(op=="delete") {s.Viewer.FinishInlineEditing();s.Viewer.AnnotationLayer.DeleteSelected();}
        else if(op=="deleteDraft")s.Viewer.InlineEditor.Delete();
        else if(op=="undo" || op=="redo"){s.Viewer.FinishInlineEditing();if(op=="undo")s.Viewer.AnnotationLayer.Undo();else s.Viewer.AnnotationLayer.Redo();}
        else if(op=="professional")
        {
            string id=Arg(c,"id");var original=s.Viewer.InlineEditor.Draft?.id==id?s.Viewer.InlineEditor.Draft:s.Viewer.AnnotationLayer.Find(id);if(original==null)return;
            var mark=ModernJson.Deserialize<AreaHighlight>(ModernJson.Serialize(original));s.Viewer.FinishInlineEditing();
            mark.text=Arg(c,"text",mark.text);mark.subtitle=Arg(c,"subtitle",mark.subtitle);mark.color=Arg(c,"color",mark.color);ColorTranslator.FromHtml(mark.color);
            mark.pageIndex=Math.Max(0,Math.Min(s.PageCount-1,(int)Number(c,"page",mark.pageIndex)));mark.fontSize=(float)Math.Max(6,Math.Min(72,Number(c,"fontSize",mark.fontSize)));mark.strokeWidth=(float)Math.Max(.5,Math.Min(20,Number(c,"strokeWidth",mark.strokeWidth)));mark.opacity=Math.Max(0,Math.Min(1,Number(c,"opacity",mark.opacity)));
            s.Viewer.AnnotationLayer.ApplyInline(mark);if(mark.pageIndex!=s.Record.lastPage)ModernGo(s,mark.pageIndex,mark.y,mark.x);
        }
        else if(op=="save")SaveSessionWithPrompt(s,true);
        else if(op=="saveAs")SaveDataElsewhere(s,true);
        else if(op=="exportData")SaveDataElsewhere(s,false);
        else if(op=="importData")ChooseDataFile();
        else if(op=="exportPdf")ChooseAnnotatedPdfExport();
        else if(op=="bookmarkAdd")
        {
            string parent=Arg(c,"parent");bookmarkTree.SelectedNode=FindBookmarkNode(bookmarkTree.Nodes,parent);AddBookmark(!String.IsNullOrEmpty(parent));modernPanel="bookmarks";
        }
        else if(op=="bookmarkRename")
        {
            var bookmark=s.Record.bookmarks.FirstOrDefault(b=>b.id==Arg(c,"id"));string title=Arg(c,"title").Trim();if(bookmark!=null&&title.Length>0){bookmark.title=title.Substring(0,Math.Min(500,title.Length));MarkDirty(s);SaveSession(s);RefreshBookmarks();}
        }
        else if(op=="bookmarkMove"){if(Enum.TryParse(Arg(c,"position"),out BookmarkDropPosition position))TryMoveBookmark(Arg(c,"id"),Arg(c,"target",null),position);}
        else if(op=="bookmarkDelete")
        {
            var bookmark=s.Record.bookmarks.FirstOrDefault(b=>b.id==Arg(c,"id"));if(bookmark!=null){if(s.Record.bookmarks.Any(b=>b.parentId==bookmark.id)&&!Flag(c,"confirmed"))throw new InvalidOperationException("请先确认删除书签及其子级。");RemoveBookmarkTree(s,bookmark);MarkDirty(s);SaveSession(s);RefreshBookmarks();}
        }
        else if(op=="bookmarkUpdate"){var bookmark=s.Record.bookmarks.FirstOrDefault(b=>b.id==Arg(c,"id"));if(bookmark!=null){CaptureBookmark(s,bookmark);MarkDirty(s);SaveSession(s);RefreshBookmarks();}}
        else if(op=="bookmarkGo"){var bookmark=s.Record.bookmarks.FirstOrDefault(b=>b.id==Arg(c,"id"));if(bookmark!=null)ModernGo(s,bookmark.pageIndex,bookmark.pageOffset,bookmark.horizontalOffset,bookmark.zoom>0?(float?)bookmark.zoom:null);}
        else if(op=="outlineGo")
        {
            NavigationTarget target=null;var nodes=s.Navigation?.Outline;foreach(string part in Arg(c,"key").Split('.')){int i;if(nodes==null||!Int32.TryParse(part,out i)||i<0||i>=nodes.Count){target=null;break;}target=nodes[i];nodes=target.Children;}
            if(target!=null&&target.PageIndex>=0)ModernGo(s,target.PageIndex,target.PageOffset,target.HorizontalOffset,target.Zoom);
        }
        else if(op=="link")
        {
            var link=s.Navigation?.Links.FirstOrDefault(l=>l.PageIndex==(int)Number(c,"page")&&l.Bounds.Contains((float)Number(c,"x"),(float)Number(c,"y")));if(link!=null)ModernGo(s,link.Target.PageIndex,link.Target.PageOffset,link.Target.HorizontalOffset,link.Target.Zoom);
        }
        SendModernState(true);
    }
    private readonly HashSet<string> modernTextRequests=new HashSet<string>();
    private async void LoadModernText(DocumentSession session,int page,bool retry)
    {
        string key=session.PdfHash+":"+page;if(!modernTextRequests.Add(key))return;
        try{var source=session.TextSource;var content=await Task.Run(()=>source.GetSelectableText(page,retry));if(!IsDisposed&&UsesModernUi&&sessions.Contains(session))PostModern(new{type="textPage",session=session.PdfHash,page,content});}
        catch(Exception){if(!IsDisposed&&UsesModernUi)PostModern(new{type="textPage",session=session.PdfHash,page,content=new SelectableText{error="文字加载未完成，请重试。"}});}
        finally{modernTextRequests.Remove(key);}
    }
    private void DisposeModernShell(){modernTimer?.Dispose();modernView?.Dispose();}
}
