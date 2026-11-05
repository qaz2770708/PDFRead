using System;using System.IO;using System.Linq;using System.Drawing;using System.Collections.Generic;using System.Reflection;using System.Windows.Forms;using System.Web.Script.Serialization;
internal static class InlineEditorAcceptance
{
 static object Field(object o,string n){return o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
 static object Call(object o,string n,params object[] a){return o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,a);}
 static void Check(bool ok,string m){if(!ok)throw new Exception(m);Console.WriteLine("PASS "+m);}
 static List<DocumentSession> Sessions(PdfReaderForm f){return (List<DocumentSession>)Field(f,"sessions");}
 static AreaHighlight Disk(DocumentSession s,string id){return new JavaScriptSerializer().Deserialize<SidecarFile>(File.ReadAllText(s.SidecarPath)).versions.First(v=>v.sourceHash==s.PdfHash).annotations.FirstOrDefault(m=>m.id==id);}
 static void Key(PdfReaderForm f,Keys k){Check((bool)Call(f,"ProcessCmdKey",new Message(),k),"handled "+k);}
 static void Click(ContinuousPdfViewer v,float x,float y){var b=v.PageRectangles[0];var p=new Point(b.X+v.AutoScrollPosition.X+(int)(x*b.Width),b.Y+v.AutoScrollPosition.Y+(int)(y*b.Height));foreach(string n in new[]{"OnMouseDown","OnMouseUp"})Call(v,n,new MouseEventArgs(MouseButtons.Left,1,p.X,p.Y,0));}
 static void NewNote(PdfReaderForm f,ContinuousPdfViewer v){Key(f,Keys.Control|Keys.Shift|Keys.T);Click(v,.22f,.22f);}
 static void Shot(Control c,string path){using(var b=new Bitmap(c.Width,c.Height)){c.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));b.Save(path);}}
 [STAThread]static int Main(string[] args)
 {
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  string folder=Path.GetFullPath(Path.Combine("work","inline-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(folder);DocumentDataRepository.DataRootOverride=Path.Combine(folder,"data");
  string pdf=Path.Combine(folder,"notes.pdf"),second=Path.Combine(folder,"second.pdf");File.Copy(args[0],pdf);File.Copy(args[0],second);File.AppendAllText(second,"\n% second\n");string hash=DocumentDataRepository.CalculateSha256(pdf);
  string currentRoot=DocumentDataRepository.DataRootOverride;DocumentDataRepository.DataRootOverride=Path.Combine(folder,"upgrade-check");Directory.CreateDirectory(DocumentDataRepository.DataRootOverride);
  var legacy=new SidecarFile{formatVersion=4};legacy.versions.Add(new VersionRecord{sourceHash=hash,pageCount=4,lastPage=1,zoom=1.1f,annotations=new List<AreaHighlight>{new AreaHighlight{id="old-text",type="text_note",text="旧文字",width=.2,height=.1}}});
  string json=new JavaScriptSerializer().Serialize(legacy).Replace(",\"fontFamily\":\"Microsoft YaHei UI\"","");File.WriteAllText(DocumentDataRepository.GetDataPath(hash),json);
  var upgraded=DocumentDataRepository.LoadForPdf(pdf,hash,4,null);Check(upgraded.NeedsSave && upgraded.Data.formatVersion==5 && upgraded.Record.lastPage==1 && upgraded.Record.annotations[0].fontFamily=="Microsoft YaHei UI","v4 upgrade retains text appearance and reading progress");
  DocumentDataRepository.DataRootOverride=currentRoot;
  var context=new ReaderApplicationContext(new[]{pdf});var form=context.Windows[0];var timer=new Timer{Interval=1000};int stage=0;string id=null;RectangleF expected=RectangleF.Empty;
  timer.Tick+=delegate
  {
   timer.Stop();try
   {
    var s=Sessions(form).First();var v=s.Viewer;var edit=v.InlineEditor;
    if(stage==0)
    {
     v.SetZoom(.75f);v.GoToPage(0,0);var strip=(ToolStrip)Field(form,"annotationStrip");
     Check(strip.Items.Count==8 && strip.Items.Cast<ToolStripItem>().All(i=>i is ToolStripButton && i.Image!=null && i.DisplayStyle==ToolStripItemDisplayStyle.Image && i.ToolTipText.Contains("Ctrl+Shift")),"eight direct icon buttons with descriptive tooltips; no tool dropdown");
     Check(strip.Items[3].AccessibleName=="文本批注" && strip.Items[5].Image!=strip.Items[6].Image,"text A and separate rectangle/ellipse buttons");
     foreach(var item in strip.Items.Cast<ToolStripItem>())Check(item.Available && item.Bounds.Bottom<=strip.Height && item.Bounds.Right<=strip.Width,"icon visible "+item.Text);
     var tools=(ReadingToolsWindow)Field(form,"rightTools");Shot(tools,Path.Combine(folder,"tools.png"));
     var original=form.Size;form.Size=form.MinimumSize;Call(form,"PositionReadingTools");
     Check(strip.Items.Cast<ToolStripItem>().All(i=>i.Bounds.Bottom<=strip.Height && i.Bounds.Right<=strip.Width),"icons fit at minimum window size");form.Size=original;Call(form,"PositionReadingTools");
     int dialogs=0;form.AnnotationEditorOverride=m=>{dialogs++;return true;};NewNote(form,v);
     Check(edit.IsOpen && edit.Input.Visible && edit.Input.ContainsFocus && s.Annotations.Count==0 && dialogs==0,"page click creates focused inline input without modal or empty saved note");
     Check(edit.Draft.fontFamily=="SimSun" && edit.Input.Font.FontFamily.Name==new Font("SimSun",12).FontFamily.Name,"new note uses SimSun");
     var caption=(Label)Field(edit,"caption");var grip=(Control)Field(edit,"grip");
     double x=edit.Draft.x,w=edit.Draft.width;
     foreach(var control in new Control[]{caption,grip}){Call(control,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,5,5,0));Call(control,"OnMouseMove",new MouseEventArgs(MouseButtons.Left,1,17,14,0));Call(control,"OnMouseUp",new MouseEventArgs(MouseButtons.Left,1,17,14,0));}
     Check(edit.Draft.x>x && edit.Draft.width>w,"drag caption moves box and corner grip resizes it");
     id=edit.Draft.id;edit.Input.Text="中文批注，可直接编辑\r\n第二行内容";stage=1;timer.Start();return;
    }
    if(stage==1)
    {
     Check(Disk(s,id)?.text.Contains("第二行")==true,"debounced Chinese multiline text persisted");
     edit.Input.Text="中文批注，可直接编辑\r\n第二行内容·新输入";v.ShowInlineSelection();Check(edit.Input.Text.EndsWith("新输入"),"reopening quick style preserves pending text instead of stale selection");
     Check(edit.ColorButtons.Count==6 && !edit.StylePanel.Controls.OfType<TextBox>().Any(),"six quick color circles and no custom color entry");
     edit.ColorButtons[4].PerformClick();Check(edit.Input.ForeColor.ToArgb()==ColorTranslator.FromHtml("#2878D7").ToArgb() && edit.Draft.color=="#2878D7","color circle updates text color and color code");
     var values=edit.StylePanel.Controls.OfType<Label>().Select(l=>l.Text);Check(values.Contains("#2878D7"),"selected hex code displayed");
     edit.SizeSlider.Value=18;edit.TransparencySlider.Value=35;
     var plus=edit.StylePanel.Controls.OfType<Button>().First(b=>b.Text=="+" && b.Top==42);var minus=edit.StylePanel.Controls.OfType<Button>().First(b=>b.Text=="−" && b.Top==42);
     plus.PerformClick();Check(edit.Draft.fontSize==19,"plus button increases size");minus.PerformClick();Check(edit.Draft.fontSize==18,"minus button decreases size");
     edit.TransparencySlider.Value=100;Check(edit.Draft.opacity==0,"transparency reaches completely transparent");edit.TransparencySlider.Value=35;
     expected=new RectangleF(.26f,.24f,.32f,.12f);edit.SetBounds(expected);Key(form,Keys.Control|Keys.S);
     var saved=Disk(s,id);Check(saved.fontSize==18 && Math.Abs(saved.opacity-.65)<.001 && saved.color=="#2878D7" && Math.Abs(saved.width-.32)<.001,"Ctrl S flushes pending style and normalized geometry");
     Check(!(bool)Call(form,"HandleAnnotationShortcut",s,Keys.Delete) && !(bool)Call(form,"HandleAnnotationShortcut",s,Keys.Control|Keys.Z),"text editing owns Delete and Ctrl Z");
     v.GoToLocation(0,.1,0);v.SetZoom(.9f);Check(edit.IsOpen && edit.StylePanel.Visible,"inline controls follow page scrolling and zoom");
     int before=v.AutoScrollPosition.Y;edit.ColorButtons[1].PerformClick();Check(v.AutoScrollPosition.Y==before,"refocusing text does not jump reading position");edit.ColorButtons[4].PerformClick();
     v.RenderPagesNearViewport();Shot(form,Path.Combine(folder,"inline-editor.png"));Shot(edit.StylePanel,Path.Combine(folder,"quick-style.png"));
     Key(form,Keys.Control|Keys.Enter);Check(!edit.IsOpen,"Ctrl Enter finishes edit");
     v.AnnotationLayer.Begin(0,new PointF(.3f,.28f),.01f,.01f);v.AnnotationLayer.Cancel();v.ShowInlineSelection();
     edit.Input.Text="可取消的临时输入";Key(form,Keys.Escape);Check(Disk(s,id).text.Contains("第二行"),"Escape discards pending edit while keeping saved text");
     v.ShowInlineSelection();edit.Input.Text="";Key(form,Keys.Control|Keys.Enter);Check(!s.Annotations.Any(),"clearing inline text removes the note");
     NewNote(form,v);Key(form,Keys.Escape);Check(!s.Annotations.Any(),"Escape cancels empty draft");
     NewNote(form,v);edit.Input.Text="标签切换前尚未等待保存";id=edit.Draft.id;Call(form,"OpenPdf",second);
     Check(Disk(s,id)?.text=="标签切换前尚未等待保存" && !edit.IsOpen,"switching tabs flushes pending input");
     var tabs=(DocumentTabControl)Field(form,"documentTabs");tabs.SelectedIndex=0;v.GoToPage(0,0);v.ShowInlineSelection();
     if(!edit.IsOpen){v.AnnotationLayer.Begin(0,new PointF(.3f,.24f),.01f,.01f);v.AnnotationLayer.Cancel();v.ShowInlineSelection();}
     edit.Input.Text="关闭后恢复的中文批注";edit.SetBounds(expected);edit.SizeSlider.Value=20;edit.ColorButtons[5].PerformClick();edit.TransparencySlider.Value=20;
     var keep=context.CreateWindow(new string[0]);form.Close();form=keep;Call(form,"OpenPdf",pdf);stage=2;timer.Start();return;
    }
    Check(s.Annotations.Count==1 && s.Annotations[0].id==id && s.Annotations[0].text=="关闭后恢复的中文批注","close before debounce and reopen restores last input");
    var note=s.Annotations[0];Check(note.fontFamily=="SimSun" && note.fontSize==20 && note.color=="#8051B5" && Math.Abs(note.opacity-.8)<.001 && Math.Abs(note.x-expected.X)<.001,"font color opacity and geometry survive reopening");
    v.AnnotationLayer.Begin(0,new PointF((float)(note.x+.03),(float)(note.y+.03)),.01f,.01f);v.AnnotationLayer.Cancel();v.ShowInlineSelection();
    form.AnnotationEditorOverride=m=>{m.text="F2 专业属性";return true;};Key(form,Keys.F2);Check(Disk(s,id).text=="F2 专业属性" && !edit.IsOpen,"F2 still dispatches professional properties");
    v.ShowInlineSelection();edit.Input.Text="拖出窗口前的输入";var detached=context.CreateWindow(new string[0]);
    Check(form.TransferSessionTo(s,detached) && Disk(s,id).text=="拖出窗口前的输入" && !edit.IsOpen,"detaching tab flushes pending inline text");
    Check(DocumentDataRepository.CalculateSha256(pdf)==hash,"original PDF unchanged");
    Console.WriteLine("INLINE_EDITOR_ACCEPTANCE_PASS "+folder);foreach(var w in context.Windows.ToArray())w.Close();
   }
   catch(Exception ex){Console.WriteLine(ex);Environment.ExitCode=1;foreach(var w in context.Windows.ToArray())w.Close();}
  };
  timer.Start();Application.Run(context);timer.Dispose();return Environment.ExitCode;
 }
}
