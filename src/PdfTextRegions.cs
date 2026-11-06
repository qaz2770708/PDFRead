using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using UglyToad.PdfPig;

internal sealed class PdfTextRegions : IDisposable
{
    private readonly string path;
    private PdfDocument document;
    private bool failed;
    private readonly Dictionary<int,object> selectablePages=new Dictionary<int,object>();
    private readonly Dictionary<int,List<AnnotationRegion>> pages=new Dictionary<int,List<AnnotationRegion>>();
    public PdfTextRegions(string pdfPath){path=pdfPath;}
    internal object GetSelectableText(int pageIndex)
    {
        object cached;if(selectablePages.TryGetValue(pageIndex,out cached))return cached;
        try
        {
            if(document==null)document=PdfDocument.Open(path);
            var page=document.GetPage(pageIndex+1);
            var words=page.GetWords().Where(w=>!String.IsNullOrWhiteSpace(w.Text)).Select(w=>
            {
                var r=w.BoundingBox;double dx=r.TopRight.X-r.TopLeft.X,dy=r.TopRight.Y-r.TopLeft.Y;
                return new {text=w.Text,x=r.TopLeft.X/page.Width,y=1-r.TopLeft.Y/page.Height,
                    width=Math.Sqrt(dx*dx+dy*dy)/page.Width,height=Math.Sqrt(Math.Pow(r.BottomLeft.X-r.TopLeft.X,2)+Math.Pow(r.BottomLeft.Y-r.TopLeft.Y,2))/page.Height,
                    angle=Math.Atan2(-dy,dx)*180/Math.PI};
            }).Where(w=>w.width>0&&w.height>0).ToArray();
            var result=new {words,error=words.Length==0?"此页没有可选择的文字，扫描页需要 OCR。":null};
            if(selectablePages.Count>=16)selectablePages.Remove(selectablePages.Keys.First());selectablePages[pageIndex]=result;return result;
        }
        catch(Exception ex){System.Diagnostics.Debug.WriteLine(ex.Message);return new {words=new object[0],error="此页文字无法解析，仍可阅读和批注。"};}
    }
    public List<AnnotationRegion> GetRegions(int pageIndex,RectangleF selection)
    {
        try
        {
            if(failed)return new List<AnnotationRegion>();
            if(document==null)document=PdfDocument.Open(path);
            List<AnnotationRegion> regions;
            if(!pages.TryGetValue(pageIndex,out regions))
            {
                var page=document.GetPage(pageIndex+1);regions=new List<AnnotationRegion>();
                foreach(var letter in page.Letters)
                {
                    if(String.IsNullOrWhiteSpace(letter.Value))continue;
                    var r=letter.BoundingBox;
                    var points=new[]{r.TopLeft,r.TopRight,r.BottomRight,r.BottomLeft}.Select(p=>new AnnotationPoint{x=p.X/page.Width,y=1-p.Y/page.Height}).ToList();
                    double left=points.Min(p=>p.x),top=points.Min(p=>p.y),right=points.Max(p=>p.x),bottom=points.Max(p=>p.y);
                    if(right<=0 || bottom<=0 || left>=1 || top>=1 || right-left<.00001 || bottom-top<.00001)continue;
                    foreach(var point in points){point.x=Math.Max(0,Math.Min(1,point.x));point.y=Math.Max(0,Math.Min(1,point.y));}
                    left=Math.Max(0,left);top=Math.Max(0,top);right=Math.Min(1,right);bottom=Math.Min(1,bottom);
                    regions.Add(new AnnotationRegion{x=left,y=top,width=right-left,height=bottom-top,quad=points});
                }
                if(pages.Count>=16)pages.Remove(pages.Keys.First());pages[pageIndex]=regions;
            }
            return MergeLines(regions.Where(r=>selection.Contains((float)(r.x+r.width/2),(float)(r.y+r.height/2))).ToList());
        }
        catch(Exception ex){failed=true;System.Diagnostics.Debug.WriteLine("Text regions: "+ex.Message);return new List<AnnotationRegion>();}
    }
    public void Dispose(){document?.Dispose();document=null;pages.Clear();selectablePages.Clear();}
    private static List<AnnotationRegion> MergeLines(List<AnnotationRegion> selected)
    {
        var result=new List<AnnotationRegion>();
        foreach(bool horizontal in new[]{true,false})
        {
            var lines=new List<List<AnnotationRegion>>();
            foreach(var r in selected.Where(r=>(Math.Abs(r.quad[1].x-r.quad[0].x)>=Math.Abs(r.quad[1].y-r.quad[0].y))==horizontal))
            {
                double center=horizontal?r.y+r.height/2:r.x+r.width/2;
                var line=lines.FirstOrDefault(g=>Math.Abs((horizontal?g[0].y+g[0].height/2:g[0].x+g[0].width/2)-center)<Math.Max(horizontal?g[0].height:g[0].width,horizontal?r.height:r.width)*.7);
                if(line==null){line=new List<AnnotationRegion>();lines.Add(line);}line.Add(r);
            }
            foreach(var line in lines)
            {
                var ordered=line.OrderBy(r=>horizontal?r.x:r.y).ToList();var segment=new List<AnnotationRegion>();double end=Double.MinValue;
                foreach(var r in ordered)
                {
                    double first=horizontal?r.x:r.y,thickness=horizontal?r.height:r.width;
                    if(segment.Count>0 && first-end>thickness*2){result.Add(UnionRegion(segment,horizontal));segment.Clear();}
                    segment.Add(r);end=horizontal?r.x+r.width:r.y+r.height;
                }
                if(segment.Count>0)result.Add(UnionRegion(segment,horizontal));
            }
        }
        return result;
    }
    private static AnnotationRegion UnionRegion(List<AnnotationRegion> line,bool horizontal)
    {
        double left=line.Min(r=>r.x),top=line.Min(r=>r.y),right=line.Max(r=>r.x+r.width),bottom=line.Max(r=>r.y+r.height);
        var tl=new AnnotationPoint{x=left,y=top};var tr=new AnnotationPoint{x=right,y=top};var br=new AnnotationPoint{x=right,y=bottom};var bl=new AnnotationPoint{x=left,y=bottom};
        var first=line[0].quad;List<AnnotationPoint> quad;
        if(horizontal)quad=first[1].x>=first[0].x?new List<AnnotationPoint>{tl,tr,br,bl}:new List<AnnotationPoint>{br,bl,tl,tr};
        else quad=first[1].y>=first[0].y?new List<AnnotationPoint>{tr,br,bl,tl}:new List<AnnotationPoint>{bl,tl,tr,br};
        return new AnnotationRegion{x=left,y=top,width=right-left,height=bottom-top,quad=quad};
    }
}
