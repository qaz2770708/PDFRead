using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

internal enum SaveFailureChoice { Keep,Retry,SaveAs }
internal static class DataDialogs
{
    internal static DialogResult VersionChoice(IWin32Window owner,string message)
    {
        using(var dialog=new Form{Text="PDF 版本不匹配",Size=new Size(550,330),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false,Font=new Font("Microsoft YaHei UI",9f)})
        {
            var label=new Label{Text=message,Dock=DockStyle.Fill,Padding=new Padding(14)};
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=50,Padding=new Padding(8)};
            string[] names={"新建记录","沿用旧注释","不加载数据","取消"};DialogResult[] choices={DialogResult.Yes,DialogResult.No,DialogResult.Ignore,DialogResult.Cancel};
            for(int i=0;i<names.Length;i++){var button=new Button{Text=names[i],Width=120,Height=30,DialogResult=choices[i]};buttons.Controls.Add(button);if(i==3)dialog.CancelButton=button;}
            dialog.Controls.Add(label);dialog.Controls.Add(buttons);return dialog.ShowDialog(owner);
        }
    }
    internal static SaveFailureChoice SaveFailure(IWin32Window owner,string message)
    {
        using(var dialog=new Form{Text="数据保存失败",Size=new Size(550,270),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false,Font=new Font("Microsoft YaHei UI",9f)})
        {
            dialog.Controls.Add(new Label{Text="内存中的修改仍保留，原 PDF 未改变。\r\n\r\n"+message,Dock=DockStyle.Fill,Padding=new Padding(14)});
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=50,Padding=new Padding(8)};
            var retry=new Button{Text="重试",Width=100,DialogResult=DialogResult.Retry};var saveAs=new Button{Text="另存数据文件…",Width=150,DialogResult=DialogResult.Yes};var keep=new Button{Text="保留修改，稍后处理",Width=200,DialogResult=DialogResult.Cancel};
            buttons.Controls.AddRange(new Control[]{retry,saveAs,keep});dialog.Controls.Add(buttons);dialog.CancelButton=keep;
            var result=dialog.ShowDialog(owner);return result==DialogResult.Retry?SaveFailureChoice.Retry:result==DialogResult.Yes?SaveFailureChoice.SaveAs:SaveFailureChoice.Keep;
        }
    }
}
internal sealed partial class PdfReaderForm
{
    private readonly ToolStripDropDownButton dataButton=new ToolStripDropDownButton("数据");
    internal Func<DocumentSession,Exception,SaveFailureChoice> SaveFailureOverride=null;
    internal Func<DocumentSession,string> DataSavePathOverride=null;
    internal Func<string,string,DialogResult> DataPromptOverride=null;
    private void InitializeDataTools()
    {
        dataButton.AutoSize=false;dataButton.Size=new Size(39,28);dataButton.ToolTipText="加载、导出、另存数据文件";
        dataButton.DropDownItems.Add("重试保存 (Ctrl+S)",null,delegate{SaveSessionWithPrompt(ActiveSession,true);});
        dataButton.DropDownItems.Add("另存并切换数据文件… (Ctrl+Shift+S)",null,delegate{SaveDataElsewhere(ActiveSession,true);});
        dataButton.DropDownItems.Add("导出随 PDF 的同名数据…",null,delegate{SaveDataElsewhere(ActiveSession,false);});
        dataButton.DropDownItems.Add("加载数据文件…",null,delegate{ChooseDataFile();});
        historyStrip.Items.Insert(1,dataButton);saveButton.Size=new Size(35,28);annotationUndo.Size=annotationRedo.Size=new Size(32,28);
    }
    private bool SaveDataElsewhere(DocumentSession s,bool switchTarget)
    {
        if(s==null)return false;s.Viewer.FlushInlineEditing();CaptureSessionData(s);
        string path=null;
        if(DataSavePathOverride!=null)path=DataSavePathOverride(s);
        else using(var dialog=new SaveFileDialog{Title=switchTarget?"另存并切换数据文件":"导出同名数据，与 PDF 一起复制",Filter="PDF 阅读数据 (*.pdfdata)|*.pdfdata",DefaultExt="pdfdata",AddExtension=true,FileName=Path.GetFileNameWithoutExtension(s.PdfPath)+".pdfdata",OverwritePrompt=true})
            if(dialog.ShowDialog(this)==DialogResult.OK)path=dialog.FileName;
        if(String.IsNullOrWhiteSpace(path))return false;
        try
        {
            DocumentDataRepository.SaveAs(s,path,switchTarget);
            if(switchTarget){s.Dirty=false;s.SaveFailed=false;s.ModifiedByRecoveryOrMigration=false;pendingProgressSaves.Remove(s);UpdateTabTitle(s);ShowSaveState(s);}
            else if(ActiveSession==s)SetStatus("已导出数据",Color.FromArgb(24,121,78));
            return true;
        }
        catch(Exception ex){if(DataPromptOverride!=null)DataPromptOverride("另存失败",ex.Message);else MessageBox.Show(this,"另存失败，修改仍保留在内存中。\r\n"+ex.Message,"另存失败",MessageBoxButtons.OK,MessageBoxIcon.Error);return false;}
    }
    private void ChooseDataFile()
    {
        var s=ActiveSession;if(s==null)return;
        using(var dialog=new OpenFileDialog{Title="加载 PDF 阅读数据",Filter="PDF 阅读数据 (*.pdfdata)|*.pdfdata"})
            if(dialog.ShowDialog(this)==DialogResult.OK)LoadDataFile(s,dialog.FileName);
    }
    internal bool LoadDataFile(DocumentSession s,string path)
    {
        s.Viewer.FinishInlineEditing();if(s.Dirty&&!SaveSessionWithPrompt(s,true))return false;
        var loaded=DocumentDataRepository.LoadForPdf(s.PdfPath,s.PdfHash,s.PageCount,this,DataPromptOverride,path);if(loaded==null)return false;
        if(!loaded.ReadOnly && File.Exists(loaded.TargetPath))
        {
            string message="将用所选文件的数据替换当前会话，原有正式数据会保留为备份。是否继续加载？";
            var choice=DataPromptOverride!=null?DataPromptOverride("加载数据文件",message):MessageBox.Show(this,message,"加载数据文件",MessageBoxButtons.YesNo,MessageBoxIcon.Question);
            if(choice!=DialogResult.Yes)return false;
        }
        ApplyLoadedData(s,loaded);s.Viewer.SetAnnotations(s.Annotations);RestoreSessionPosition(s);RefreshNavigation(s);UpdateToolbar(s);
        if(s.Dirty)SaveSession(s);return true;
    }
    private static void ApplyLoadedData(DocumentSession s,SidecarLoadResult loaded)
    {
        s.SidecarPath=loaded.TargetPath;s.Data=loaded.Data;s.Record=loaded.Record;s.Annotations=loaded.Record.annotations;s.DataReadOnly=loaded.ReadOnly;s.Dirty=loaded.NeedsSave;s.ModifiedByRecoveryOrMigration=loaded.NeedsSave;s.ExpectedDataHash=loaded.ExpectedDataHash;s.DataBaselineCaptured=true;s.SaveFailed=false;s.IndexWarning=null;
    }
    private void CaptureSessionData(DocumentSession s)
    {
        if(s==ActiveSession && !s.Restoring){int page;double offset;s.Viewer.CaptureReadingPosition(out page,out offset);s.Record.lastPage=page;s.Record.lastPageOffset=offset;s.Record.lastPageHorizontal=s.Viewer.CaptureHorizontalPosition();s.Record.zoom=s.Viewer.Zoom;}
        s.Record.annotations=s.Annotations;
        if(!s.Data.versions.Contains(s.Record))s.Data.versions.Add(s.Record);
    }
    private void ShowSaveState(DocumentSession s)
    {
        if(ActiveSession!=s)return;
        if(s.SaveFailed)SetStatus("保存失败",Color.Firebrick);
        else if(s.DataReadOnly)SetStatus(s.Dirty || s.Viewer.InlineEditor.HasPendingChanges?"临时·未保存":"临时阅读",Color.DarkOrange);
        else SetStatus(s.Dirty || s.Viewer.InlineEditor.HasPendingChanges?"未保存":"已保存",s.Dirty || s.Viewer.InlineEditor.HasPendingChanges?Color.DarkOrange:Color.FromArgb(24,121,78));
        statusLabel.ToolTipText=statusLabel.Text+"\r\n"+s.SidecarPath+(s.IndexWarning==null?"":"\r\n"+s.IndexWarning);
    }
    private bool SaveSession(DocumentSession s){return SaveSessionWithPrompt(s,false);}
    private bool SaveSessionWithPrompt(DocumentSession s,bool manual)
    {
        if(s==null||s.Restoring)return true;if(s.Saving)return false;
        s.Saving=true;
        try
        {
            s.Viewer.FlushInlineEditing();if(!s.Dirty){ShowSaveState(s);return true;}
            if(s.DataReadOnly){ShowSaveState(s);return manual&&SaveDataElsewhere(s,true);}
            if(s.SaveFailed&&!manual){ShowSaveState(s);return false;}
            CaptureSessionData(s);
            while(true)
            {
                if(ActiveSession==s){SetStatus("保存中…",Color.SlateGray);statusLabel.Owner?.Refresh();}
                try
                {
                    DocumentDataRepository.Save(s);s.Dirty=false;s.SaveFailed=false;s.ModifiedByRecoveryOrMigration=false;pendingProgressSaves.Remove(s);UpdateTabTitle(s);ShowSaveState(s);return true;
                }
                catch(Exception ex)
                {
                    s.SaveFailed=true;UpdateTabTitle(s);ShowSaveState(s);
                    var choice=SaveFailureOverride!=null?SaveFailureOverride(s,Unwrap(ex)):DataDialogs.SaveFailure(this,Unwrap(ex).Message);
                    if(choice==SaveFailureChoice.Retry)continue;
                    if(choice==SaveFailureChoice.SaveAs)return SaveDataElsewhere(s,true);
                    return false;
                }
            }
        }
        finally{s.Saving=false;}
    }
}
