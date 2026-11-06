using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;

internal sealed class SelectableWord
{
    public string text;public double x,y,width,height,angle;
}
internal sealed class SelectableText
{
    public SelectableWord[] words=new SelectableWord[0];public string error;public string source="pdf";
}
// Local OCR fallback consumes the same rendered page as the reader, never modifies the PDF.
internal static class WindowsTextRecognition
{
    private static Type Win(string name){return Type.GetType(name+", Windows.Foundation, ContentType=WindowsRuntime",true);}
    private static object Wait(object op,Type result)
    {
        var method=typeof(System.WindowsRuntimeSystemExtensions).GetMethods().First(m=>m.Name=="AsTask"&&m.IsGenericMethod&&m.GetParameters().Length==1);
        var task=(Task)method.MakeGenericMethod(result).Invoke(null,new[]{op});task.GetAwaiter().GetResult();return task.GetType().GetProperty("Result").GetValue(task,null);
    }
    internal static SelectableText Read(string path,int page)
    {
        object document=null,software=null,random=null;
        try
        {
            Type engineType=Win("Windows.Media.Ocr.OcrEngine");var engine=engineType.GetMethod("TryCreateFromUserProfileLanguages").Invoke(null,null);
            if(engine==null)return new SelectableText{source="ocr",error="Windows OCR 缺少可用识别语言，请在 Windows 语言设置中安装文字识别组件后重试。"};
            int count;document=WindowsPdfRenderer.Open(path,out count);var size=WindowsPdfRenderer.GetPageSize(document,page);
            double limit=Convert.ToDouble(engineType.GetProperty("MaxImageDimension").GetValue(null,null));float scale=(float)Math.Min(2.5,Math.Min((limit-1)/Math.Max(size.Width,size.Height),Math.Sqrt(10000000.0/(size.Width*size.Height))));
            using(var bitmap=WindowsPdfRenderer.Render(document,page,scale))using(var memory=new MemoryStream())
            {
                bitmap.Save(memory,ImageFormat.Png);memory.Position=0;random=typeof(System.IO.WindowsRuntimeStreamExtensions).GetMethod("AsRandomAccessStream").Invoke(null,new object[]{memory});
                var decoderType=Win("Windows.Graphics.Imaging.BitmapDecoder");var decoder=Wait(decoderType.GetMethods().First(m=>m.Name=="CreateAsync"&&m.GetParameters().Length==1).Invoke(null,new[]{random}),decoderType);
                var softwareType=Win("Windows.Graphics.Imaging.SoftwareBitmap");software=Wait(decoderType.GetMethods().First(m=>m.Name=="GetSoftwareBitmapAsync"&&m.GetParameters().Length==0).Invoke(decoder,null),softwareType);
                var result=Wait(engineType.GetMethod("RecognizeAsync").Invoke(engine,new[]{software}),Win("Windows.Media.Ocr.OcrResult"));var words=new List<SelectableWord>();
                foreach(var line in (IEnumerable)result.GetType().GetProperty("Lines").GetValue(result,null))foreach(var word in (IEnumerable)line.GetType().GetProperty("Words").GetValue(line,null))
                {
                    var r=word.GetType().GetProperty("BoundingRect").GetValue(word,null);var t=r.GetType();Func<string,double> v=n=>Convert.ToDouble(t.GetProperty(n).GetValue(r,null));
                    words.Add(new SelectableWord{text=(string)word.GetType().GetProperty("Text").GetValue(word,null),x=v("X")/bitmap.Width,y=v("Y")/bitmap.Height,width=v("Width")/bitmap.Width,height=v("Height")/bitmap.Height});
                }
                return new SelectableText{source="ocr",words=words.ToArray(),error=words.Count==0?"当前页未识别到文字，可重试 OCR；空白页或低清晰度图像可能无法识别。":null};
            }
        }
        catch(Exception e){while(e is TargetInvocationException&&e.InnerException!=null)e=e.InnerException;return new SelectableText{source="ocr",error="本页 OCR 未完成："+e.Message+"，可点击重试。"};}
        finally{(software as IDisposable)?.Dispose();(random as IDisposable)?.Dispose();if(document!=null)WindowsPdfRenderer.DisposeDocument(document);}
    }
}
