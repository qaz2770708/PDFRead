using System;using System.IO;using System.Linq;using System.Drawing;using System.Collections.Generic;using System.Reflection;using System.Windows.Forms;
internal static class PersistenceUiAcceptance
{
 static object Field(object o,string n){return o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);}
 static object Call(object o,string n,params object[] a){return o.GetType().GetMethod(n,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,a);}
 static void Check(bool ok,string m){if(!ok)throw new Exception(m);Console.WriteLine("PASS "+m);}
 static DocumentSession Session(PdfReaderForm f){return ((List<DocumentSession>)Field(f,"sessions")).Single();}
 static void Key(PdfReaderForm f,Keys keys){Check((bool)Call(f,"ProcessCmdKey",new Message(),keys),"shortcut "+keys);}
 static void Click(ContinuousPdfViewer v,float x,float y){var p=v.PageRectangles[v.CurrentPage];Point xy=new Point(p.Left+v.AutoScrollPosition.X+(int)(p.Width*x),p.Top+v.AutoScrollPosition.Y+(int)(p.Height*y));foreach(var n in new[]{"OnMouseDown","OnMouseUp"})Call(v,n,new MouseEventArgs(MouseButtons.Left,1,xy.X,xy.Y,0));}
 static void AddRectangle(ContinuousPdfViewer v,float y){v.SetAnnotationTool(AnnotationTool.Rectangle);v.AnnotationLayer.Begin(v.CurrentPage,new PointF(.1f,y),.01f,.01f);v.AnnotationLayer.End(new PointF(.25f,y+.03f));}
 static void Shot(Control c,string path){using(var b=new Bitmap(c.Width,c.Height)){c.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));b.Save(path);}}
 [STAThread]static int Main(string[] args)
 {
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  string folder=Path.GetFullPath(Path.Combine("work","m5-ui-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(folder);DocumentDataRepository.DataRootOverride=Path.Combine(folder,"data");string pdf=Path.Combine(folder,"报告.pdf");File.Copy(args[0],pdf);string hash=DocumentDataRepository.CalculateSha256(pdf);
  var context=new ReaderApplicationContext(new[]{pdf});var form=context.Windows[0];var timer=new Timer{Interval=1000};int stage=0;string stickyId=null,alternate=null;
  timer.Tick+=delegate
  {
   timer.Stop();try
   {
    var s=Session(form);var v=s.Viewer;var editor=v.InlineEditor;var status=(ToolStripLabel)Field(form,"statusLabel");
    if(stage==0)
    {
     Check(Path.GetFileName(s.SidecarPath)=="报告.pdfdata" && File.Exists(s.SidecarPath),"application creates named data file under hash namespace");
     Check(((ToolStripDropDownButton)Field(form,"dataButton")).DropDownItems.Count==4,"data menu offers retry save-as export and import");
     v.SetZoom(.75f);v.GoToPage(0,0);Key(form,Keys.Control|Keys.D);Key(form,Keys.Control|Keys.Shift|Keys.N);Click(v,.3f,.3f);
     Check(editor.IsOpen && editor.Draft.type=="sticky_note" && editor.Subtitle.Visible,"sticky tool opens editable page note with subtitle");
     editor.Input.Text="第五步便签正文";editor.Subtitle.Text="副文本随文件迁移";editor.ColorButtons[2].PerformClick();stickyId=editor.Draft.id;
     Check(status.Text=="未保存"&&s.Tab.Text.EndsWith("*"),"pending input immediately displays unsaved state");
     Key(form,Keys.Control|Keys.S);Check(status.Text=="已保存"&&File.ReadAllText(s.SidecarPath).Contains("副文本随文件迁移"),"manual save persists sticky body and subtitle");
     Shot(form,Path.Combine(folder,"sticky-editor.png"));Shot(editor.StylePanel,Path.Combine(folder,"sticky-style.png"));
     Key(form,Keys.Control|Keys.Enter);Key(form,Keys.Control|Keys.Shift|Keys.T);Click(v,.2f,.15f);editor.Input.Text="第五步文本批注";Key(form,Keys.Control|Keys.Enter);
     v.SetZoom(1.3f);v.GoToPage(2,.15);Call(form,"FlushPendingProgress");
     var keep=context.CreateWindow(new string[0]);form.Close();form=keep;Call(form,"OpenPdf",pdf);stage=1;timer.Start();return;
    }
    if(stage==1)
    {
     int page;double offset;v.CaptureReadingPosition(out page,out offset);
     Check(s.Annotations.Count==2 && s.Record.bookmarks.Count==1 && s.Annotations.Single(a=>a.id==stickyId).subtitle=="副文本随文件迁移" && page==2 && Math.Abs(offset-.15)<.005 && Math.Abs(v.Zoom-1.3)<.001,"actual close and reopen restores bookmarks notes annotations and reading position");
     int failures=0;form.SaveFailureOverride=(a,e)=>{failures++;return SaveFailureChoice.Keep;};string before=File.ReadAllText(s.SidecarPath);
     using(var locked=new FileStream(s.SidecarPath,FileMode.Open,FileAccess.Read,FileShare.Read))
     {
      AddRectangle(v,.35f);Check(s.Dirty && s.SaveFailed && status.Text=="保存失败"&&failures==1 && File.ReadAllText(s.SidecarPath)==before,"save failure keeps memory marks and offers recovery actions");
      AddRectangle(v,.45f);Check(failures==1&&s.Annotations.Count==4,"later autosave failures do not repeatedly interrupt reading");
      Check(!(bool)Call(form,"CloseSession",s) && ((List<DocumentSession>)Field(form,"sessions")).Count==1,"failed save prevents closing and losing memory edits");
     }
     Key(form,Keys.Control|Keys.S);Check(!s.Dirty&&!s.SaveFailed&&status.Text=="已保存","retry after failure saves all pending edits");
     string oldPath=s.SidecarPath;string oldBytes=File.ReadAllText(oldPath);alternate=Path.Combine(folder,"另存报告.pdfdata");form.DataSavePathOverride=a=>alternate;form.SaveFailureOverride=(a,e)=>SaveFailureChoice.SaveAs;
     using(var locked=new FileStream(oldPath,FileMode.Open,FileAccess.Read,FileShare.Read)){AddRectangle(v,.55f);}
     Check(s.SidecarPath==alternate && !s.Dirty && File.Exists(alternate)&&File.ReadAllText(oldPath)==oldBytes,"failure save-as switches location and preserves original valid data");
     var keep=context.CreateWindow(new string[0]);form.Close();form=keep;Call(form,"OpenPdf",pdf);stage=2;timer.Start();return;
    }
    Check(s.SidecarPath==alternate && s.Annotations.Count==5,"reopen follows user-selected alternate data path");
    string exported=Path.Combine(folder,"交付报告.pdfdata");form.DataSavePathOverride=a=>exported;
    Check((bool)Call(form,"SaveDataElsewhere",s,false),"data menu exports a portable snapshot");AddRectangle(v,.65f);
    form.DataPromptOverride=(a,b)=>a=="加载数据文件"?DialogResult.No:DialogResult.Yes;
    Check(!form.LoadDataFile(s,exported)&&s.Annotations.Count==6,"declining explicit replacement keeps current data");
    form.DataPromptOverride=(a,b)=>DialogResult.Yes;Check(form.LoadDataFile(s,exported)&&s.Annotations.Count==5,"confirmed import loads the chosen portable data");
    string bad=Path.Combine(folder,"坏数据.pdfdata");File.WriteAllText(bad,"{invalid");form.DataPromptOverride=(a,b)=>DialogResult.OK;string prior=s.SidecarPath;
    Check(!form.LoadDataFile(s,bad) && s.SidecarPath==prior && s.Annotations.Count==5,"failed explicit import preserves current in-memory document");
    using(var chooseTimer=new Timer{Interval=100})
    {
     chooseTimer.Tick+=delegate{var d=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Text=="PDF 版本不匹配");if(d==null)return;chooseTimer.Stop();Shot(d,Path.Combine(folder,"version-dialog.png"));d.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Single(b=>b.Text=="不加载数据").PerformClick();};chooseTimer.Start();
     Check(DataDialogs.VersionChoice(form,"PDF 内容已变化，请选择数据处理方式。") == DialogResult.Ignore,"actual four-choice version dialog supports no-load option");
    }
    using(var failTimer=new Timer{Interval=100})
    {
     failTimer.Tick+=delegate{var d=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Text=="数据保存失败");if(d==null)return;failTimer.Stop();Shot(d,Path.Combine(folder,"save-failure.png"));d.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Single(b=>b.Text=="重试").PerformClick();};failTimer.Start();
     Check(DataDialogs.SaveFailure(form,"测试文件被锁定。") == SaveFailureChoice.Retry,"actual save failure dialog exposes retry and save-as");
    }
    string unrelated=Path.Combine(folder,"另一份.pdfdata");var other=new SidecarFile();other.versions.Add(new VersionRecord{sourceHash=new string('a',64),pageCount=4});File.WriteAllText(unrelated,DocumentDataRepository.SerializeData(other));string intactPath=s.SidecarPath,intact=File.ReadAllText(intactPath);
    form.DataPromptOverride=(a,b)=>DialogResult.Ignore;Check(form.LoadDataFile(s,unrelated)&&s.DataReadOnly&&s.Annotations.Count==0,"UI no-load choice starts temporary reading without old marks");
    v.GoToPage(1,.2);Key(form,Keys.Control|Keys.Oemplus);Call(form,"FlushPendingProgress");Check(s.Record.lastPage==1 && !s.Dirty&&File.ReadAllText(intactPath)==intact,"temporary reading does not save progress over existing records");
    AddRectangle(v,.4f);string temporary=Path.Combine(folder,"临时阅读另存.pdfdata");form.DataSavePathOverride=a=>temporary;Key(form,Keys.Control|Keys.S);
    Check(!s.DataReadOnly&&!s.Dirty&&s.SidecarPath==temporary&&File.ReadAllText(intactPath)==intact,"temporary edits persist only after explicit save-as");
    Check(DocumentDataRepository.CalculateSha256(pdf)==hash,"UI persistence operations preserve PDF bytes");Console.WriteLine("PERSISTENCE_UI_ACCEPTANCE_PASS "+folder);foreach(var w in context.Windows.ToArray())w.Close();
   }
   catch(Exception ex){Console.WriteLine(ex);Environment.Exit(1);}
  };
  timer.Start();Application.Run(context);timer.Dispose();return 0;
 }
}
