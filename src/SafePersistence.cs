using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static partial class DocumentDataRepository
{
    internal const int CurrentFormatVersion=6;
    private static readonly JavaScriptSerializer Serializer=new JavaScriptSerializer{MaxJsonLength=Int32.MaxValue,RecursionLimit=128};
    private sealed class Candidate
    {
        internal SidecarFile Data;
        internal string Error;
        internal bool Migrated,Unsupported;
    }
    public static string CalculateSha256(string path)
    {
        using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
    }
    internal static string Fingerprint(string path){return File.Exists(path)?CalculateSha256(path):null;}
    private static string Checksum(SidecarFile data)
    {
        var copy=Serializer.Deserialize<SidecarFile>(Serializer.Serialize(data));copy.contentChecksum=null;
        using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Serializer.Serialize(copy)))).Replace("-","").ToLowerInvariant();
    }
    internal static string SerializeData(SidecarFile data){data.contentChecksum=Checksum(data);return Serializer.Serialize(data);}
    private static bool Finite(double v){return !Double.IsNaN(v)&&!Double.IsInfinity(v);}
    private static void Validate(SidecarFile data)
    {
        if(data==null || data.versions==null || data.revision<0)throw new InvalidDataException("数据记录结构不完整。");
        var hashes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var record in data.versions)
        {
            if(record==null)throw new InvalidDataException("版本记录为空。");CheckHash(record.sourceHash);
            if(!hashes.Add(record.sourceHash) || record.pageCount<1)throw new InvalidDataException("版本重复或页数无效。");
            if(!Finite(record.lastPageOffset)||!Finite(record.lastPageHorizontal)||!Finite(record.zoom))throw new InvalidDataException("阅读位置无效。");
            if(record.annotations==null)record.annotations=new List<AreaHighlight>();if(record.bookmarks==null)record.bookmarks=new List<UserBookmark>();
            var ids=new HashSet<string>();
            foreach(var mark in record.annotations)
            {
                if(mark==null || String.IsNullOrWhiteSpace(mark.id)||!ids.Add(mark.id)||mark.pageIndex<0||mark.pageIndex>=record.pageCount)throw new InvalidDataException("注释标识或页码无效。");
                if(!new[]{"area_highlight","underline","text_note","ink","rectangle","ellipse","line","sticky_note"}.Contains(mark.type))throw new InvalidDataException("发现不支持的注释类型。");
                if(!Finite(mark.x)||!Finite(mark.y)||!Finite(mark.width)||!Finite(mark.height)||mark.width<0||mark.height<0||!Finite(mark.opacity)||!Finite(mark.fontSize)||!Finite(mark.strokeWidth))throw new InvalidDataException("注释坐标或样式无效。");
                if(mark.points!=null && mark.points.Any(p=>p==null||!Finite(p.x)||!Finite(p.y)))throw new InvalidDataException("笔迹数据无效。");
                if(mark.textRegions!=null && mark.textRegions.Any(r=>r==null||!Finite(r.x)||!Finite(r.y)||!Finite(r.width)||!Finite(r.height)||r.quad==null||r.quad.Any(p=>p==null||!Finite(p.x)||!Finite(p.y))))throw new InvalidDataException("高亮文字位置无效。");
            }
            ids.Clear();foreach(var mark in record.bookmarks)
                if(mark==null||String.IsNullOrWhiteSpace(mark.id)||!ids.Add(mark.id)||mark.pageIndex<0||mark.pageIndex>=record.pageCount||!Finite(mark.pageOffset)||!Finite(mark.horizontalOffset)||!Finite(mark.zoom))throw new InvalidDataException("书签记录无效。");
            foreach(var bookmark in record.bookmarks)
            {
                var visited=new HashSet<string>();var current=bookmark;
                while(!String.IsNullOrEmpty(current.parentId))
                {
                    if(!visited.Add(current.id))throw new InvalidDataException("书签章节层级存在循环。");
                    current=record.bookmarks.FirstOrDefault(b=>b.id==current.parentId);
                    if(current==null)throw new InvalidDataException("书签章节父级缺失。");
                }
            }
        }
    }
    private static Candidate ReadCandidate(string path)
    {
        var result=new Candidate();if(!File.Exists(path))return result;
        try
        {
            string raw=File.ReadAllText(path,Encoding.UTF8);
            var root=Serializer.DeserializeObject(raw) as Dictionary<string,object>;
            if(root==null||!root.ContainsKey("formatVersion")||!root.ContainsKey("revision")||!root.ContainsKey("versions"))throw new InvalidDataException("数据缺少格式版本、修订号或版本列表。");
            int version=Convert.ToInt32(root["formatVersion"]);
            if(version<1||version>CurrentFormatVersion){result.Unsupported=true;throw new InvalidDataException("不支持的数据格式版本："+version+"。请使用对应版本的软件打开。");}
            // Backup original bytes before deserialization/migration can change defaults.
            if(version<CurrentFormatVersion)
            {
                string backupFolder=Path.Combine(DataRoot,"_backups",CalculateSha256(path));Directory.CreateDirectory(backupFolder);
                string backup=Path.Combine(backupFolder,"preupgrade-v"+version+".pdfdata");
                if(!File.Exists(backup))File.Copy(path,backup,false);
                else if(CalculateSha256(backup)!=CalculateSha256(path))throw new IOException("升级备份校验失败。");
            }
            var data=Serializer.Deserialize<SidecarFile>(raw);
            if(version==CurrentFormatVersion && !String.Equals(data.contentChecksum,Checksum(data),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("数据完整性校验失败。文件内容可能损坏或未完整写入。");
            Validate(data);
            result.Migrated=version<CurrentFormatVersion;data.formatVersion=CurrentFormatVersion;
            result.Data=data;
        }
        catch(Exception ex){result.Error=ex.Message;}
        return result;
    }
    internal static string PreserveFile(string path,string reason)
    {
        string folder=Path.Combine(DataRoot,"_backups",CalculateSha256(path));Directory.CreateDirectory(folder);
        string copy=Path.Combine(folder,reason+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+".pdfdata");
        File.Copy(path,copy,false);return copy;
    }
    private static DialogResult Ask(IWin32Window owner,Func<string,string,DialogResult> confirm,string caption,string message,MessageBoxButtons buttons=MessageBoxButtons.YesNo,MessageBoxIcon icon=MessageBoxIcon.Warning)
    {return confirm!=null?confirm(caption,message):MessageBox.Show(owner,message,caption,buttons,icon);}
    private static VersionRecord EmptyRecord(string pdfPath,string hash,int pages)
    {return new VersionRecord{sourceHash=hash,sourceName=Path.GetFileName(pdfPath),pageCount=pages,zoom=.82f};}
    public static SidecarLoadResult LoadForPdf(string pdfPath,string hash,int pageCount,IWin32Window owner,Func<string,string,DialogResult> confirm=null,string selectedData=null)
    {
        CheckHash(hash);string target=GetDataPath(hash,pdfPath),source=selectedData??FindExistingData(pdfPath,hash);
        // An explicitly selected alternate save location remains writable on this machine.
        var location=ReadIndex().locations.LastOrDefault(x=>String.Equals(x.path,Path.GetFullPath(pdfPath),StringComparison.OrdinalIgnoreCase)&&x.hash==hash);
        if(selectedData==null && location!=null && !String.IsNullOrEmpty(location.dataPath) && String.Equals(source,location.dataPath,StringComparison.OrdinalIgnoreCase))target=source;
        var main=ReadCandidate(source);var temp=ReadCandidate(source+".tmp");var backup=ReadCandidate(source+".bak");
        if(main.Unsupported){Ask(owner,confirm,"数据格式不支持",main.Error,MessageBoxButtons.OK);return null;}
        SidecarFile data=main.Data;bool changed=main.Migrated||!String.Equals(source,target,StringComparison.OrdinalIgnoreCase)||selectedData!=null;
        try
        {
            if(data!=null && temp.Data!=null && temp.Data.revision>data.revision)
            {
                var choice=Ask(owner,confirm,"恢复未保存数据","发现完整且修订号较新的恢复数据，是否恢复？\r\n选择否将保留恢复文件的独立副本，并使用正式数据。");
                PreserveFile(source+".tmp",choice==DialogResult.Yes?"recovered":"declined");
                if(choice==DialogResult.Yes){data=temp.Data;changed=true;}else File.Delete(source+".tmp");
            }
            else if(data==null && (temp.Data!=null||backup.Data!=null))
            {
                bool useTemp=temp.Data!=null&&(backup.Data==null||temp.Data.revision>backup.Data.revision);var candidate=useTemp?temp:backup;string path=source+(useTemp?".tmp":".bak");
                if(Ask(owner,confirm,"恢复数据","正式数据不存在、损坏或升级失败。发现有效的"+(useTemp?"恢复文件":"备份")+"，是否恢复？\r\n"+main.Error)!=DialogResult.Yes)return null;
                PreserveFile(path,"recovered");
                // Compatibility protection files are copies, never the only surviving snapshot.
                string compatibility=source+(useTemp?".recovered.tmp":".recovered.bak");if(!File.Exists(compatibility))File.Copy(path,compatibility,false);
                if(File.Exists(source))PreserveFile(source,"damaged-original");data=candidate.Data;changed=true;
            }
            else if(data==null && (File.Exists(source)||File.Exists(source+".tmp")||File.Exists(source+".bak")))
            {Ask(owner,confirm,"数据文件错误","数据损坏或升级失败，没有有效恢复副本。所有原文件保留，本次取消加载。\r\n"+(main.Error??temp.Error??backup.Error),MessageBoxButtons.OK);return null;}
        }
        catch(Exception ex){Ask(owner,confirm,"保护备份失败","无法创建保护副本，已停止恢复或升级，原数据未覆盖。\r\n"+ex.Message,MessageBoxButtons.OK);return null;}
        if(data==null)data=new SidecarFile();
        var record=data.versions.FirstOrDefault(v=>String.Equals(v.sourceHash,hash,StringComparison.OrdinalIgnoreCase));
        if(record!=null && record.pageCount!=pageCount){Ask(owner,confirm,"数据页数错误","数据页数与当前 PDF 不一致，已停止加载，原数据保留。",MessageBoxButtons.OK);return null;}
        if(record==null && data.versions.Count>0)
        {
            string message="PDF SHA-256 与已有版本不同，旧注释可能错位。\r\n\r\n新建记录：保留旧记录，创建当前版本空记录。\r\n沿用旧注释：复制旧注释、便签、书签和位置到当前版本。\r\n不加载：仅本次阅读，不自动保存数据。\r\n取消：停止打开。";
            DialogResult choice=confirm!=null?confirm("PDF 内容已变化",message):DataDialogs.VersionChoice(owner,message);
            if(choice==DialogResult.Cancel)return null;
            if(choice==DialogResult.Ignore)return new SidecarLoadResult{Data=new SidecarFile(),Record=EmptyRecord(pdfPath,hash,pageCount),ReadOnly=true,TargetPath=target,ExpectedDataHash=Fingerprint(target)};
            if(choice==DialogResult.No)
            {
                if(Ask(owner,confirm,"确认沿用旧注释","旧注释可能错位。旧版本保留不变，是否继续沿用？")!=DialogResult.Yes)return null;
                record=Serializer.Deserialize<VersionRecord>(Serializer.Serialize(data.versions.Last()));record.sourceHash=hash;record.sourceName=Path.GetFileName(pdfPath);record.pageCount=pageCount;
                record.annotations.RemoveAll(a=>a.pageIndex>=pageCount);record.bookmarks.RemoveAll(b=>b.pageIndex>=pageCount);
                foreach(var b in record.bookmarks)if(!String.IsNullOrEmpty(b.parentId)&&!record.bookmarks.Any(p=>p.id==b.parentId))b.parentId=null;
                data.versions.Add(record);changed=true;
            }
        }
        if(record==null){record=EmptyRecord(pdfPath,hash,pageCount);data.versions.Add(record);changed=true;}
        record.lastPage=Math.Max(0,Math.Min(pageCount-1,record.lastPage));record.lastPageOffset=Math.Max(0,Math.Min(1,record.lastPageOffset));record.lastPageHorizontal=Math.Max(0,Math.Min(1,record.lastPageHorizontal));record.zoom=Math.Max(.35f,Math.Min(2.2f,record.zoom<=0?.82f:record.zoom));
        if(!changed && File.Exists(target))try{RegisterLocation(pdfPath,hash,target);}catch(Exception ex){System.Diagnostics.Debug.WriteLine(ex.Message);}
        return new SidecarLoadResult{Data=data,Record=record,NeedsSave=changed,TargetPath=target,ExpectedDataHash=Fingerprint(target)};
    }
    internal static VersionRecord ReadExportRecord(string path,string hash)
    {
        var candidate=ReadCandidate(path);if(candidate.Data==null)throw new InvalidDataException("无法读取导出数据："+candidate.Error);
        var record=candidate.Data.versions.FirstOrDefault(v=>String.Equals(v.sourceHash,hash,StringComparison.OrdinalIgnoreCase));
        if(record==null)throw new InvalidDataException("数据文件中没有与原 PDF SHA-256 对应的记录。");return record;
    }
    internal static void WriteDurable(string path,byte[] bytes)
    {
        using(var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None,65536,FileOptions.WriteThrough)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
    }
    private static SidecarFile WriteSnapshot(DocumentSession session,string target,string expected,bool enforce)
    {
        if(String.Equals(Path.GetFullPath(target),Path.GetFullPath(session.PdfPath),StringComparison.OrdinalIgnoreCase))throw new IOException("数据文件不能覆盖原 PDF。");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target)));
        using(var gate=new FileStream(target+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None,1,FileOptions.DeleteOnClose))
        {
            if(enforce && Fingerprint(target)!=expected)throw new IOException("数据文件在打开后被其他程序修改或删除。请重新加载，或另存数据文件，不能覆盖现有记录。");
            var snapshot=Serializer.Deserialize<SidecarFile>(Serializer.Serialize(session.Data));snapshot.formatVersion=CurrentFormatVersion;
            var current=snapshot.versions.FirstOrDefault(v=>String.Equals(v.sourceHash,session.PdfHash,StringComparison.OrdinalIgnoreCase));if(current==null)throw new InvalidDataException("当前 PDF 没有对应的数据版本。");
            snapshot.revision=Math.Max(snapshot.revision,0)+1;snapshot.lastSavedUtc=DateTime.UtcNow.ToString("o");current.updatedUtc=snapshot.lastSavedUtc;Validate(snapshot);
            string temp=target+".tmp",backup=target+".bak";
            if(File.Exists(temp))PreserveFile(temp,"previous-temp");
            WriteDurable(temp,Encoding.UTF8.GetBytes(SerializeData(snapshot)));
            // Re-read a complete durable snapshot before the atomic replacement.
            var verified=ReadCandidate(temp);if(verified.Data==null || verified.Data.contentChecksum!=snapshot.contentChecksum)throw new InvalidDataException("写入后的数据校验失败或临时数据发生变化："+verified.Error);
            if(enforce && Fingerprint(target)!=expected)throw new IOException("保存期间发现外部修改，已保留恢复数据，未覆盖正式文件。");
            if(File.Exists(target))
            {
                var existing=ReadCandidate(target);if(existing.Unsupported)throw new InvalidDataException(existing.Error);
                if(existing.Data==null){PreserveFile(target,"damaged-original");File.Replace(temp,target,null);}
                else File.Replace(temp,target,backup);
            }
            else File.Move(temp,target);
            return snapshot;
        }
    }
    public static void Save(DocumentSession session)
    {
        if(session.DataReadOnly)throw new IOException("当前为不加载数据的临时阅读会话，请另存数据文件。");
        string path=session.SidecarPath??GetDataPath(session.PdfHash,session.PdfPath);
        var saved=WriteSnapshot(session,path,session.ExpectedDataHash,session.DataBaselineCaptured);
        session.SidecarPath=path;session.Data.revision=saved.revision;session.Data.lastSavedUtc=saved.lastSavedUtc;session.Data.contentChecksum=saved.contentChecksum;session.Record.updatedUtc=saved.lastSavedUtc;
        session.ExpectedDataHash=Fingerprint(path);session.DataBaselineCaptured=true;session.IndexWarning=null;
        try {RegisterLocation(session.PdfPath,session.PdfHash,path);foreach(var alias in session.KnownPaths)RegisterLocation(alias,session.PdfHash,path);}
        catch(Exception ex){session.IndexWarning="数据已保存，路径索引未更新："+ex.Message;}
    }
    public static void SaveAs(DocumentSession session,string path,bool switchTarget)
    {
        string full=Path.GetFullPath(path);string expected=Fingerprint(full);
        if(!String.Equals(Path.GetExtension(full),".pdfdata",StringComparison.OrdinalIgnoreCase))throw new IOException("请选择 .pdfdata 数据文件。");
        if(String.Equals(full,session.SidecarPath,StringComparison.OrdinalIgnoreCase) && !session.DataReadOnly){Save(session);return;}
        var saved=WriteSnapshot(session,full,expected,true);
        if(switchTarget)
        {
            session.IndexWarning=null;session.SidecarPath=full;session.DataReadOnly=false;session.DataBaselineCaptured=true;session.ExpectedDataHash=Fingerprint(full);session.Data.revision=saved.revision;session.Data.lastSavedUtc=saved.lastSavedUtc;session.Data.contentChecksum=saved.contentChecksum;session.Record.updatedUtc=saved.lastSavedUtc;
            try{RegisterLocation(session.PdfPath,session.PdfHash,full);}catch(Exception ex){session.IndexWarning="另存成功，路径索引未更新："+ex.Message;}
        }
    }
}
