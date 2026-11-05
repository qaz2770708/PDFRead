using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal static class AnnotationIcons
{
    public static Bitmap Action(string name)
    {
        var image=new Bitmap(32,32);
        using(var g=Graphics.FromImage(image))using(var pen=new Pen(Color.FromArgb(39,54,73),2){StartCap=LineCap.Round,EndCap=LineCap.Round})
        {
            g.SmoothingMode=SmoothingMode.AntiAlias;
            if(name=="properties")
            {
                int[] positions={10,22,15};for(int i=0;i<3;i++){int y=7+i*9;g.DrawLine(pen,4,y,28,y);g.FillEllipse(Brushes.White,positions[i]-3,y-3,6,6);g.DrawEllipse(pen,positions[i]-3,y-3,6,6);}
            }
            else if(name=="delete")
            {g.DrawLine(pen,6,8,26,8);g.DrawLine(pen,12,4,20,4);g.DrawLine(pen,9,11,10,28);g.DrawLine(pen,23,11,22,28);g.DrawLine(pen,10,28,22,28);g.DrawLine(pen,14,13,14,24);g.DrawLine(pen,18,13,18,24);}
            else
            {
                if(name=="redo"){g.TranslateTransform(32,0);g.ScaleTransform(-1,1);}
                g.DrawBezier(pen,7,12,26,2,32,27,14,26);g.DrawLine(pen,7,12,8,4);g.DrawLine(pen,7,12,16,14);
            }
        }
        return image;
    }
    public static Bitmap Create(AnnotationTool tool)
    {
        var image=new Bitmap(32,32);
        using(var g=Graphics.FromImage(image))
        using(var pen=new Pen(Color.FromArgb(39,54,73),2){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
        {
            g.SmoothingMode=SmoothingMode.AntiAlias;
            switch(tool)
            {
                case AnnotationTool.Select:
                    var arrow=new[]{new Point(7,3),new Point(7,25),new Point(13,20),new Point(18,29),new Point(22,27),new Point(17,18),new Point(25,17)};
                    g.FillPolygon(Brushes.White,arrow);g.DrawPolygon(pen,arrow);break;
                case AnnotationTool.Highlight:
                    using(var fill=new SolidBrush(Color.FromArgb(255,218,69)))g.FillRectangle(fill,4,25,24,5);
                    g.DrawPolygon(pen,new[]{new Point(10,21),new Point(6,17),new Point(20,3),new Point(27,10),new Point(13,24)});
                    g.DrawLine(pen,9,14,16,21);g.DrawLine(pen,6,22,10,26);break;
                case AnnotationTool.Underline:
                    g.DrawLine(pen,8,5,8,16);g.DrawBezier(pen,8,16,8,27,24,27,24,16);g.DrawLine(pen,24,16,24,5);g.DrawLine(pen,5,29,27,29);break;
                case AnnotationTool.Text:
                    using(var font=new Font("SimSun",23,FontStyle.Regular,GraphicsUnit.Pixel))
                        TextRenderer.DrawText(g,"A",font,new Rectangle(1,1,30,30),pen.Color,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
                    break;
                case AnnotationTool.Ink:
                    g.DrawCurve(pen,new[]{new Point(4,24),new Point(10,8),new Point(16,9),new Point(11,25),new Point(22,13),new Point(19,25),new Point(28,20)});break;
                case AnnotationTool.Rectangle:g.DrawRectangle(pen,5,6,22,20);break;
                case AnnotationTool.Ellipse:g.DrawEllipse(pen,4,7,24,18);break;
                case AnnotationTool.Line:g.DrawLine(pen,5,26,27,6);g.FillEllipse(Brushes.SlateGray,3,24,4,4);g.FillEllipse(Brushes.SlateGray,25,4,4,4);break;
            }
        }
        return image;
    }
}
