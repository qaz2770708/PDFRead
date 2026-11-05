using System;using System.IO;using System.Linq;using System.Drawing;using System.Drawing.Imaging;using System.Collections.Generic;using PdfSharp.Pdf;using PdfSharp.Pdf.IO;using PdfSharp.Drawing;
internal static class PdfExportAcceptance
{
 [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)]static extern bool CreateHardLink(string newPath,string existing,IntPtr attributes);
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static void Throws(Action action,string message){bool failed=false;try{action();}catch(Exception){failed=true;}Check(failed,message);}
 static AreaHighlight Mark(int page,string type,double x,double y,double w,double h,string color){return new AreaHighlight{id=page+"-"+type,type=type,pageIndex=page,coordinateSpace="normalized_page",x=x,y=y,width=w,height=h,color=color,opacity=.7,strokeWidth=2,fontSize=14,fontFamily="SimSun"};}
 internal static void AddMarksForUi(VersionRecord record,int i)
 {
  var highlight=Mark(i,"area_highlight",.1,.2,.28,.05,"#FFE45C");highlight.textRegions.Add(new AnnotationRegion{x=.1,y=.2,width=.28,height=.05,quad=new List<AnnotationPoint>{new AnnotationPoint{x=.1,y=.21},new AnnotationPoint{x=.38,y=.2},new AnnotationPoint{x=.38,y=.24},new AnnotationPoint{x=.1,y=.25}}});record.annotations.Add(highlight);
  record.annotations.Add(Mark(i,"underline",.5,.2,.28,.05,"#D94A44"));
  var text=Mark(i,"text_note",.1,.34,.38,.14,"#2878D7");text.text="中文文本批注\nExport test";record.annotations.Add(text);
  var sticky=Mark(i,"sticky_note",.52,.34,.38,.17,"#C47C0B");sticky.text="便签正文";sticky.subtitle="便签副文本";record.annotations.Add(sticky);
  record.annotations.Add(Mark(i,"rectangle",.1,.56,.28,.08,"#D94A44"));record.annotations.Add(Mark(i,"ellipse",.55,.56,.28,.08,"#8051B5"));
  var ink=Mark(i,"ink",.1,.7,.28,.08,"#2E925E");ink.points.AddRange(new[]{new AnnotationPoint{x=.1,y=.76},new AnnotationPoint{x=.18,y=.7},new AnnotationPoint{x=.25,y=.78},new AnnotationPoint{x=.38,y=.72}});record.annotations.Add(ink);
  var line=Mark(i,"line",.5,.72,.3,.08,"#1F2937");line.points.AddRange(new[]{new AnnotationPoint{x=.5,y=.8},new AnnotationPoint{x=.8,y=.72}});record.annotations.Add(line);
 }
 static double Error(Bitmap actual,Bitmap expected,RectangleF region)
 {
  int x0=(int)(region.X*actual.Width),y0=(int)(region.Y*actual.Height),x1=Math.Min(actual.Width,(int)(region.Right*actual.Width)),y1=Math.Min(actual.Height,(int)(region.Bottom*actual.Height));double error=0;int n=0;
  for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++){var a=actual.GetPixel(x,y);var b=expected.GetPixel(x,y);error+=Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B);n+=3;}
  return error/Math.Max(1,n);
 }
 [STAThread]static int Main(string[] args)
 {
  try
  {
   string folder=Path.GetFullPath(Path.Combine("work","pdf-export-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(folder);DocumentDataRepository.DataRootOverride=Path.Combine(folder,"data");string source=Path.Combine(folder,"source.pdf"),target=Path.Combine(folder,"annotated.pdf");
   using(var input=PdfReader.Open(args[0],PdfDocumentOpenMode.Import))using(var fixture=new PdfDocument())
   {
    fixture.Version=16;
    for(int i=0;i<8;i++)
    {
     var p=fixture.AddPage(input.Pages[0]);var box=p.MediaBox;double x=i>=4?-30:0,y=i>=4?25:0;
     p.MediaBox=new PdfRectangle(new XRect(x,y,box.Width,box.Height));p.CropBox=new PdfRectangle(new XRect(x+25,y+35,box.Width-50,box.Height-70));p.Rotate=(i%4)*90;
     if(i>=4)p.Elements.SetReal("/UserUnit",1.5);
    }
    fixture.Save(source);
   }
   string hash=DocumentDataRepository.CalculateSha256(source);var loaded=DocumentDataRepository.LoadForPdf(source,hash,8,null);for(int i=0;i<8;i++)AddMarksForUi(loaded.Record,i);
   var session=new DocumentSession{PdfPath=source,PdfHash=hash,PageCount=8,SidecarPath=loaded.TargetPath,Data=loaded.Data,Record=loaded.Record,Annotations=loaded.Record.annotations};DocumentDataRepository.Save(session);string dataHash=DocumentDataRepository.CalculateSha256(session.SidecarPath);
   PdfAnnotationExporter.Export(source,session.SidecarPath,target,hash,false);Check(File.Exists(target),"new annotated PDF generated from source and persisted data");
   object before=WindowsPdfRenderer.Open(source,out int pages),after=WindowsPdfRenderer.Open(target,out int outputPages);
   try
   {
    Check(outputPages==pages && pages==8,"all source pages retained");
    using(var sourceDoc=PdfReader.Open(source,PdfDocumentOpenMode.Import))using(var outDoc=PdfReader.Open(target,PdfDocumentOpenMode.Import))
     for(int i=0;i<pages;i++)Check(sourceDoc.Pages[i].Rotate==outDoc.Pages[i].Rotate && sourceDoc.Pages[i].MediaBox.ToString()==outDoc.Pages[i].MediaBox.ToString() && sourceDoc.Pages[i].CropBox.ToString()==outDoc.Pages[i].CropBox.ToString(),"media crop rotation retained page "+i);
    for(int i=0;i<pages;i++)using(var original=WindowsPdfRenderer.Render(before,i,1))using(var actual=WindowsPdfRenderer.Render(after,i,1))using(var expected=(Bitmap)original.Clone())
    {
     Check(original.Size==actual.Size,"export dimensions page "+i);
     using(var g=Graphics.FromImage(expected))AnnotationLayer.PaintAppearance(g,new Rectangle(Point.Empty,expected.Size),i,1,loaded.Record.annotations);
     expected.Save(Path.Combine(folder,"expected-"+i+".png"));actual.Save(Path.Combine(folder,"export-"+i+".png"));
     foreach(var mark in loaded.Record.annotations.Where(m=>m.pageIndex==i))
     {
      var region=new RectangleF((float)Math.Max(0,mark.x-.01),(float)Math.Max(0,mark.y-.01),(float)Math.Min(1-mark.x+.01,mark.width+.02),(float)Math.Min(1-mark.y+.01,mark.height+.02));
      double error=Error(actual,expected,region),signal=Error(original,expected,region);Check(error<5 && error<signal*.65,"appearance "+mark.type+" page "+i+" error="+error.ToString("F2")+" signal="+signal.ToString("F2"));
     }
    }
   }
   finally{WindowsPdfRenderer.DisposeDocument(before);WindowsPdfRenderer.DisposeDocument(after);}
   using(var originalText=new PdfTextRegions(source))using(var exportedText=new PdfTextRegions(target))Check(exportedText.GetRegions(0,new RectangleF(0,0,1,1)).Count==originalText.GetRegions(0,new RectangleF(0,0,1,1)).Count,"original searchable text preserved");
   Throws(()=>PdfAnnotationExporter.Export(source,session.SidecarPath,source,hash,true),"cannot overwrite original PDF");
   string alias=Path.Combine(folder,"source-hardlink.pdf");Check(CreateHardLink(alias,source,IntPtr.Zero),"hardlink source alias fixture created");Throws(()=>PdfAnnotationExporter.Export(source,session.SidecarPath,alias,hash,true),"file identity protects source through hardlink alias");
   string dataAlias=Path.Combine(folder,"data-hardlink.pdf");Check(CreateHardLink(dataAlias,session.SidecarPath,IntPtr.Zero),"data alias fixture created");Throws(()=>PdfAnnotationExporter.Export(source,session.SidecarPath,dataAlias,hash,true),"file identity also protects sidecar aliases");
   string outputHash=DocumentDataRepository.CalculateSha256(target);Throws(()=>PdfAnnotationExporter.Export(source,session.SidecarPath,target,hash,false),"existing target requires overwrite permission");Check(DocumentDataRepository.CalculateSha256(target)==outputHash,"unconfirmed target left intact");
   using(var locked=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read))Throws(()=>PdfAnnotationExporter.Export(source,session.SidecarPath,target,hash,true),"locked target fails without partial overwrite");Check(DocumentDataRepository.CalculateSha256(target)==outputHash,"failed export preserves valid target");
   PdfAnnotationExporter.Export(source,session.SidecarPath,target,hash,true);Check(!Directory.GetFiles(folder,".pdfread-*.pdf").Any(),"confirmed overwrite succeeds and scratch PDFs are cleaned");
   Throws(()=>PdfAnnotationExporter.Export(source,session.SidecarPath,Path.Combine(folder,"wrong.pdf"),new string('a',64),false),"mismatched PDF SHA prevents export");
   Check(DocumentDataRepository.CalculateSha256(source)==hash && DocumentDataRepository.CalculateSha256(session.SidecarPath)==dataHash,"original PDF and sidecar unchanged by export");Console.WriteLine("PDF_EXPORT_ACCEPTANCE_PASS "+folder);return 0;
  }catch(Exception ex){Console.WriteLine(ex);return 1;}
 }
}
