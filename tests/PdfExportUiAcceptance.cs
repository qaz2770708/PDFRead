using System;using System.IO;using System.Linq;using System.Drawing;using System.Collections.Generic;using System.Reflection;using System.Windows.Forms;
internal static class PdfExportUiAcceptance
{
 static object Field(object o,string n){return o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);}
 static object Call(object o,string n,params object[] a){return o.GetType().GetMethod(n,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,a);}
 static void Check(bool ok,string m){if(!ok)throw new Exception(m);Console.WriteLine("PASS "+m);}
 [STAThread]static int Main(string[] args)
 {
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  string folder=Path.GetFullPath(Path.Combine("work","pdf-export-ui-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(folder);DocumentDataRepository.DataRootOverride=Path.Combine(folder,"data");string source=Path.Combine(folder,"navigation.pdf");File.Copy(args[0],source);string hash=DocumentDataRepository.CalculateSha256(source);
  var context=new ReaderApplicationContext(new[]{source});var form=context.Windows[0];var timer=new Timer{Interval=1000};
  timer.Tick+=delegate
  {
   timer.Stop();try
   {
    var s=((List<DocumentSession>)Field(form,"sessions")).Single();var viewer=s.Viewer;var layer=viewer.AnnotationLayer;var editor=viewer.InlineEditor;
    viewer.SetZoom(.75f);viewer.GoToPage(0,0);
    PdfExportAcceptance.AddMarksForUi(s.Record,0); // Eight appearance types, including note subtitle.
    viewer.SetAnnotations(s.Annotations);Call(form,"MarkDirty",s);Call(form,"SaveSession",s);
    layer.SetTool(AnnotationTool.Text);layer.Begin(0,new PointF(.1f,.86f),.01f,.01f);layer.End(new PointF(.1f,.86f));editor.Input.Text="新增批注会在导出前自动保存";
    string target=Path.Combine(folder,"导出结果.pdf");form.PdfExportPathOverride=x=>target;var errors=new List<string>();form.DataPromptOverride=(a,b)=>{errors.Add(a+": "+b);return DialogResult.OK;};
    Check((bool)Call(form,"ProcessCmdKey",new Message(),Keys.Control|Keys.E),"Ctrl E dispatches annotated PDF export");
    Check(File.Exists(target)&&!editor.IsOpen&&!s.Dirty&&File.ReadAllText(s.SidecarPath).Contains("新增批注会在导出前自动保存")&&errors.Count==0,"export flushes pending editor input and reads persisted data");
    File.Copy(target,Path.Combine(folder,"export-demo.pdf"));
    var before=PdfNavigationReader.Read(source);var after=PdfNavigationReader.Read(target);Check(after.Error==null&&after.Outline.Count==before.Outline.Count&&after.Links.Count==before.Links.Count&&after.Links[0].Target.PageIndex==3,"original directory and internal links remain usable in export");
    string outputHash=DocumentDataRepository.CalculateSha256(target);int overwritePrompts=0;form.PdfExportOverwriteOverride=p=>{overwritePrompts++;return false;};Call(form,"ChooseAnnotatedPdfExport");Check(overwritePrompts==1&&DocumentDataRepository.CalculateSha256(target)==outputHash,"existing export prompts and cancellation preserves it");
    var note=s.Annotations.First(m=>m.type=="text_note");note.text="确认覆盖后的批注";Call(form,"MarkDirty",s);Call(form,"SaveSession",s);form.PdfExportOverwriteOverride=p=>{overwritePrompts++;return true;};Call(form,"ChooseAnnotatedPdfExport");Check(overwritePrompts==2&&DocumentDataRepository.CalculateSha256(target)!=outputHash&&errors.Count==0,"confirmed overwrite exports current saved annotations");
    form.PdfExportPathOverride=x=>null;int count=Directory.GetFiles(folder,"*.pdf").Length;Call(form,"ChooseAnnotatedPdfExport");Check(Directory.GetFiles(folder,"*.pdf").Length==count,"cancelling destination produces no PDF");
    form.PdfExportPathOverride=x=>source;Call(form,"ChooseAnnotatedPdfExport");Check(errors.Count==1&&errors[0].StartsWith("PDF 导出失败"),"choosing original PDF is rejected through UI");
    string dataText=File.ReadAllText(s.SidecarPath);File.WriteAllText(s.SidecarPath,dataText+" ");form.PdfExportPathOverride=x=>Path.Combine(folder,"conflict.pdf");Call(form,"ChooseAnnotatedPdfExport");Check(errors.Count==2&&!File.Exists(Path.Combine(folder,"conflict.pdf")),"external data changes prevent stale UI export");File.WriteAllText(s.SidecarPath,dataText);
    Check(DocumentDataRepository.CalculateSha256(source)==hash,"source PDF unchanged by all UI export operations");
    viewer.SetAnnotationTool(AnnotationTool.Select);viewer.GoToPage(0,0);viewer.RenderPagesNearViewport();using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(folder,"export-ui.png"));}
    Console.WriteLine("PDF_EXPORT_UI_ACCEPTANCE_PASS "+folder);foreach(var w in context.Windows.ToArray())w.Close();
   }catch(Exception ex){Console.WriteLine(ex);Environment.Exit(1);}
  };timer.Start();Application.Run(context);timer.Dispose();return 0;
 }
}
