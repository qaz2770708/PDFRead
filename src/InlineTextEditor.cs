using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class ColorChoiceButton : Button
{
    public Color Swatch {get;set;}
    public bool Chosen;
    public ColorChoiceButton(){Size=new Size(25,25);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;}
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(BackColor);
        using(var fill=new SolidBrush(Swatch))e.Graphics.FillEllipse(fill,5,5,15,15);
        using(var pen=new Pen(Chosen?Color.RoyalBlue:Color.LightGray,Chosen?2:1))e.Graphics.DrawEllipse(pen,2,2,21,21);
        if(Focused)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(0,0,24,24));
    }
}
internal sealed class NoteResizeGrip : Control
{
    public NoteResizeGrip(){Size=new Size(13,13);Cursor=Cursors.SizeNWSE;BackColor=Color.White;}
    protected override void OnPaint(PaintEventArgs e){for(int i=3;i<=11;i+=4)e.Graphics.DrawLine(Pens.SlateGray,i,12,12,i);}
}
internal sealed class NoteInputBox : TextBox
{
    internal InlineTextEditor Editor;
    public NoteInputBox(){Multiline=true;WordWrap=true;AcceptsReturn=true;ScrollBars=ScrollBars.Vertical;MaxLength=10000;BorderStyle=BorderStyle.None;}
    protected override bool ProcessCmdKey(ref Message msg,Keys keys){return Editor.HandleKey(keys)||base.ProcessCmdKey(ref msg,keys);}
}

