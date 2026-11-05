using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed partial class ContinuousPdfViewer
{
    internal readonly AnnotationLayer AnnotationLayer=new AnnotationLayer();
    private bool annotationDragging;
    private int annotationPage;
    public event EventHandler AnnotationsChanged;
    public event EventHandler AnnotationSelectionChanged;
    public event EventHandler<AnnotationEditEventArgs> AnnotationEditRequested;
    private void InitializeAnnotationLayer()
    {
        AnnotationLayer.Changed+=delegate{Invalidate();AnnotationsChanged?.Invoke(this,EventArgs.Empty);};
        AnnotationLayer.SelectionChanged+=delegate{Invalidate();AnnotationSelectionChanged?.Invoke(this,EventArgs.Empty);};
        AnnotationLayer.EditRequested+=delegate(object sender,AnnotationEditEventArgs args){AnnotationEditRequested?.Invoke(this,args);};
        var menu=new ContextMenuStrip();
        var edit=new ToolStripMenuItem("编辑属性 (F2)",null,delegate{AnnotationLayer.EditSelected();});
        var remove=new ToolStripMenuItem("删除 (Delete)",null,delegate{AnnotationLayer.DeleteSelected();});
        var undoItem=new ToolStripMenuItem("撤销 (Ctrl+Z)",null,delegate{AnnotationLayer.Undo();});
        var redoItem=new ToolStripMenuItem("重做 (Ctrl+Y)",null,delegate{AnnotationLayer.Redo();});
        menu.Items.AddRange(new ToolStripItem[]{edit,remove,undoItem,redoItem});
        menu.Opening+=delegate{edit.Enabled=remove.Enabled=AnnotationLayer.Selected!=null;undoItem.Enabled=AnnotationLayer.CanUndo;redoItem.Enabled=AnnotationLayer.CanRedo;};
        ContextMenuStrip=menu;
    }
    public void SetAnnotationTool(AnnotationTool tool)
    {
        annotationDragging=false;Capture=false;AnnotationLayer.SetTool(tool);Cursor=tool==AnnotationTool.Select?Cursors.Default:Cursors.Cross;Invalidate();
    }
    public void CancelAnnotationOperation(){annotationDragging=false;Capture=false;AnnotationLayer.ClearSelection();SetAnnotationTool(AnnotationTool.Select);}
    private PointF NormalizedPoint(int page,Point client)
    {
        Point content=ToContentPoint(client);Rectangle bounds=pageBounds[page];return new PointF((float)(content.X-bounds.Left)/bounds.Width,(float)(content.Y-bounds.Top)/bounds.Height);
    }
    private bool BeginAnnotation(MouseEventArgs e)
    {
        int page=FindPageAt(ToContentPoint(e.Location));
        if(page<0){AnnotationLayer.ClearSelection();return false;}
        Rectangle bounds=pageBounds[page];
        if(e.Button==MouseButtons.Right){AnnotationLayer.SetTool(AnnotationTool.Select);Cursor=Cursors.Default;}
        bool hit=AnnotationLayer.Begin(page,NormalizedPoint(page,e.Location),8f/bounds.Width,8f/bounds.Height);
        if(e.Button==MouseButtons.Right){AnnotationLayer.Cancel();Invalidate();return hit;}
        if(!hit)return false;
        annotationPage=page;annotationDragging=true;Capture=true;Invalidate();return true;
    }
    private void MoveAnnotation(MouseEventArgs e)
    {
        if(annotationDragging){AnnotationLayer.Move(NormalizedPoint(annotationPage,e.Location));Invalidate();return;}
        if(AnnotationLayer.Tool!=AnnotationTool.Select){Cursor=Cursors.Cross;return;}
        int page=FindPageAt(ToContentPoint(e.Location));
        if(page>=0 && AnnotationLayer.HitTest(page,NormalizedPoint(page,e.Location),8f/pageBounds[page].Width,8f/pageBounds[page].Height)!=null)Cursor=Cursors.SizeAll;
        else Cursor=LinkAt(e.Location)!=null?Cursors.Hand:Cursors.Default;
    }
    private void EndAnnotation(MouseEventArgs e)
    {
        if(!annotationDragging || e.Button!=MouseButtons.Left)return;
        annotationDragging=false;Capture=false;AnnotationLayer.End(NormalizedPoint(annotationPage,e.Location));Invalidate();
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x0202 && annotationDragging)
        {
            long p=m.LParam.ToInt64();EndAnnotation(new MouseEventArgs(MouseButtons.Left,1,(short)(p&0xffff),(short)((p>>16)&0xffff),0));
        }
        base.WndProc(ref m);
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);if(!Capture && annotationDragging){annotationDragging=false;AnnotationLayer.Cancel();Invalidate();}
    }
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);if(e.Button==MouseButtons.Left && AnnotationLayer.Tool==AnnotationTool.Select && AnnotationLayer.Selected!=null)AnnotationLayer.EditSelected();
    }
}
