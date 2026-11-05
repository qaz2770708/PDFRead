using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

// Source pages stay vector PDF content; only the separate annotation appearance
// is painted to a transparent high-resolution overlay. No editor chrome is drawn.
internal static class PdfAnnotationExporter
{
    private const double PreferredScale=3; // 288 dpi, matching the reader's 96 dpi page coordinate space.
    private const double MaxOverlayPixels=16000000;
    [StructLayout(LayoutKind.Sequential)]private struct FileIdentity
    {
        public uint Attributes;public System.Runtime.InteropServices.ComTypes.FILETIME Creation,Access,Write;
        public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;
    }
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetFileInformationByHandle(SafeFileHandle handle,out FileIdentity identity);
    private static FileIdentity Identity(string path)
    {
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
        {FileIdentity identity;if(!GetFileInformationByHandle(stream.SafeFileHandle,out identity))throw new IOException("无法核对文件身份。",new System.ComponentModel.Win32Exception());return identity;}
    }
    internal static void ValidateDestination(string source,string data,string target)
    {
        if(!String.Equals(Path.GetExtension(target),".pdf",StringComparison.OrdinalIgnoreCase))throw new IOException("导出文件必须为 .pdf。");
        string output=Path.GetFullPath(target);
        if(String.Equals(output,Path.GetFullPath(source),StringComparison.OrdinalIgnoreCase)||String.Equals(output,Path.GetFullPath(data),StringComparison.OrdinalIgnoreCase))throw new IOException("请选择新的文件名，不能覆盖原 PDF 或数据文件。");
        if(File.Exists(output))
        {
            var a=Identity(source);var b=Identity(output);
            if(a.Volume==b.Volume&&a.IndexHigh==b.IndexHigh&&a.IndexLow==b.IndexLow)throw new IOException("目标与原 PDF 指向同一文件，请选择新的导出文件。");
            if(File.Exists(data)){var d=Identity(data);if(d.Volume==b.Volume&&d.IndexHigh==b.IndexHigh&&d.IndexLow==b.IndexLow)throw new IOException("目标与数据文件指向同一文件，请选择新的导出文件。");}
        }
    }
    internal static void Export(string source,string data,string target,string expectedHash,bool overwrite)
    {
        ValidateDestination(source,data,target);string full=Path.GetFullPath(target);
        if(File.Exists(full)&&!overwrite)throw new IOException("目标文件已存在，尚未确认覆盖。");
        string destinationFingerprint=DocumentDataRepository.Fingerprint(full),dataFingerprint=DocumentDataRepository.Fingerprint(data);
        if(DocumentDataRepository.CalculateSha256(source)!=expectedHash)throw new IOException("原 PDF 内容已改变，请重新打开并确认数据版本后导出。");
        VersionRecord record=DocumentDataRepository.ReadExportRecord(data,expectedHash);
        Directory.CreateDirectory(Path.GetDirectoryName(full));string temporary=Path.Combine(Path.GetDirectoryName(full),".pdfread-"+Guid.NewGuid().ToString("N")+".pdf");
        object renderedSource=null;
        try
        {
            renderedSource=WindowsPdfRenderer.Open(source,out int count);
            if(record.pageCount!=count)throw new InvalidDataException("PDF 页数与数据版本不匹配，已停止导出。");
            using(var document=PdfReader.Open(source,PdfDocumentOpenMode.Modify))
            {
                if(document.PageCount!=count)throw new InvalidDataException("PDF 页面解析结果不一致。");
                document.Version=Math.Max(document.Version,14);
                foreach(var pageIndex in record.annotations.Select(a=>a.pageIndex).Distinct().OrderBy(i=>i))
                {
                    var size=WindowsPdfRenderer.GetPageSize(renderedSource,pageIndex);
                    double scale=Math.Min(PreferredScale,Math.Min(12000/Math.Max(size.Width,size.Height),Math.Sqrt(MaxOverlayPixels/((double)size.Width*size.Height))));
                    if(size.Width<=0||size.Height<=0||Double.IsNaN(scale)||scale<=0)throw new InvalidDataException("页面尺寸无效，无法导出注释。");
                    int width=Math.Max(1,(int)Math.Ceiling(size.Width*scale)),height=Math.Max(1,(int)Math.Ceiling(size.Height*scale));
                    using(var overlay=new Bitmap(width,height,PixelFormat.Format32bppArgb))
                    using(var pixels=Graphics.FromImage(overlay))
                    using(var png=new MemoryStream())
                    {
                        overlay.SetResolution(96,96);pixels.Clear(Color.Transparent);
                        AnnotationLayer.PaintAppearance(pixels,new Rectangle(0,0,width,height),pageIndex,(float)scale,record.annotations);
                        overlay.Save(png,ImageFormat.Png);png.Position=0;
                        using(var image=XImage.FromStream(png))AppendOverlay(document.Pages[pageIndex],image);
                    }
                }
                document.Save(temporary);
            }
            using(var durable=new FileStream(temporary,FileMode.Open,FileAccess.ReadWrite,FileShare.None))durable.Flush(true);
            using(var verified=PdfReader.Open(temporary,PdfDocumentOpenMode.Import))if(verified.PageCount!=count)throw new InvalidDataException("导出后的 PDF 页数校验失败。");
            if(DocumentDataRepository.CalculateSha256(source)!=expectedHash || DocumentDataRepository.Fingerprint(data)!=dataFingerprint)throw new IOException("导出期间原 PDF 或数据发生变化，目标未覆盖，请重新加载后再试。");
            if(DocumentDataRepository.Fingerprint(full)!=destinationFingerprint)throw new IOException("导出期间目标文件发生变化，已停止覆盖。");
            ValidateDestination(source,data,full);
            if(File.Exists(full))File.Replace(temporary,full,null);else File.Move(temporary,full);
        }
        finally
        {
            if(renderedSource!=null)WindowsPdfRenderer.DisposeDocument(renderedSource);
            if(File.Exists(temporary))File.Delete(temporary);
        }
    }
    private static void AppendOverlay(PdfPage page,XImage overlay)
    {
        PdfRectangle media=page.MediaBox,crop=page.CropBox;if(crop.Width<=0||crop.Height<=0)crop=media;
        int rotation=page.Rotate;
        Func<double,double,XPoint> map=(x,y)=>
        {
            double u=x,v=1-y;
            switch((rotation%360+360)%360)
            {case 90:u=y;v=x;break;case 180:u=1-x;v=y;break;case 270:u=1-y;v=1-x;break;}
            return new XPoint(crop.X1+u*crop.Width,media.Height-(crop.Y1+v*crop.Height));
        };
        // Temporarily neutralize page metadata so XGraphics writes into unrotated
        // PDF user space. Restore the original boxes/rotation before saving.
        try
        {
            page.Rotate=0;page.MediaBox=new PdfRectangle(new XRect(0,0,media.Width,media.Height));
            using(var graphics=XGraphics.FromPdfPage(page,XGraphicsPdfPageOptions.Append))
            {
                XPoint origin=map(0,0),right=map(1,0),bottom=map(0,1);
                graphics.MultiplyTransform(new XMatrix(right.X-origin.X,right.Y-origin.Y,bottom.X-origin.X,bottom.Y-origin.Y,origin.X,origin.Y));
                graphics.DrawImage(overlay,0,0,1,1);
            }
        }
        finally{page.MediaBox=media;page.Rotate=rotation;}
    }
}