// The native input box lives over the page. Only committed copies enter the
// annotation list, so progress autosave cannot accidentally save a drag preview.
internal sealed class InlineTextEditor : IDisposable
{
    private readonly ContinuousPdfViewer viewer;
    private readonly Panel frame=new Panel{BorderStyle=BorderStyle.FixedSingle,BackColor=Color.Transparent,Visible=false};
    private readonly Label caption=new Label{Text="拖动 · 文本批注",BackColor=Color.White,ForeColor=Color.SlateGray,Cursor=Cursors.SizeAll,Height=19,AutoSize=false,Visible=false};
    internal readonly Panel StylePanel=new Panel{Size=new Size(338,152),BackColor=Color.White,BorderStyle=BorderStyle.FixedSingle,Visible=false};
    internal readonly NoteInputBox Input=new NoteInputBox();
    internal readonly TextBox Subtitle=new TextBox{MaxLength=1000};
    private readonly Label subtitleLabel=new Label{Text="副文本",AutoSize=false};
    private readonly List<Button> actionButtons=new List<Button>();
    internal readonly TrackBar SizeSlider=new TrackBar{AutoSize=false,TickStyle=TickStyle.None,SmallChange=1,LargeChange=5};
    internal readonly TrackBar TransparencySlider=new TrackBar{Minimum=0,Maximum=100,AutoSize=false,TickStyle=TickStyle.None,SmallChange=1,LargeChange=10};
    internal readonly List<ColorChoiceButton> ColorButtons=new List<ColorChoiceButton>();
    private readonly Label colorCode=new Label(),sizeValue=new Label(),transparencyValue=new Label(),sizeLabel=new Label();
    private readonly NoteResizeGrip grip=new NoteResizeGrip();
    private readonly Timer saveTimer=new Timer{Interval=400};
    private readonly ToolTip tips=new ToolTip();
    private static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=Int32.MaxValue};
    private bool suppress,dirty,flushing,positioning,disposed,dragging,resizing;
    private Point dragStart;
    private AreaHighlight dragOriginal;
    private Font inputFont;
    private AreaHighlight draft;
    public bool IsOpen {get{return draft!=null;}}
    public bool TextFocused {get{return IsOpen && (Input.ContainsFocus || Subtitle.ContainsFocus);}}
    public bool HasPendingChanges {get{return dirty;}}
    internal AreaHighlight Draft {get{return draft;}}
    private bool IsText {get{return draft!=null && (draft.type=="text_note" || draft.type=="sticky_note");}}
    private static T Copy<T>(T value){return Json.Deserialize<T>(Json.Serialize(value));}
    private static Color Parse(string value){try{return ColorTranslator.FromHtml(value??"#2878D7");}catch{return Color.RoyalBlue;}}
    private static string Hex(Color value){return String.Format("#{0:X2}{1:X2}{2:X2}",value.R,value.G,value.B);}
    public InlineTextEditor(ContinuousPdfViewer owner)
    {
        viewer=owner;Input.Editor=this;Input.Dock=DockStyle.Fill;frame.Controls.Add(Input);frame.Controls.Add(grip);
        frame.SizeChanged+=delegate{grip.Location=new Point(Math.Max(0,frame.Width-15),Math.Max(0,frame.Height-15));grip.BringToFront();};
        viewer.Controls.Add(frame);viewer.Controls.Add(caption);viewer.Controls.Add(StylePanel);
        Input.TextChanged+=delegate{if(!suppress && IsText){draft.text=Input.Text;Changed();}};
        Input.GotFocus+=delegate{Input.Show();};
        Input.LostFocus+=delegate{if(IsOpen){Input.Hide();viewer.Invalidate();}};
        frame.MouseDown+=delegate(object sender,MouseEventArgs e){if(IsText && e.Button==MouseButtons.Left)FocusInput();};
        AddLabel("颜色",8,10,37);colorCode.SetBounds(224,12,104,22);StylePanel.Controls.Add(colorCode);
        string[] colors={"#1F2937","#D94A44","#C47C0B","#2E925E","#2878D7","#8051B5"};
        string[] names={"黑色","红色","橙色","绿色","蓝色","紫色"};
        for(int i=0;i<colors.Length;i++)
        {
            var swatch=new ColorChoiceButton{Swatch=Parse(colors[i]),AccessibleName=names[i]+" "+colors[i],Location=new Point(46+i*28,7)};
            string color=colors[i];swatch.Click+=delegate{SetColor(color);if(IsText)FocusInput();};
            ColorButtons.Add(swatch);StylePanel.Controls.Add(swatch);tips.SetToolTip(swatch,names[i]+" "+color);
        }
        sizeLabel.SetBounds(8,45,40,22);StylePanel.Controls.Add(sizeLabel);
        AddSliderRow(SizeSlider,sizeValue,42,delegate(int delta){SizeSlider.Value=Math.Max(SizeSlider.Minimum,Math.Min(SizeSlider.Maximum,SizeSlider.Value+delta));});
        AddLabel("透明度",8,79,49);AddSliderRow(TransparencySlider,transparencyValue,76,delegate(int delta){TransparencySlider.Value=Math.Max(0,Math.Min(100,TransparencySlider.Value+delta));});
        SizeSlider.ValueChanged+=delegate
        {
            if(suppress || !IsOpen)return;
            if(IsText)draft.fontSize=SizeSlider.Value;else draft.strokeWidth=SizeSlider.Value/10f;
            RefreshValues();UpdateInputAppearance();Changed();
        };
        TransparencySlider.ValueChanged+=delegate{if(suppress || !IsOpen)return;draft.opacity=1-TransparencySlider.Value/100.0;RefreshValues();Changed();};
        var done=new Button{Text="完成",Location=new Point(8,113),Size=new Size(69,28)};
        var remove=new Button{Text="删除",Location=new Point(85,113),Size=new Size(69,28)};
        var advanced=new Button{Text="专业属性 F2",Location=new Point(206,113),Size=new Size(119,28)};
        done.Click+=delegate{Finish();viewer.Focus();};remove.Click+=delegate{Delete();};advanced.Click+=delegate{Professional();};
        StylePanel.Controls.AddRange(new Control[]{done,remove,advanced});actionButtons.AddRange(new[]{done,remove,advanced});
        subtitleLabel.SetBounds(8,115,49,26);Subtitle.SetBounds(61,113,263,26);StylePanel.Controls.AddRange(new Control[]{subtitleLabel,Subtitle});
        Subtitle.TextChanged+=delegate{if(!suppress && IsOpen && draft.type=="sticky_note"){draft.subtitle=Subtitle.Text;Changed();}};
        saveTimer.Tick+=delegate{if(!dragging)Flush();};
        WireDrag(caption,false);WireDrag(grip,true);
    }
    private void AddLabel(string text,int x,int y,int width){StylePanel.Controls.Add(new Label{Text=text,Location=new Point(x,y),Size=new Size(width,22),TextAlign=ContentAlignment.MiddleLeft});}
    private void AddSliderRow(TrackBar slider,Label value,int y,Action<int> step)
    {
        var minus=new Button{Text="−",Location=new Point(58,y),Size=new Size(25,26),AccessibleName="减小"};
        slider.SetBounds(85,y,162,27);
        var plus=new Button{Text="+",Location=new Point(248,y),Size=new Size(25,26),AccessibleName="增大"};
        value.SetBounds(280,y+2,48,23);value.TextAlign=ContentAlignment.MiddleLeft;
        minus.Click+=delegate{step(-1);};plus.Click+=delegate{step(1);};StylePanel.Controls.AddRange(new Control[]{minus,slider,plus,value});
    }
    public void Begin(AreaHighlight value,bool focusText)
    {
        Finish();draft=Copy(value);dirty=false;suppress=true;
        try
        {
            Input.Text=draft.text??"";Input.Visible=IsText;
            bool sticky=draft.type=="sticky_note";caption.Text=sticky?"拖动 · 便签":"拖动 · 文本批注";
            Subtitle.Text=draft.subtitle??"";Subtitle.Visible=subtitleLabel.Visible=sticky;
            foreach(var button in actionButtons)button.Top=sticky?147:113;
            StylePanel.Height=sticky?186:152;
            SizeSlider.Minimum=IsText?6:5;SizeSlider.Maximum=IsText?72:200;
            SizeSlider.Value=Math.Max(SizeSlider.Minimum,Math.Min(SizeSlider.Maximum,(int)Math.Round(IsText?draft.fontSize:draft.strokeWidth*10)));
            TransparencySlider.Value=Math.Max(0,Math.Min(100,(int)Math.Round((1-draft.opacity)*100)));
            sizeLabel.Text=IsText?"字号":"线宽";RefreshValues();UpdateInputAppearance();
        }
        finally{suppress=false;}
        viewer.AnnotationLayer.SetInlinePreview(draft);Reposition();viewer.Invalidate();
        if(IsText && focusText){FocusInput();Input.SelectionStart=Input.TextLength;Input.SelectionLength=0;}
    }
    private void FocusInput(){if(!IsText)return;Input.Show();Input.Focus();}
    private void RefreshValues()
    {
        if(!IsOpen)return;
        colorCode.Text=Hex(Parse(draft.color));sizeValue.Text=(IsText?draft.fontSize:draft.strokeWidth).ToString("0.#")+" pt";
        transparencyValue.Text=TransparencySlider.Value+"%";
        foreach(var swatch in ColorButtons){swatch.Chosen=swatch.Swatch.ToArgb()==Parse(draft.color).ToArgb();swatch.Invalidate();}
    }
    public void SetColor(string color){if(!IsOpen)return;draft.color=color;RefreshValues();UpdateInputAppearance();Changed();}
    private void UpdateInputAppearance()
    {
        if(!IsText)return;Input.ForeColor=Parse(draft.color);
        float points=Math.Max(3,draft.fontSize*viewer.Zoom);string family=draft.fontFamily??"SimSun";
        if(inputFont==null || Math.Abs(inputFont.SizeInPoints-points)>.01f || inputFont.FontFamily.Name!=family)
        {var next=new Font(family,points);Input.Font=next;inputFont?.Dispose();inputFont=next;}
    }
    private void Changed(){dirty=true;viewer.AnnotationLayer.SetInlinePreview(draft);viewer.Invalidate();saveTimer.Stop();saveTimer.Start();viewer.NotifyDraftState();}
    public void Flush()
    {
        saveTimer.Stop();if(!IsOpen || !dirty || dragging || flushing)return;flushing=true;
        try
        {
            viewer.AnnotationLayer.ApplyInline(draft);
            var saved=viewer.AnnotationLayer.Find(draft.id);if(saved!=null)draft=Copy(saved);
            dirty=false;viewer.AnnotationLayer.SetInlinePreview(draft);viewer.NotifyDraftState();
        }
        finally{flushing=false;}
        viewer.Invalidate();
    }
    public void Finish(){if(!IsOpen)return;if(dragging)EndDrag();Flush();CloseOverlay();}
    public void Cancel(){if(!IsOpen)return;dragging=false;caption.Capture=grip.Capture=false;CloseOverlay();viewer.Focus();}
    private void CloseOverlay()
    {
        saveTimer.Stop();draft=null;dirty=false;frame.Hide();caption.Hide();StylePanel.Hide();viewer.AnnotationLayer.SetInlinePreview(null);viewer.Invalidate();viewer.NotifyDraftState();
    }
    public void MarksChanged()
    {
        if(!IsOpen || flushing)return;
        if(viewer.AnnotationLayer.Find(draft.id)==null && !dirty)CloseOverlay();
    }
    public void Delete(){if(!IsOpen)return;string id=draft.id;CloseOverlay();viewer.AnnotationLayer.Delete(id);viewer.Focus();}
    public void Professional()
    {
        if(IsOpen && viewer.AnnotationLayer.Find(draft.id)==null)
        {
            var proposed=Copy(draft);if(!viewer.RequestAnnotationProperties(proposed))return;
            draft=proposed;dirty=true;Finish();viewer.Focus();NavigateSelected();return;
        }
        Finish();viewer.Focus();viewer.AnnotationLayer.EditSelected();
        NavigateSelected();
    }
    private void NavigateSelected(){var selected=viewer.AnnotationLayer.Selected;if(selected!=null && selected.pageIndex!=viewer.CurrentPage)viewer.GoToLocation(selected.pageIndex,selected.y,selected.x);}
    public bool HandleKey(Keys key)
    {
        if(!IsOpen)return false;
        if(key==Keys.Escape){Cancel();return true;}
        if(key==(Keys.Control|Keys.Enter)){Finish();viewer.Focus();return true;}
        if(key==(Keys.Control|Keys.S)){Flush();return false;}
        if(key==Keys.F2){Professional();return true;}
        return false;
    }
    public void Reposition()
    {
        if(!IsOpen || positioning || disposed)return;positioning=true;
        try
        {
            Rectangle rect=viewer.AnnotationClientBounds(draft);
            bool visible=rect.IntersectsWith(viewer.ClientRectangle) && viewer.Visible;
            frame.Visible=caption.Visible=visible && IsText;StylePanel.Visible=visible;
            if(!visible)return;
            frame.Bounds=rect;caption.SetBounds(rect.Left,rect.Top>=20?rect.Top-20:rect.Bottom+2,Math.Min(130,Math.Max(85,rect.Width)),19);
            int width=Math.Min(338,viewer.ClientSize.Width-8),height=draft.type=="sticky_note"?186:152;
            int left=Math.Max(4,Math.Min(viewer.ClientSize.Width-width-4,rect.Left));
            int top=rect.Bottom+5;if(top+height>viewer.ClientSize.Height-4)top=rect.Top-height-5;
            top=Math.Max(4,Math.Min(viewer.ClientSize.Height-height-4,top));StylePanel.SetBounds(left,top,width,height);
            frame.BringToFront();caption.BringToFront();StylePanel.BringToFront();UpdateInputAppearance();
        }
        finally{positioning=false;}
    }
    private void WireDrag(Control control,bool resize)
    {
        control.MouseDown+=delegate(object sender,MouseEventArgs e)
        {
            if(e.Button!=MouseButtons.Left || !IsOpen)return;dragging=true;resizing=resize;dragOriginal=Copy(draft);
            dragStart=viewer.PointToClient(control.PointToScreen(e.Location));control.Capture=true;saveTimer.Stop();
        };
        control.MouseMove+=delegate(object sender,MouseEventArgs e)
        {
            if(!dragging || !IsOpen)return;Point point=viewer.PointToClient(control.PointToScreen(e.Location));var page=viewer.PageRectangles[draft.pageIndex];
            float dx=(float)(point.X-dragStart.X)/page.Width,dy=(float)(point.Y-dragStart.Y)/page.Height;
            RectangleF box=new RectangleF((float)dragOriginal.x,(float)dragOriginal.y,(float)dragOriginal.width,(float)dragOriginal.height);
            if(resizing){box.Width=Math.Min(1-box.X,Math.Max(.04f,box.Width+dx));box.Height=Math.Min(1-box.Y,Math.Max(.025f,box.Height+dy));}
            else{box.X=Math.Max(0,Math.Min(1-box.Width,box.X+dx));box.Y=Math.Max(0,Math.Min(1-box.Height,box.Y+dy));}
            SetBounds(box);
        };
        control.MouseUp+=delegate(object sender,MouseEventArgs e){if(dragging && e.Button==MouseButtons.Left){control.Capture=false;EndDrag();}};
    }
    internal void SetBounds(RectangleF rect)
    {
        if(!IsText)return;draft.x=rect.X;draft.y=rect.Y;draft.width=rect.Width;draft.height=rect.Height;Changed();Reposition();
    }
    private void EndDrag(){dragging=false;caption.Capture=grip.Capture=false;Flush();}
    public void Dispose(){if(disposed)return;disposed=true;saveTimer.Dispose();tips.Dispose();frame.Dispose();caption.Dispose();StylePanel.Dispose();inputFont?.Dispose();}
}
