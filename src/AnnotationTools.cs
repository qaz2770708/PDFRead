using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

internal sealed partial class PdfReaderForm
{
    private bool syncingAnnotationTools;
    private readonly ToolStripDropDownButton annotationMenu=new ToolStripDropDownButton("选择注释");
    private readonly ToolStripButton annotationProperties=new ToolStripButton("属性 F2");
    private readonly ToolStripButton annotationDelete=new ToolStripButton("删除 Del");
    private readonly ToolStripButton annotationUndo=new ToolStripButton("撤销");
    private readonly ToolStripButton annotationRedo=new ToolStripButton("重做");
    private readonly Dictionary<AnnotationTool,ToolStripMenuItem> annotationItems=new Dictionary<AnnotationTool,ToolStripMenuItem>();
    internal Func<AreaHighlight,bool> AnnotationEditorOverride { get; set; }
    private static string ToolName(AnnotationTool tool)
    {
        switch(tool){case AnnotationTool.Highlight:return "高亮";case AnnotationTool.Underline:return "下划线";case AnnotationTool.Text:return "文本批注";case AnnotationTool.Ink:return "手写";case AnnotationTool.Rectangle:return "矩形";case AnnotationTool.Ellipse:return "椭圆";case AnnotationTool.Line:return "直线";default:return "选择 / 移动";}
    }
    private void InitializeAnnotationTools()
    {
        annotationStrip.Items.Clear();annotationMenu.AutoSize=false;annotationMenu.Size=new Size(146,28);annotationStrip.Items.Add(annotationMenu);
        var tools=new[]{AnnotationTool.Select,AnnotationTool.Highlight,AnnotationTool.Underline,AnnotationTool.Text,AnnotationTool.Ink,AnnotationTool.Rectangle,AnnotationTool.Ellipse,AnnotationTool.Line};
        var keys=new[]{"A","H","U","T","P","R","E","L"};
        for(int i=0;i<tools.Length;i++)
        {
            AnnotationTool tool=tools[i];var item=new ToolStripMenuItem(ToolName(tool)){ShortcutKeyDisplayString="Ctrl+Shift+"+keys[i]};
            item.Click+=delegate{SetActiveAnnotationTool(tool);};annotationMenu.DropDownItems.Add(item);annotationItems[tool]=item;
        }
        annotationMenu.ToolTipText="选择工具后在页面上拖拽；高亮/下划线优先贴合文本，没有文本时按区域标注";
        annotationEditStrip.Items.Clear();
        foreach(var button in new[]{annotationProperties,annotationDelete}){button.AutoSize=false;button.Size=new Size(73,28);annotationEditStrip.Items.Add(button);}
        annotationProperties.Click+=delegate{EditActiveAnnotation();};annotationDelete.Click+=delegate{ActiveSession?.Viewer.AnnotationLayer.DeleteSelected();};
        foreach(var button in new[]{annotationUndo,annotationRedo}){button.AutoSize=false;button.Size=new Size(47,28);historyStrip.Items.Add(button);}
        annotationUndo.ToolTipText="撤销注释操作 Ctrl+Z";annotationRedo.ToolTipText="重做注释操作 Ctrl+Y";
        annotationUndo.Click+=delegate{ActiveSession?.Viewer.AnnotationLayer.Undo();};annotationRedo.Click+=delegate{ActiveSession?.Viewer.AnnotationLayer.Redo();};
    }
    private void SetActiveAnnotationTool(AnnotationTool tool){var s=ActiveSession;if(s==null)return;s.Viewer.SetAnnotationTool(tool);s.Viewer.Focus();SyncAnnotationControls();}
    private void SyncAnnotationControls()
    {
        var layer=ActiveSession?.Viewer.AnnotationLayer;syncingAnnotationTools=true;
        try
        {
            annotationMenu.Enabled=layer!=null;
            annotationMenu.Text=layer==null?"注释工具":ToolName(layer.Tool);
            foreach(var pair in annotationItems)pair.Value.Checked=layer!=null && pair.Key==layer.Tool;
            highlightButton.Checked=layer!=null && layer.Tool==AnnotationTool.Highlight;
            annotationProperties.Enabled=annotationDelete.Enabled=layer?.Selected!=null;
            annotationUndo.Enabled=layer!=null && layer.CanUndo;annotationRedo.Enabled=layer!=null && layer.CanRedo;
        }
        finally{syncingAnnotationTools=false;}
    }
    private void EditActiveAnnotation()
    {
        var s=ActiveSession;if(s==null)return;s.Viewer.AnnotationLayer.EditSelected();
        var selected=s.Viewer.AnnotationLayer.Selected;if(selected!=null && selected.pageIndex!=s.Viewer.CurrentPage)s.Viewer.GoToLocation(selected.pageIndex,selected.y,selected.x);
    }
    private bool HandleAnnotationShortcut(DocumentSession s,Keys key)
    {
        AnnotationTool? tool=null;
        switch(key)
        {
            case Keys.Control|Keys.Shift|Keys.A:tool=AnnotationTool.Select;break;
            case Keys.Control|Keys.Shift|Keys.H:tool=s.Viewer.AnnotationLayer.Tool==AnnotationTool.Highlight?AnnotationTool.Select:AnnotationTool.Highlight;break;
            case Keys.Control|Keys.Shift|Keys.U:tool=AnnotationTool.Underline;break;
            case Keys.Control|Keys.Shift|Keys.T:tool=AnnotationTool.Text;break;
            case Keys.Control|Keys.Shift|Keys.P:tool=AnnotationTool.Ink;break;
            case Keys.Control|Keys.Shift|Keys.R:tool=AnnotationTool.Rectangle;break;
            case Keys.Control|Keys.Shift|Keys.E:tool=AnnotationTool.Ellipse;break;
            case Keys.Control|Keys.Shift|Keys.L:tool=AnnotationTool.Line;break;
        }
        if(tool.HasValue){SetActiveAnnotationTool(tool.Value);return true;}
        if(key==Keys.Delete && s.Viewer.ContainsFocus && s.Viewer.AnnotationLayer.Selected!=null){s.Viewer.AnnotationLayer.DeleteSelected();return true;}
        if(key==Keys.F2 && s.Viewer.ContainsFocus && s.Viewer.AnnotationLayer.Selected!=null){EditActiveAnnotation();return true;}
        if(!PageInputFocused && !bookmarkTree.ContainsFocus && (key==(Keys.Control|Keys.Z) || key==(Keys.Control|Keys.Y)))
        {if(key==(Keys.Control|Keys.Z))s.Viewer.AnnotationLayer.Undo();else s.Viewer.AnnotationLayer.Redo();return true;}
        return false;
    }
    private bool EditAnnotationProperties(DocumentSession session,AreaHighlight annotation)
    {
        if(AnnotationEditorOverride!=null)return AnnotationEditorOverride(annotation);
        using(var dialog=new Form{Text="注释属性",StartPosition=FormStartPosition.CenterParent,Size=new Size(450,510),MinimumSize=new Size(420,490),Font=Font,MinimizeBox=false,MaximizeBox=false})
        {
            var table=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=8,Padding=new Padding(12)};
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            Action<string,Control> row=(label,control)=>{table.Controls.Add(new Label{Text=label,AutoSize=true,Anchor=AnchorStyles.Left});control.Dock=DockStyle.Fill;table.Controls.Add(control);};
            string friendly=annotation.type=="area_highlight"?"高亮":annotation.type=="underline"?"下划线":annotation.type=="text_note"?"文本批注":annotation.type=="ink"?"手写":annotation.type=="ellipse"?"椭圆":annotation.type=="line"?"直线":"矩形";
            row("类型",new Label{Text=friendly,AutoSize=true});
            var page=new NumericUpDown{Minimum=1,Maximum=session.PageCount,Value=Math.Max(1,Math.Min(session.PageCount,annotation.pageIndex+1))};row("所在页",page);
            var text=new TextBox{Multiline=true,ScrollBars=ScrollBars.Vertical,MaxLength=10000,Text=annotation.text??"",Height=115};row("批注文本",text);table.RowStyles.Add(new RowStyle(SizeType.Absolute,28));table.RowStyles.Add(new RowStyle(SizeType.Absolute,30));table.RowStyles.Add(new RowStyle(SizeType.Absolute,120));
            Color color;try{color=ColorTranslator.FromHtml(annotation.color??"#2878D7");}catch{color=Color.RoyalBlue;}
            var colorButton=new Button{Text="选择颜色",BackColor=color};colorButton.Click+=delegate{using(var picker=new ColorDialog{Color=color})if(picker.ShowDialog(dialog)==DialogResult.OK){color=picker.Color;colorButton.BackColor=color;}};row("颜色",colorButton);
            var opacity=new NumericUpDown{Minimum=10,Maximum=100,Value=(decimal)Math.Max(10,Math.Min(100,annotation.opacity*100))};row("不透明度 %",opacity);
            var width=new NumericUpDown{Minimum=.5m,Maximum=20,DecimalPlaces=1,Increment=.5m,Value=(decimal)Math.Max(.5,Math.Min(20,annotation.strokeWidth))};row("线宽 pt",width);
            var font=new NumericUpDown{Minimum=6,Maximum=72,Value=(decimal)Math.Max(6,Math.Min(72,annotation.fontSize))};row("字号 pt",font);
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,AutoSize=true};
            var cancel=new Button{Text="取消",DialogResult=DialogResult.Cancel};var ok=new Button{Text="确定",DialogResult=DialogResult.OK};buttons.Controls.Add(cancel);buttons.Controls.Add(ok);table.Controls.Add(buttons);table.SetColumnSpan(buttons,2);dialog.AcceptButton=ok;dialog.CancelButton=cancel;dialog.Controls.Add(table);
            ok.Click+=delegate{if(annotation.type=="text_note" && String.IsNullOrWhiteSpace(text.Text)){dialog.DialogResult=DialogResult.None;text.Focus();}};
            if(dialog.ShowDialog(this)!=DialogResult.OK)return false;
            annotation.text=text.Text;annotation.color=ColorTranslator.ToHtml(color);annotation.opacity=(double)opacity.Value/100;annotation.strokeWidth=(float)width.Value;annotation.fontSize=(float)font.Value;annotation.pageIndex=(int)page.Value-1;return true;
        }
    }
}
