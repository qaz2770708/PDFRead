using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Web.Script.Serialization;

internal enum AnnotationTool { Select, Highlight, Underline, Text, Ink, Rectangle, Ellipse, Line }
internal sealed class AnnotationEditEventArgs : EventArgs
{
    public AreaHighlight Annotation;
    public bool Cancel;
}

// Owns editable page geometry and history. It never writes a PDF or renders its contents.
internal sealed class AnnotationLayer
{
    private List<AreaHighlight> marks = new List<AreaHighlight>();
    private readonly List<List<AreaHighlight>> undo = new List<List<AreaHighlight>>();
    private readonly List<List<AreaHighlight>> redo = new List<List<AreaHighlight>>();
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength=Int32.MaxValue, RecursionLimit=128 };
    private List<AreaHighlight> before;
    private AreaHighlight preview, original;
    private PointF start;
    private int page;
    private int corner = -1;
    private bool gesture, moving;
    public AnnotationTool Tool { get; private set; }
    public AreaHighlight Selected { get; private set; }
    public bool CanUndo { get { return undo.Count>0; } }
    public bool CanRedo { get { return redo.Count>0; } }
    public Func<int,RectangleF,List<AnnotationRegion>> TextRegions;
    public event EventHandler Changed;
    public event EventHandler SelectionChanged;
    public event EventHandler<AnnotationEditEventArgs> EditRequested;
    public event EventHandler<AreaHighlight> TextDraftCreated;
    private AreaHighlight inlinePreview;
    public AreaHighlight Find(string id){return marks.FirstOrDefault(m=>m.id==id);}
    public void SetInlinePreview(AreaHighlight value){inlinePreview=value;}
    public void SetMarks(List<AreaHighlight> source) { Cancel(); marks=source;Selected=null;undo.Clear();redo.Clear();SelectionChanged?.Invoke(this,EventArgs.Empty); }
    public void SetTool(AnnotationTool tool) { Cancel();Tool=tool;if(tool!=AnnotationTool.Select)Selected=null;SelectionChanged?.Invoke(this,EventArgs.Empty); }
    private static T Copy<T>(T value) { return Json.Deserialize<T>(Json.Serialize(value)); }
    private static RectangleF Bounds(AreaHighlight m) { return new RectangleF((float)m.x,(float)m.y,(float)m.width,(float)m.height); }
    private static PointF Clamp(PointF p) { return new PointF(Math.Max(0,Math.Min(1,p.X)),Math.Max(0,Math.Min(1,p.Y))); }
    private static RectangleF Box(PointF a,PointF b) { return RectangleF.FromLTRB(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Max(a.X,b.X),Math.Max(a.Y,b.Y)); }
    private static PointF[] Corners(RectangleF b) { return new[]{new PointF(b.Left,b.Top),new PointF(b.Right,b.Top),new PointF(b.Right,b.Bottom),new PointF(b.Left,b.Bottom)}; }
    private static string TypeFor(AnnotationTool t) { return t==AnnotationTool.Highlight?"area_highlight":t==AnnotationTool.Text?"text_note":t.ToString().ToLowerInvariant(); }
    public AreaHighlight HitTest(int index,PointF p,float tx,float ty)
    {
        for(int i=marks.Count-1;i>=0;i--)
        {
            var m=marks[i];if(m.pageIndex!=index)continue;
            RectangleF box=Bounds(m);box.Inflate(tx,ty);if(!box.Contains(p))continue;
            if((m.type=="ink" || m.type=="line") && m.points!=null && m.points.Count>0)
            {
                bool hit=false;
                for(int n=1;n<m.points.Count;n++)
                {
                    PointF a=new PointF((float)m.points[n-1].x/tx,(float)m.points[n-1].y/ty),b=new PointF((float)m.points[n].x/tx,(float)m.points[n].y/ty),q=new PointF(p.X/tx,p.Y/ty);
                    double dx=b.X-a.X,dy=b.Y-a.Y,length=dx*dx+dy*dy;
                    double t=length==0?0:Math.Max(0,Math.Min(1,((q.X-a.X)*dx+(q.Y-a.Y)*dy)/length));
                    if(Math.Pow(q.X-a.X-t*dx,2)+Math.Pow(q.Y-a.Y-t*dy,2)<=1) { hit=true;break; }
                }
                if(!hit && m.points.Count>1)continue;
            }
            return m;
        }
        return null;
    }
    public bool Begin(int index,PointF p,float tx,float ty)
    {
        Cancel();page=index;start=Clamp(p);corner=-1;moving=false;
        if(Tool==AnnotationTool.Select)
        {
            if(Selected!=null && Selected.pageIndex==index)
            {
                var corners=Corners(Bounds(Selected));
                for(int i=0;i<4;i++)if(Math.Abs(corners[i].X-p.X)<=tx && Math.Abs(corners[i].Y-p.Y)<=ty){corner=i;break;}
            }
            if(corner<0)Selected=HitTest(index,p,tx,ty);
            SelectionChanged?.Invoke(this,EventArgs.Empty);
            if(Selected==null)return false;
            before=Copy(marks);original=Copy(Selected);moving=true;gesture=true;return true;
        }
        before=Copy(marks);gesture=true;
        preview=new AreaHighlight{id=Guid.NewGuid().ToString("N"),type=TypeFor(Tool),pageIndex=index,coordinateSpace="normalized_page",x=p.X,y=p.Y,width=.001,height=.001,color=Tool==AnnotationTool.Highlight?"#FFE45C":"#2878D7",opacity=Tool==AnnotationTool.Highlight?.45:.95,strokeWidth=2,fontSize=12,createdUtc=DateTime.UtcNow.ToString("o")};
        if(Tool==AnnotationTool.Ink || Tool==AnnotationTool.Line)preview.points.Add(new AnnotationPoint{x=p.X,y=p.Y});
        return true;
    }
    public void Move(PointF point)
    {
        if(!gesture)return;PointF p=Clamp(point);
        if(moving)
        {
            if(Math.Abs(p.X-start.X)+Math.Abs(p.Y-start.Y)<.00001){preview=null;return;}
            RectangleF old=Bounds(original),next;
            if(corner<0)
            {
                float dx=Math.Max(-old.Left,Math.Min(1-old.Right,p.X-start.X));float dy=Math.Max(-old.Top,Math.Min(1-old.Bottom,p.Y-start.Y));
                next=new RectangleF(old.X+dx,old.Y+dy,old.Width,old.Height);
            }
            else
            {
                PointF fixedPoint=Corners(old)[(corner+2)%4];next=Box(fixedPoint,p);next.Width=Math.Max(.002f,next.Width);next.Height=Math.Max(.002f,next.Height);
            }
            var replacement=Copy(original);Transform(replacement,next);preview=replacement;return;
        }
        if(Tool==AnnotationTool.Ink)
        {
            var last=preview.points.Last();if(Math.Abs(last.x-p.X)+Math.Abs(last.y-p.Y)>.0005)preview.points.Add(new AnnotationPoint{x=p.X,y=p.Y});
            SetBounds(preview,PointsBox(preview.points));
        }
        else
        {
            SetBounds(preview,Box(start,p));
            if(Tool==AnnotationTool.Line)preview.points=new List<AnnotationPoint>{new AnnotationPoint{x=start.X,y=start.Y},new AnnotationPoint{x=p.X,y=p.Y}};
        }
    }
    public void End(PointF point)
    {
        if(!gesture)return;Move(point);gesture=false;
        if(moving)
        {
            if(preview!=null && Json.Serialize(original)!=Json.Serialize(preview)){ReplaceSelected(preview);preview=null;Commit(before);}
            preview=null;
            before=null;moving=false;original=null;return;
        }
        var item=preview;preview=null;
        if(item==null)return;
        if(Tool==AnnotationTool.Text && item.width<.004 && item.height<.004)
            SetBounds(item,new RectangleF(Math.Min(start.X,.76f),Math.Min(start.Y,.92f),.22f,.07f));
        if(Tool!=AnnotationTool.Ink && Tool!=AnnotationTool.Line && (item.width<.002 || item.height<.002)){before=null;return;}
        if(item.type=="area_highlight" || item.type=="underline")
        {
            item.textRegions=TextRegions?.Invoke(page,Bounds(item)) ?? new List<AnnotationRegion>();
            if(item.textRegions.Count>0)
            {
                RectangleF union=new RectangleF((float)item.textRegions[0].x,(float)item.textRegions[0].y,(float)item.textRegions[0].width,(float)item.textRegions[0].height);
                foreach(var r in item.textRegions)union=RectangleF.Union(union,new RectangleF((float)r.x,(float)r.y,(float)r.width,(float)r.height));
                SetBounds(item,union);
            }
        }
        if(item.type=="text_note")
        {
            item.text=String.Empty;item.fontFamily="SimSun";before=null;Tool=AnnotationTool.Select;
            SelectionChanged?.Invoke(this,EventArgs.Empty);TextDraftCreated?.Invoke(this,item);return;
        }
        marks.Add(item);Selected=item;Commit(before);before=null;
    }
    public void Cancel()
    {
        gesture=false;moving=false;preview=null;before=null;original=null;
    }
    public void ClearSelection(){Cancel();Selected=null;SelectionChanged?.Invoke(this,EventArgs.Empty);}
    private void ReplaceSelected(AreaHighlight next) { int index=marks.IndexOf(Selected);if(index>=0)marks[index]=next;Selected=next; }
    public void DeleteSelected()
    {
        Cancel();if(Selected==null)return;var prior=Copy(marks);marks.Remove(Selected);Selected=null;Commit(prior);
    }
    public void EditSelected()
    {
        Cancel();if(Selected==null)return;var proposed=Copy(Selected);var args=new AnnotationEditEventArgs{Annotation=proposed};EditRequested?.Invoke(this,args);
        if(args.Cancel)return;var prior=Copy(marks);ReplaceSelected(proposed);Commit(prior);
    }
    public void ApplyProperties(string text,string color,double opacity,float width,float font)
    {
        if(Selected==null)return;var prior=Copy(marks);Selected.text=text;Selected.color=color;Selected.opacity=Math.Max(0,Math.Min(1,opacity));Selected.strokeWidth=Math.Max(.5f,Math.Min(20,width));Selected.fontSize=Math.Max(6,Math.Min(72,font));Commit(prior);
    }
    public void ApplyInline(AreaHighlight value)
    {
        var existing=Find(value.id);
        if(existing==null && value.type=="text_note" && String.IsNullOrWhiteSpace(value.text))return;
        if(existing!=null && Json.Serialize(existing)==Json.Serialize(value))return;
        var prior=Copy(marks);
        if(value.type=="text_note" && String.IsNullOrWhiteSpace(value.text))
        {if(existing!=null)marks.Remove(existing);Selected=null;}
        else
        {
            var next=Copy(value);if(existing==null)marks.Add(next);else marks[marks.IndexOf(existing)]=next;Selected=next;
        }
        Commit(prior);
    }
    public void Delete(string id){var value=Find(id);if(value==null)return;Selected=value;DeleteSelected();}
    public void ResizeSelected(RectangleF bounds)
    {
        if(Selected==null)return;var prior=Copy(marks);Transform(Selected,bounds);Commit(prior);
    }
    private void Commit(List<AreaHighlight> prior)
    {
        if(prior==null)return;
        if(Selected!=null)Selected.modifiedUtc=DateTime.UtcNow.ToString("o");
        undo.Add(prior);if(undo.Count>30)undo.RemoveAt(0);redo.Clear();SelectionChanged?.Invoke(this,EventArgs.Empty);Changed?.Invoke(this,EventArgs.Empty);
    }
    public void Undo(){Cancel();if(!CanUndo)return;redo.Add(Copy(marks));Restore(undo[undo.Count-1]);undo.RemoveAt(undo.Count-1);}
    public void Redo(){Cancel();if(!CanRedo)return;undo.Add(Copy(marks));Restore(redo[redo.Count-1]);redo.RemoveAt(redo.Count-1);}
    private void Restore(List<AreaHighlight> snapshot){marks.Clear();marks.AddRange(Copy(snapshot));Selected=null;SelectionChanged?.Invoke(this,EventArgs.Empty);Changed?.Invoke(this,EventArgs.Empty);}
    private static RectangleF PointsBox(List<AnnotationPoint> points)
    {
        return RectangleF.FromLTRB((float)points.Min(p=>p.x),(float)points.Min(p=>p.y),(float)points.Max(p=>p.x),(float)points.Max(p=>p.y));
    }
    private static void SetBounds(AreaHighlight m,RectangleF r){m.x=r.X;m.y=r.Y;m.width=Math.Max(.0001,r.Width);m.height=Math.Max(.0001,r.Height);}
    private static void Transform(AreaHighlight m,RectangleF target)
    {
        RectangleF old=Bounds(m);target.Intersect(new RectangleF(0,0,1,1));
        Func<double,double,double> x=(value,unused)=>target.X+(value-old.X)/Math.Max(.0001,old.Width)*target.Width;
        Func<double,double,double> y=(value,unused)=>target.Y+(value-old.Y)/Math.Max(.0001,old.Height)*target.Height;
        foreach(var p in m.points ?? new List<AnnotationPoint>()){p.x=x(p.x,0);p.y=y(p.y,0);}
        foreach(var r in m.textRegions ?? new List<AnnotationRegion>())
        {r.x=x(r.x,0);r.y=y(r.y,0);r.width=r.width/Math.Max(.0001,old.Width)*target.Width;r.height=r.height/Math.Max(.0001,old.Height)*target.Height;foreach(var p in r.quad){p.x=x(p.x,0);p.y=y(p.y,0);}}
        SetBounds(m,target);
    }
    public void Paint(Graphics graphics,Rectangle pageBounds,int index,float zoom)
    {
        GraphicsState state=graphics.Save();graphics.SetClip(pageBounds,CombineMode.Intersect);graphics.SmoothingMode=SmoothingMode.AntiAlias;
        foreach(var m in marks)if(m.pageIndex==index && !(moving && preview!=null && m==Selected) && (inlinePreview==null || inlinePreview.id!=m.id))DrawMark(graphics,pageBounds,m,zoom);
        if(preview!=null && preview.pageIndex==index)DrawMark(graphics,pageBounds,preview,zoom);
        if(inlinePreview!=null && inlinePreview.pageIndex==index)DrawMark(graphics,pageBounds,inlinePreview,zoom);
        if(Selected!=null && Selected.pageIndex==index)
        {
            RectangleF b=ScreenBounds(pageBounds,Bounds(moving && preview!=null?preview:Selected));using(var pen=new Pen(Color.RoyalBlue,1){DashStyle=DashStyle.Dash})graphics.DrawRectangle(pen,b.X,b.Y,b.Width,b.Height);
            foreach(var p in Corners(b)){RectangleF h=new RectangleF(p.X-4,p.Y-4,8,8);graphics.FillRectangle(Brushes.White,h);graphics.DrawRectangle(Pens.RoyalBlue,h.X,h.Y,h.Width,h.Height);}
        }
        graphics.Restore(state);
    }
    private static RectangleF ScreenBounds(Rectangle page,RectangleF r){return new RectangleF(page.X+r.X*page.Width,page.Y+r.Y*page.Height,r.Width*page.Width,r.Height*page.Height);}
    private static void DrawMark(Graphics g,Rectangle page,AreaHighlight m,float zoom)
    {
        if(Double.IsNaN(m.x) || Double.IsNaN(m.y) || Double.IsNaN(m.width) || Double.IsNaN(m.height))return;
        Color baseColor;try{baseColor=ColorTranslator.FromHtml(m.color??"#FFE45C");}catch{baseColor=Color.Gold;}
        Color color=Color.FromArgb((int)(255*Math.Max(0,Math.Min(1,m.opacity))),baseColor);
        RectangleF b=ScreenBounds(page,Bounds(m));
        Func<AnnotationPoint,PointF> point=p=>new PointF(page.X+(float)p.x*page.Width,page.Y+(float)p.y*page.Height);
        using(var pen=new Pen(color,Math.Max(.5f,m.strokeWidth*zoom*4/3)){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
        using(var fill=new SolidBrush(color))
        {
            if(m.type=="area_highlight" || m.type=="underline")
            {
                if(m.textRegions!=null && m.textRegions.Count>0)foreach(var r in m.textRegions)
                {
                    var polygon=r.quad!=null && r.quad.Count==4?r.quad.Select(point).ToArray():Corners(ScreenBounds(page,new RectangleF((float)r.x,(float)r.y,(float)r.width,(float)r.height)));
                    if(m.type=="underline")g.DrawLine(pen,polygon[3],polygon[2]);else g.FillPolygon(fill,polygon);
                }
                else if(m.type=="underline")g.DrawLine(pen,b.Left,b.Bottom,b.Right,b.Bottom);else g.FillRectangle(fill,b);
            }
            else if(m.type=="ink" || m.type=="line")
            {
                if(m.points!=null && m.points.Count>1)g.DrawLines(pen,m.points.Select(point).ToArray());
                else g.FillEllipse(fill,b.X,b.Y,Math.Max(2,pen.Width),Math.Max(2,pen.Width));
            }
            else if(m.type=="text_note")
            {
                using(var bg=new SolidBrush(Color.FromArgb((int)(35*Math.Max(0,Math.Min(1,m.opacity))),baseColor)))g.FillRectangle(bg,b);g.DrawRectangle(pen,b.X,b.Y,b.Width,b.Height);
                using(var font=new Font(m.fontFamily??"SimSun",Math.Max(6,Math.Min(72,m.fontSize))*zoom))g.DrawString(m.text??"",font,fill,b);
            }
            else if(m.type=="ellipse")g.DrawEllipse(pen,b);
            else g.DrawRectangle(pen,b.X,b.Y,b.Width,b.Height);
        }
    }
}
