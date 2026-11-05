using System;using System.IO;using System.Linq;using System.Drawing;using System.Reflection;using System.Collections.Generic;using System.Windows.Forms;using System.Web.Script.Serialization;
internal static class ProgressAcceptance
{
 static void Check(bool ok,string m){if(!ok)throw new Exception(m);Console.WriteLine("PASS "+m);}
 static object Field(object o,string n){return o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
 static object Call(object o,string n,params object[] args){return o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,args);}
 [STAThread]static int Main(string[] args)
 {
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  string folder=Path.GetFullPath(Path.Combine("work","progress-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(folder);
  DocumentDataRepository.DataRootOverride=Path.Combine(folder,"data");string pdf=Path.Combine(folder,"document.pdf");File.Copy(args[0],pdf);
  string hash=DocumentDataRepository.CalculateSha256(pdf),legacy=Path.ChangeExtension(pdf,".pdfdata");
  var old=new SidecarFile{formatVersion=3};old.versions.Add(new VersionRecord{sourceHash=hash,pageCount=4,lastPage=1,lastPageOffset=.36,zoom=1.25f});
  File.WriteAllText(legacy,new JavaScriptSerializer().Serialize(old));string legacyBytes=File.ReadAllText(legacy);
  var imported=DocumentDataRepository.LoadForPdf(pdf,hash,4,null,(a,b)=>DialogResult.Yes);
  Check(imported.NeedsSave && imported.Record.lastPage==1,"legacy sidecar detected with progress");
  DocumentDataRepository.Save(new DocumentSession{PdfPath=pdf,PdfHash=hash,Data=imported.Data,Record=imported.Record});
  string canonical=DocumentDataRepository.GetDataPath(hash),snapshot=File.ReadAllText(canonical);
  Check(File.ReadAllText(legacy)==legacyBytes && imported.Data.formatVersion==5,"legacy preserved and data upgraded in root data folder");
  var context=new ReaderApplicationContext(new[]{pdf});var form=context.Windows[0];var timer=new Timer{Interval=1200};int stage=0;
  timer.Tick+=delegate
  {
   timer.Stop();try
   {
    var s=((List<DocumentSession>)Field(form,"sessions")).Single();int page;double offset;s.Viewer.CaptureReadingPosition(out page,out offset);
    if(stage==0)
    {
     Check(page==1 && Math.Abs(offset-.36)<.005 && Math.Abs(s.Viewer.Zoom-1.25)<.001,"actual initial form layout restores saved progress");
     Check(File.ReadAllText(canonical)==snapshot && Directory.GetFiles(DocumentDataRepository.DataRoot,"*.pdfdata").Length==1,"reopening does not rewrite progress or duplicate data");
     var tools=(Form)Field(form,"rightTools");var split=(SplitContainer)Field(form,"navigationSplit");Rectangle area=split.Panel1.RectangleToScreen(split.Panel1.ClientRectangle);
     Check(Math.Abs(tools.Height-area.Height*.8)<2 && Math.Abs(tools.Top-area.Top-area.Height*.1)<2,"tools occupy eighty percent height with ten percent top and bottom margins");
     var groups=(List<ToolStripControlHost>)Field(form,"toolGroups");var centers=groups.Select(g=>g.Control.PointToScreen(new Point(0,g.Control.Height/2)).Y).ToArray();
     Check(centers.Zip(centers.Skip(1),(a,b)=>b-a).Max()-centers.Zip(centers.Skip(1),(a,b)=>b-a).Min()<3,"tool groups have equal vertical spacing");
     var tree=(TreeView)Field(form,"outlineTree");Check(tree.DrawMode==TreeViewDrawMode.Normal && tree.ItemHeight<40,"directory contains only chapter names");
     s.Viewer.SetZoom(1.4f);s.Viewer.GoToPage(2,.15);Call(form,"FlushPendingProgress");
     var keep=context.CreateWindow(new string[0]);form.Close();form=keep;Call(form,"OpenPdf",pdf);stage=1;timer.Start();return;
    }
    Check(page==2 && Math.Abs(offset-.15)<.005 && Math.Abs(s.Viewer.Zoom-1.4)<.001,"closing and reopening restores latest reading page offset and zoom");
    string alias=Path.Combine(folder,"renamed.pdf");File.Copy(pdf,alias);
    var aliased=DocumentDataRepository.LoadForPdf(alias,hash,4,null,(a,b)=>throw new Exception("same hash should not prompt"));
    Check(aliased.Record.lastPage==2 && Directory.GetFiles(DocumentDataRepository.DataRoot,"*.pdfdata").Length==1,"renamed or copied PDF reuses hash data file");
    File.AppendAllText(alias,"\n% changed version\n");string changed=DocumentDataRepository.CalculateSha256(alias);int prompts=0;
    var next=DocumentDataRepository.LoadForPdf(alias,changed,4,null,(a,b)=>{prompts++;return DialogResult.Yes;});
    Check(prompts==1 && next.Record.sourceHash==changed && next.Data.versions.Count==2,"modified PDF prompts and keeps previous version");
    DocumentDataRepository.Save(new DocumentSession{PdfPath=alias,PdfHash=changed,Data=next.Data,Record=next.Record});
    Check(File.Exists(canonical) && File.Exists(DocumentDataRepository.GetDataPath(changed)),"modified PDF never overwrites original hash data");
    using(var shot=new Bitmap(toolsSize(form).Width,toolsSize(form).Height)){((Form)Field(form,"rightTools")).DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(folder,"long-tools.png"));}
    Console.WriteLine("PROGRESS_ACCEPTANCE_PASS "+folder);foreach(var w in context.Windows.ToArray())w.Close();
   }
   catch(Exception e){Console.WriteLine(e);Environment.ExitCode=1;foreach(var w in context.Windows.ToArray())w.Close();}
  };
  timer.Start();Application.Run(context);timer.Dispose();return Environment.ExitCode;
 }
 static Size toolsSize(PdfReaderForm f){return ((Form)Field(f,"rightTools")).Size;}
}
