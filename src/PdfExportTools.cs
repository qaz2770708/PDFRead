using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

internal sealed partial class PdfReaderForm
{
    internal Func<DocumentSession,string> PdfExportPathOverride=null;
    internal Func<string,bool> PdfExportOverwriteOverride=null;
    private void ChooseAnnotatedPdfExport()
    {
        var session=ActiveSession;if(session==null||session.Restoring)return;
        session.Viewer.FinishInlineEditing();
        if(session.DataReadOnly){if(!SaveDataElsewhere(session,true))return;}
        else if(session.Dirty&&!SaveSessionWithPrompt(session,true))return;
        string path=null;
        if(PdfExportPathOverride!=null)path=PdfExportPathOverride(session);
        else using(var dialog=new SaveFileDialog{Title="导出带注释的 PDF",Filter="PDF 文件 (*.pdf)|*.pdf",DefaultExt="pdf",AddExtension=true,FileName=Path.GetFileNameWithoutExtension(session.PdfPath)+"-带注释.pdf",OverwritePrompt=false})
            if(dialog.ShowDialog(this)==DialogResult.OK)path=dialog.FileName;
        if(String.IsNullOrWhiteSpace(path))return;
        try
        {
            PdfAnnotationExporter.ValidateDestination(session.PdfPath,session.SidecarPath,path);
            bool overwrite=false;
            if(File.Exists(path))
            {
                overwrite=PdfExportOverwriteOverride!=null?PdfExportOverwriteOverride(path):MessageBox.Show(this,"目标文件已存在，是否覆盖？\r\n"+path,"确认覆盖导出文件",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes;
                if(!overwrite)return;
            }
            if(DocumentDataRepository.Fingerprint(session.SidecarPath)!=session.ExpectedDataHash)throw new IOException("数据文件在打开后被其他程序修改，请重新加载数据后导出。");
            UseWaitCursor=true;SetStatus("导出中…",Color.SlateGray);statusLabel.Owner?.Refresh();
            PdfAnnotationExporter.Export(session.PdfPath,session.SidecarPath,path,session.PdfHash,overwrite);
            SetStatus("PDF 已导出",Color.FromArgb(24,121,78));statusLabel.ToolTipText="已导出到："+Path.GetFullPath(path)+"\r\n原 PDF 保持不变。";
        }
        catch(Exception ex)
        {
            ShowSaveState(session);
            if(DataPromptOverride!=null)DataPromptOverride("PDF 导出失败",Unwrap(ex).Message);
            else MessageBox.Show(this,"PDF 导出失败，原 PDF 保持不变。\r\n\r\n"+Unwrap(ex).Message,"PDF 导出失败",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
        finally{UseWaitCursor=false;}
    }
}
