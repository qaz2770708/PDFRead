using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Reflection;using System.Threading.Tasks;using System.Windows.Forms;using System.Web.Script.Serialization;using Microsoft.Web.WebView2.Core;
internal static class ModernUiAcceptance
{
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=Int32.MaxValue};static string folder;static CoreWebView2 browser;
 static void Check(bool ok,string m){if(!ok)throw new Exception(m);Console.WriteLine("PASS "+m);}
 static List<DocumentSession> Sessions(PdfReaderForm f){return (List<DocumentSession>)typeof(PdfReaderForm).GetField("sessions",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(f);}
 static Task<string> JS(string code){return browser.ExecuteScriptAsync(code);}
 static async Task Wait(string condition){for(int i=0;i<50;i++){if(await JS(condition)=="true")return;await Task.Delay(100);}throw new Exception("UI condition timed out: "+condition);}
 static async Task Click(string selector){await JS("(()=>{let e=document.querySelector("+Json.Serialize(selector)+");e?.focus();e?.click();})()");await Task.Delay(250);}
 static async Task Command(string op,object more=null){var data=new Dictionary<string,object>{{"op",op}};if(more!=null)foreach(var p in more.GetType().GetProperties())data[p.Name]=p.GetValue(more,null);await JS("window.chrome.webview.postMessage("+Json.Serialize(data)+")");await Task.Delay(250);}
 static async Task Point(double x,double y,string type="mousePressed",string button="left")
 {await browser.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",Json.Serialize(new{type,x,y,button,buttons=type=="mouseReleased"?0:1,clickCount=1}));}
 static async Task<double[]> PagePoint(double x,double y,int page=0)
 {string raw=await JS("(()=>{let r=document.querySelector('[data-page=\\\""+page+"\\\"]').getBoundingClientRect();return [r.left+r.width*"+x.ToString(System.Globalization.CultureInfo.InvariantCulture)+",r.top+r.height*"+y.ToString(System.Globalization.CultureInfo.InvariantCulture)+"];})()".Replace("\\\"","\""));return Json.Deserialize<double[]>(raw);}
 static async Task Tap(double x,double y){var p=await PagePoint(x,y);await Point(p[0],p[1]);await Point(p[0],p[1],"mouseReleased");await Task.Delay(300);}
 static async Task Drag(double x,double y,double toX,double toY)
 {var a=await PagePoint(x,y);var b=await PagePoint(toX,toY);await Point(a[0],a[1]);await Point(b[0],b[1],"mouseMoved");await Point(b[0],b[1],"mouseReleased");await Task.Delay(350);}
 static async Task Insert(string value){await browser.CallDevToolsProtocolMethodAsync("Input.insertText",Json.Serialize(new{text=value}));await Task.Delay(200);}
 static async Task Shot(string name){using(var stream=File.Create(Path.Combine(folder,name+".png")))await browser.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);}
 static async Task Ready(PdfReaderForm form)
 {for(int i=0;i<80;i++){if(form.ModernStartupError!=null)throw new Exception(form.ModernStartupError);if(form.UsesModernUi&&form.ModernBrowser.CoreWebView2!=null){browser=form.ModernBrowser.CoreWebView2;if(await JS("document.querySelectorAll('.pdf-page').length>0")=="true"){await Wait("document.querySelectorAll('img.pdf-render').length>0");return;}}await Task.Delay(150);}throw new Exception("HeroUI startup timeout");}
 [STAThread]static int Main(string[] args)
 {
  string repo=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"../../../.."));Directory.SetCurrentDirectory(repo);
  if(args.Length==0){Console.SetOut(new StreamWriter(Path.Combine(repo,"work/modern-desktop-test.log"),false){AutoFlush=true});args=new[]{Path.Combine(repo,"samples/navigation.pdf")};}
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  folder=Path.GetFullPath(Path.Combine("work","modern-ui-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(folder);ModernUi.Enabled=true;ModernUi.AssetsOverride=Path.GetFullPath("ui/dist");ModernUi.UserDataOverride=Path.Combine(folder,"profile");DocumentDataRepository.DataRootOverride=Path.Combine(folder,"data");
  string pdf=Path.Combine(folder,"阅读示例.pdf");File.Copy(args[0],pdf);string hash=DocumentDataRepository.CalculateSha256(pdf);var context=new ReaderApplicationContext(new[]{pdf});var form=context.Windows[0];var timer=new Timer{Interval=500};
  timer.Tick+=async delegate
  {
   timer.Stop();try
   {
    await Ready(form);await Task.Delay(700);var s=Sessions(form).Single();
    Check(await JS("document.querySelector('[data-heroui-version]').dataset.herouiVersion")=="\"3.2.7\"","real HeroUI 3.2.7 shell loads in WebView2");await Shot("reader");
    await JS("(()=>{let p=document.querySelector('[data-page]'),r=p.getBoundingClientRect();for(let deltaY of [-40,-40,-20])p.dispatchEvent(new WheelEvent('wheel',{bubbles:true,cancelable:true,ctrlKey:true,deltaY,clientX:r.left+100,clientY:r.top+150}));})()");await Task.Delay(400);Check(Math.Abs(s.Record.zoom-.92)<.001,"high precision Ctrl wheel accumulates and zooms through web UI");await Command("zoom",new{zoom=.82});
    Check(await JS("[...document.querySelectorAll('button,[role=dialog]')].every(e=>!getComputedStyle(e).backgroundImage.includes('gradient'))")=="true","visible controls use flat surfaces without gradients");
    await Click("button[aria-label='用户书签']");await Click(".bookmark-toolbar button");Check(s.Record.bookmarks.Count==1,"HeroUI bookmark add persists through native data bridge");await Shot("bookmarks");
    await Command("panel",new{panel="bookmarks"});await Click("button[aria-label^='文本批注']");await Tap(.2,.3);await Wait("!!document.querySelector('[aria-label=批注正文]')");
    await Insert("现代界面的中文批注");Check(s.Viewer.InlineEditor.Draft.text=="现代界面的中文批注","native browser text input reaches separate annotation layer");
    await Click("button[aria-label='颜色 #8051B5']");await Click("button[aria-label=增大字号]");Check(s.Viewer.InlineEditor.Draft.color=="#8051B5"&&s.Viewer.InlineEditor.Draft.fontSize==13,"HeroUI swatches and size controls update draft");
    await Command("zoom",new{zoom=.9});Check(s.Viewer.InlineEditor.IsOpen,"zoom preserves active text editor");await Shot("annotation");await Command("finish");Check(s.Annotations.Count==1&&File.ReadAllText(s.SidecarPath).Contains("现代界面的中文批注"),"inline text persists on completion");
    await Click("button[aria-label^='页面便签']");await Tap(.54,.35);await Wait("!!document.querySelector('[aria-label=批注正文]')");await Insert("便签正文");await Click("input[aria-label=便签副文本]");await Insert("保存在独立数据中");await Command("finish");Check(s.Annotations.Any(m=>m.type=="sticky_note"&&m.subtitle=="保存在独立数据中"),"modern sticky note body and subtitle save correctly");
    await Click("button[aria-label^='矩形']");await Drag(.1,.54,.29,.62);Check(s.Annotations.Count(m=>m.type=="rectangle")==1,"browser pointer drag creates rectangle");var shape=s.Annotations.Single(m=>m.type=="rectangle");double initialX=shape.x;
    await Click("button[aria-label^='选择 / 移动']");await Drag((float)(shape.x+shape.width/2),(float)(shape.y+shape.height/2),(float)(shape.x+shape.width/2+.05),(float)(shape.y+shape.height/2+.02));shape=s.Annotations.Single(m=>m.type=="rectangle");Check(shape.x>initialX+.04,"browser selection drag moves stored geometry");
    double initialW=shape.width;await Drag(shape.x+shape.width,shape.y+shape.height,shape.x+shape.width+.04,shape.y+shape.height+.02);Check(s.Annotations.Single(m=>m.type=="rectangle").width>initialW+.03,"corner drag resizes annotation");await Command("quick");await Shot("shape-style");await Click("button[aria-label='专业属性 · F2']");await Wait("!!document.querySelector('.professional-form')");await Shot("professional");await Click(".reader-dialog button[aria-label=关闭]");Check(await JS("!document.querySelector('.professional-form')")=="true","professional property dialog uses HeroUI and dismisses cleanly");await Command("finish");
    await Command("undo");Check(s.Annotations.Single(m=>m.type=="rectangle").width<initialW+.01,"undo crosses modern UI bridge");await Command("redo");
    await JS("(()=>{const v=document.querySelector('.reader-scroll'),p=document.querySelector('[data-page=\\\"1\\\"]');v.scrollTop+=p.getBoundingClientRect().top-v.getBoundingClientRect().top+p.clientHeight*.23;})()".Replace("\\\"","\""));await Task.Delay(550);Check(s.Record.lastPage==1&&Math.Abs(s.Record.lastPageOffset-.23)<.01,"browser continuous scroll updates saved reading position");
    await Click("button[aria-label='用户书签']");await Click(".bookmark-toolbar button");Check(s.Record.bookmarks.Last().pageIndex==1,"bookmark records browser viewport rather than hidden legacy viewer");
    await Command("bookmarkMove",new{id=s.Record.bookmarks[1].id,target=s.Record.bookmarks[0].id,position="Inside"});Check(s.Record.bookmarks[1].parentId==s.Record.bookmarks[0].id,"bookmark nesting persists with modern bridge");
    await JS("(()=>{const row=document.querySelector('[data-bookmark]');let r=row.getBoundingClientRect();row.dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,clientX:r.left+40,clientY:r.top+10}));})()");await Task.Delay(250);await Wait("document.querySelectorAll('.context-menu [role=menuitem]').length===5");await Shot("bookmark-menu");await JS("document.querySelector('.context-menu [role=menuitem]').click()");await Task.Delay(300);Check(s.Record.bookmarks.Count==3,"HeroUI context menu actions are live");
    await Click("button[aria-label='缩略图']");await Wait("!!document.querySelector('.thumbnail img')");await Click("button[aria-label='跳转到第 3 页']");Check(s.Record.lastPage==2,"modern thumbnails navigate to actual page index");await Shot("thumbnails");
    await Click("button[aria-label='目录']");await JS("[...document.querySelectorAll('.outline-link')].at(-1).click()");await Task.Delay(400);Check(s.Record.lastPage==3,"modern outline destination reaches PDF page four");
    string output=Path.Combine(folder,"modern-export.pdf");form.PdfExportPathOverride=d=>output;await Command("exportPdf");Check(File.Exists(output)&&DocumentDataRepository.CalculateSha256(pdf)==hash,"modern export preserves source PDF");
    await Command("goto",new{page=1,offset=.28});await Command("save");var keep=context.CreateWindow(new string[0]);form.Close();form=keep;typeof(PdfReaderForm).GetMethod("OpenPdf",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{pdf});await Ready(form);s=Sessions(form).Single();Check(s.Record.lastPage==1&&Math.Abs(s.Record.lastPageOffset-.28)<.01&&s.Annotations.Count==3&&s.Record.bookmarks.Count==3,"new UI reopens saved progress bookmarks and all annotations");
    await Click("button[aria-label=最大化窗口]");Check(form.WindowState==FormWindowState.Maximized,"HeroUI custom window controls maximize");await Click("button[aria-label=还原窗口]");Check(form.WindowState==FormWindowState.Normal,"HeroUI custom window controls restore");
    string second=Path.Combine(folder,"second.pdf");File.Copy(pdf,second);File.AppendAllText(second,"\n% another document\n");typeof(PdfReaderForm).GetMethod("OpenPdf",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{second});await Task.Delay(500);
    var other=Sessions(form).Last();await Command("reading",new{session=s.PdfHash,page=3,offset=.9});Check(other.Record.lastPage==0,"late reading events cannot overwrite another document position");
    await Command("selectTab",new{id=s.PdfHash});await Task.Delay(300);Check(s.Record.lastPage==1&&Math.Abs(s.Record.lastPageOffset-.28)<.01,"modern tab switching restores independent document position");
    var detached=context.CreateWindow(new string[0]);Check(form.TransferSessionTo(s,detached),"modern document can move to separate window");await Ready(detached);Check(s.Record.lastPage==1&&Math.Abs(s.Record.lastPageOffset-.28)<.01,"detached HeroUI window retains browser reading position");
    Console.WriteLine("MODERN_UI_ACCEPTANCE_PASS "+folder);foreach(var window in context.Windows.ToArray())window.Close();
   }catch(Exception e){Console.WriteLine(e);Environment.Exit(1);}
  };timer.Start();Application.Run(context);timer.Dispose();return 0;
 }
}
