using System;using System.IO;using System.Linq;using System.Drawing;using System.Collections.Generic;using System.Reflection;using System.Windows.Forms;using System.Web.Script.Serialization;
internal static class AnnotationAcceptance
{
 static void Check(bool ok,string m){if(!ok)throw new Exception(m);Console.WriteLine("PASS "+m);}
 static object Field(object o,string n){return o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
 static object Call(object o,string n,params object[] a){return o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,a);}
 static void Key(PdfReaderForm f,Keys k){Check((bool)Call(f,"ProcessCmdKey",new Message(),k),"shortcut "+k);}
 static Point Pixel(ContinuousPdfViewer v,PointF p){var b=v.PageRectangles[0];return new Point(b.Left+v.AutoScrollPosition.X+(int)(p.X*b.Width),b.Top+v.AutoScrollPosition.Y+(int)(p.Y*b.Height));}
 static void Mouse(ContinuousPdfViewer v,string method,PointF p){var px=Pixel(v,p);Call(v,method,new MouseEventArgs(MouseButtons.Left,1,px.X,px.Y,0));}
 static List<AreaHighlight> Disk(DocumentSession s){return new JavaScriptSerializer().Deserialize<SidecarFile>(File.ReadAllText(s.SidecarPath)).versions.First(x=>x.sourceHash==s.PdfHash).annotations;}
 static PointF HitPoint(AreaHighlight m)
 {
  if(m.type=="ink"){var p=m.points[m.points.Count/2];return new PointF((float)p.x,(float)p.y);}
  return new PointF((float)(m.x+m.width/2),(float)(m.y+m.height/2));
 }
 [STAThread]static int Main(string[] args)
 {
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  string folder=Path.GetFullPath(Path.Combine("work","annotations-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(folder);DocumentDataRepository.DataRootOverride=Path.Combine(folder,"data");
  string pdf=Path.Combine(folder,"annotated.pdf");File.Copy(args[0],pdf);string hash=DocumentDataRepository.CalculateSha256(pdf);
  var context=new ReaderApplicationContext(new[]{pdf});var form=context.Windows[0];form.AnnotationEditorOverride=m=>{m.text="文本批注示例";m.color="#D24E30";return true;};
  var timer=new Timer{Interval=1300};int stage=0;string[] ids=null;
  timer.Tick+=delegate
  {
   timer.Stop();try
   {
    var s=((List<DocumentSession>)Field(form,"sessions")).Single();var v=s.Viewer;var layer=v.AnnotationLayer;
    if(stage==0)
    {
     v.SetZoom(.65f);v.GoToPage(0,0);
     var regions=s.TextSource.GetRegions(0,new RectangleF(.08f,.03f,.8f,.08f));
     Check(regions.Count>0 && regions.All(r=>r.quad.Count==4 && r.y<.15 && r.x>=0 && r.x+r.width<1),"selected text lines use actual glyph-derived quadrilaterals");
     var rotated=s.TextSource.GetRegions(2,new RectangleF(0,0,1,1));Check(rotated.Count>0 && rotated.All(r=>r.x>-0.1 && r.x<1.1 && r.y>-.1 && r.y<1.1),"rotated cropped page text coordinates normalized");
     var tools=new[]{AnnotationTool.Highlight,AnnotationTool.Underline,AnnotationTool.Text,AnnotationTool.Ink,AnnotationTool.Rectangle,AnnotationTool.Ellipse,AnnotationTool.Line};
     var keys=new[]{Keys.H,Keys.U,Keys.T,Keys.P,Keys.R,Keys.E,Keys.L};
     var starts=new[]{new PointF(.08f,.03f),new PointF(.08f,.11f),new PointF(.2f,.25f),new PointF(.2f,.38f),new PointF(.2f,.5f),new PointF(.5f,.6f),new PointF(.2f,.72f)};
     var ends=new[]{new PointF(.85f,.1f),new PointF(.85f,.17f),new PointF(.5f,.315f),new PointF(.4f,.43f),new PointF(.48f,.56f),new PointF(.75f,.67f),new PointF(.6f,.76f)};
     for(int i=0;i<tools.Length;i++)
     {
      int count=s.Annotations.Count;Key(form,Keys.Control|Keys.Shift|keys[i]);Check(layer.Tool==tools[i],"tool selected "+tools[i]);
      Mouse(v,"OnMouseDown",starts[i]);if(tools[i]==AnnotationTool.Ink){Mouse(v,"OnMouseMove",new PointF(.25f,.41f));Mouse(v,"OnMouseMove",new PointF(.31f,.37f));}Mouse(v,"OnMouseMove",ends[i]);Mouse(v,"OnMouseUp",ends[i]);
      if(tools[i]==AnnotationTool.Text){v.InlineEditor.Input.Text="文本批注示例";v.FinishInlineEditing();}
      Check(s.Annotations.Count==count+1,"create "+tools[i]);var mark=layer.Selected;string id=mark.id;
      if(i<2)Check(mark.textRegions.Count>0 && Disk(s).Last().textRegions.Count>0,"highlights and underlines store glyph positions "+tools[i]);
      if(tools[i]==AnnotationTool.Ink)Check(mark.points.Count>=3,"handwriting stores stroke points");
      var rightPoint=Pixel(v,HitPoint(mark));Call(v,"OnMouseDown",new MouseEventArgs(MouseButtons.Right,1,rightPoint.X,rightPoint.Y,0));
      Check(layer.Tool==AnnotationTool.Select && layer.Selected?.id==id && s.Annotations.Count==count+1,"right click selects annotation without creating a mark "+tools[i]);
      Key(form,Keys.Control|Keys.Shift|Keys.A);PointF hit=HitPoint(mark);Mouse(v,"OnMouseDown",hit);Mouse(v,"OnMouseUp",hit);Check(layer.Selected?.id==id,"select "+tools[i]);
      Key(form,Keys.F2);Check(layer.Selected.text=="文本批注示例","property editing "+tools[i]);
      layer.ApplyProperties("已修改 "+tools[i],"#C13872",.6,3,15);Check(Disk(s).First(m=>m.id==id).strokeWidth==3,"save modified style "+tools[i]);
      mark=layer.Selected;hit=HitPoint(mark);double originalX=mark.x;
      Mouse(v,"OnMouseDown",hit);Mouse(v,"OnMouseMove",new PointF(hit.X+.04f,hit.Y+.025f));
      Call(form,"MarkDirty",s);Call(form,"SaveSession",s);
      Check(Math.Abs(Disk(s).First(m=>m.id==id).x-originalX)<.00001,"autosave excludes uncommitted drag preview "+tools[i]);
      Key(form,Keys.Escape);Check(Math.Abs(s.Annotations.First(m=>m.id==id).x-originalX)<.00001,"cancel move "+tools[i]);
      Key(form,Keys.Control|Keys.Shift|Keys.A);mark=s.Annotations.First(m=>m.id==id);hit=HitPoint(mark);
      Mouse(v,"OnMouseDown",hit);Mouse(v,"OnMouseMove",new PointF(hit.X+.04f,hit.Y+.025f));Mouse(v,"OnMouseUp",new PointF(hit.X+.04f,hit.Y+.025f));
      Check(layer.Selected?.id==id && layer.Selected.x>originalX+.025,"move "+tools[i]);
      mark=layer.Selected;double oldWidth=mark.width;var corner=new PointF((float)(mark.x+mark.width),(float)(mark.y+mark.height));
      Mouse(v,"OnMouseDown",corner);Mouse(v,"OnMouseMove",new PointF(corner.X+.03f,corner.Y+.02f));Mouse(v,"OnMouseUp",new PointF(corner.X+.03f,corner.Y+.02f));
      Check(layer.Selected.width>oldWidth+.015,"resize "+tools[i]);
      Key(form,Keys.Delete);Check(s.Annotations.Count==count && Disk(s).Count==count,"delete persisted "+tools[i]);
      Key(form,Keys.Control|Keys.Z);Check(s.Annotations.Count==count+1,"undo deletion "+tools[i]);
      Key(form,Keys.Control|Keys.Y);Check(s.Annotations.Count==count,"redo deletion "+tools[i]);
      Key(form,Keys.Control|Keys.Z);
     }
     Check(s.Annotations.Count==7 && Disk(s).Count==7,"all annotation types saved in the separate layer");ids=s.Annotations.Select(m=>m.id).ToArray();
     Check(s.Annotations.All(m=>m.coordinateSpace=="normalized_page" && m.x>=0 && m.y>=0 && m.x+m.width<=1.001 && m.y+m.height<=1.001),"annotations remain in normalized page coordinates");
     v.GoToPage(0,0);v.RenderPagesNearViewport();using(var image=new Bitmap(v.Width,v.Height)){v.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(folder,"annotation-layer.png"));}
     var keep=context.CreateWindow(new string[0]);form.Close();form=keep;Call(form,"OpenPdf",pdf);stage=1;timer.Start();return;
    }
    Check(s.Annotations.Count==7 && ids.All(id=>s.Annotations.Any(m=>m.id==id)),"all annotation types survive actual window close and reopen");
    Check(s.Annotations.Where(m=>m.type=="area_highlight" || m.type=="underline").All(m=>m.textRegions.Count>0),"text quadrilaterals survive reopening");
    Check(s.Annotations.First(m=>m.type=="ink").points.Count>=3,"handwriting points survive reopening");
    v.SetZoom(.65f);v.GoToPage(0,0);var note=s.Annotations.First(m=>m.type=="text_note");
    v.SetAnnotationTool(AnnotationTool.Select);var noteHit=HitPoint(note);Mouse(v,"OnMouseDown",noteHit);Mouse(v,"OnMouseUp",noteHit);
    form.AnnotationEditorOverride=m=>{m.pageIndex=1;m.text="跨页移动后";return true;};v.Focus();Key(form,Keys.F2);
    Check(Disk(s).First(m=>m.id==note.id).pageIndex==1 && v.CurrentPage==1,"properties move annotation to another page");
    v.GoToPage(0,0);form.AnnotationEditorOverride=m=>false;Key(form,Keys.Control|Keys.Shift|Keys.T);
    Mouse(v,"OnMouseDown",new PointF(.6f,.8f));Mouse(v,"OnMouseUp",new PointF(.6f,.8f));
    Check(s.Annotations.Count==7 && Disk(s).Count==7,"cancel text editor does not create an annotation");
    v.SetAnnotationTool(AnnotationTool.Select);v.GoToPage(1,0);note=s.Annotations.First(m=>m.type=="text_note");layer.Begin(1,HitPoint(note),.01f,.01f);layer.Cancel();form.AnnotationEditorOverride=null;
    bool captured=false;
    using(var dialogTimer=new Timer{Interval=150})
    {
     dialogTimer.Tick+=delegate
     {
      var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Text=="注释属性");if(dialog==null)return;dialogTimer.Stop();
      using(var shot=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(folder,"properties-dialog.png"));}
      captured=true;dialog.DialogResult=DialogResult.Cancel;dialog.Close();
     };
     dialogTimer.Start();layer.EditSelected();
    }
    Check(captured && layer.Selected.text=="跨页移动后","actual property dialog opens and Cancel preserves annotation");
    Check(DocumentDataRepository.CalculateSha256(pdf)==hash,"annotation editing does not modify source PDF");
    Console.WriteLine("ANNOTATION_ACCEPTANCE_PASS "+folder);foreach(var w in context.Windows.ToArray())w.Close();
   }
   catch(Exception e){Console.WriteLine(e);Environment.ExitCode=1;foreach(var w in context.Windows.ToArray())w.Close();}
  };
  timer.Start();Application.Run(context);timer.Dispose();return Environment.ExitCode;
 }
}
