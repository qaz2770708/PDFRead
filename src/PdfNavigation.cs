using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Advanced;

internal sealed class NavigationTarget
{
    public string Title;
    public int PageIndex = -1;
    public double PageOffset;
    public double HorizontalOffset;
    public float? Zoom;
    public string Fit;
    public List<NavigationTarget> Children = new List<NavigationTarget>();
}
internal sealed class PageLink
{
    public int PageIndex;
    public RectangleF Bounds;
    public NavigationTarget Target;
}
internal sealed class PdfNavigationData
{
    public List<NavigationTarget> Outline = new List<NavigationTarget>();
    public List<PageLink> Links = new List<PageLink>();
    public string Error;
}
internal static class PdfNavigationReader
{
    public static PdfNavigationData Read(string path)
    {
        var result = new PdfNavigationData();
        try
        {
            using (PdfDocument doc = PdfReader.Open(path, PdfDocumentOpenMode.Import))
            {
                var parser = new Parser(doc);
                PdfDictionary catalog = doc.Internals.Catalog;
                parser.ReadNames(Resolve(catalog.Elements["/Names"]) as PdfDictionary);
                var legacy = Resolve(catalog.Elements["/Dests"]) as PdfDictionary;
                if (legacy != null) foreach (string key in legacy.Elements.Keys) parser.Names[key.TrimStart('/')] = legacy.Elements[key];
                var outlines = Resolve(catalog.Elements["/Outlines"]) as PdfDictionary;
                if (outlines != null) parser.ReadOutline(outlines.Elements["/First"], result.Outline, 0);
                for (int i = 0; i < doc.PageCount; i++)
                {
                    var annots = Resolve(doc.Pages[i].Elements["/Annots"]) as PdfArray;
                    if (annots == null) continue;
                    foreach (var raw in annots.Elements)
                    {
                        var link = Resolve(raw) as PdfDictionary;
                        if (link == null || link.Elements.GetName("/Subtype") != "/Link") continue;
                        var target = parser.Target(link);
                        var rect = Resolve(link.Elements["/Rect"]) as PdfArray;
                        if (target.PageIndex < 0 || rect == null || rect.Elements.Count < 4) continue;
                        PointF a = parser.Position(i, Number(rect.Elements[0]) ?? 0, Number(rect.Elements[1]) ?? 0);
                        PointF b = parser.Position(i, Number(rect.Elements[2]) ?? 0, Number(rect.Elements[3]) ?? 0);
                        result.Links.Add(new PageLink { PageIndex = i, Target = target,
                            Bounds = RectangleF.FromLTRB(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Max(a.X,b.X),Math.Max(a.Y,b.Y)) });
                    }
                }
            }
        }
        catch (Exception e) { result.Error = "PDF 目录读取失败：" + e.Message; }
        return result;
    }
    private static PdfItem Resolve(PdfItem item) { var r = item as PdfReference; return r == null ? item : r.Value; }
    private static double? Number(PdfItem item)
    {
        item = Resolve(item);
        if (item is PdfReal) return ((PdfReal)item).Value;
        if (item is PdfInteger) return ((PdfInteger)item).Value;
        return null;
    }
    private sealed class Parser
    {
        private readonly PdfDocument doc;
        private readonly Dictionary<string,int> pages = new Dictionary<string,int>();
        private readonly HashSet<PdfDictionary> visited = new HashSet<PdfDictionary>();
        public readonly Dictionary<string,PdfItem> Names = new Dictionary<string,PdfItem>(StringComparer.Ordinal);
        public Parser(PdfDocument d)
        {
            doc = d;
            for (int i=0;i<d.PageCount;i++) pages[d.Pages[i].Reference.ObjectID.ToString()] = i;
        }
        public void ReadNames(PdfDictionary names)
        {
            if (names != null) ReadNameTree(Resolve(names.Elements["/Dests"]) as PdfDictionary, new HashSet<PdfDictionary>(), 0);
        }
        private void ReadNameTree(PdfDictionary tree, HashSet<PdfDictionary> seen, int depth)
        {
            if (tree == null || depth > 64 || seen.Count > 10000 || !seen.Add(tree)) return;
            var pairs = Resolve(tree.Elements["/Names"]) as PdfArray;
            if (pairs != null) for(int i=0;i+1<pairs.Elements.Count;i+=2)
            {
                var name = Resolve(pairs.Elements[i]) as PdfString;
                if(name != null) Names[name.Value] = pairs.Elements[i+1];
            }
            var kids = Resolve(tree.Elements["/Kids"]) as PdfArray;
            if(kids != null) foreach(var child in kids.Elements) ReadNameTree(Resolve(child) as PdfDictionary,seen,depth+1);
        }
        public void ReadOutline(PdfItem item, List<NavigationTarget> list, int depth)
        {
            if(depth>64) return;
            while(item != null)
            {
                var node = Resolve(item) as PdfDictionary;
                if(node == null || visited.Count >= 10000 || !visited.Add(node)) return;
                var target = Target(node);
                target.Title = node.Elements.GetString("/Title");
                if(String.IsNullOrWhiteSpace(target.Title)) target.Title = "未命名章节";
                list.Add(target);
                ReadOutline(node.Elements["/First"], target.Children,depth+1);
                item = node.Elements["/Next"];
            }
        }
        public NavigationTarget Target(PdfDictionary node)
        {
            var target = new NavigationTarget();
            PdfItem dest = node.Elements["/Dest"];
            if(dest == null)
            {
                var action = Resolve(node.Elements["/A"]) as PdfDictionary;
                if(action == null || action.Elements.GetName("/S") != "/GoTo") return target;
                dest = action.Elements["/D"];
            }
            for(int loop=0;loop<16 && dest!=null;loop++)
            {
                dest = Resolve(dest);
                var name = dest as PdfName;
                var text = dest as PdfString;
                if(name != null || text != null)
                {
                    if(!Names.TryGetValue(name != null ? name.Value.TrimStart('/') : text.Value,out dest)) return target;
                    continue;
                }
                var dict = dest as PdfDictionary;
                if(dict != null) { dest=dict.Elements["/D"]; continue; }
                break;
            }
            var array = dest as PdfArray;
            if(array == null || array.Elements.Count < 2) return target;
            var pageRef = array.Elements[0] as PdfReference;
            int index;
            if(pageRef == null || !pages.TryGetValue(pageRef.ObjectID.ToString(),out index)) return target;
            target.PageIndex = index;
            var mode = Resolve(array.Elements[1]) as PdfName;
            string type = mode == null ? "" : mode.Value;
            double? left=null, top=null;
            if(type=="/XYZ")
            {
                if(array.Elements.Count>2) left=Number(array.Elements[2]);
                if(array.Elements.Count>3) top=Number(array.Elements[3]);
                if(array.Elements.Count>4) { var z=Number(array.Elements[4]); if(z>0) target.Zoom=(float)z; }
            }
            else if(type=="/FitH" || type=="/FitBH") { target.Fit="width"; if(array.Elements.Count>2) top=Number(array.Elements[2]); }
            else if(type=="/Fit" || type=="/FitB") target.Fit="page";
            else if(type=="/FitR" && array.Elements.Count>5) { left=Number(array.Elements[2]);top=Number(array.Elements[5]); }
            else if(type=="/FitV" || type=="/FitBV") { if(array.Elements.Count>2) left=Number(array.Elements[2]); }
            PdfRectangle crop=doc.Pages[index].CropBox;
            PointF point=Position(index,left??crop.X1,top??crop.Y2);
            target.HorizontalOffset=left.HasValue?Clamp(point.X):0;
            target.PageOffset=top.HasValue?Clamp(point.Y):0;
            return target;
        }
        public PointF Position(int index,double x,double y)
        {
            PdfPage page=doc.Pages[index]; PdfRectangle box=page.CropBox;
            if(box.Width<=0 || box.Height<=0) box=page.MediaBox;
            double u=(x-box.X1)/box.Width,v=(y-box.Y1)/box.Height;
            switch((page.Rotate%360+360)%360)
            {
                case 90:return new PointF((float)v,(float)u);
                case 180:return new PointF((float)(1-u),(float)v);
                case 270:return new PointF((float)(1-v),(float)(1-u));
                default:return new PointF((float)u,(float)(1-v));
            }
        }
        private static double Clamp(double v) { return Math.Max(0,Math.Min(1,v)); }
    }
}
