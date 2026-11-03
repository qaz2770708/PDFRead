using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[Serializable]
internal sealed class SidecarFile
{
    public int formatVersion { get; set; } = 2;
    public int revision { get; set; }
    public List<VersionRecord> versions { get; set; } = new List<VersionRecord>();
    public string lastSavedUtc { get; set; }
}

[Serializable]
internal sealed class VersionRecord
{
    public string sourceHash { get; set; }
    public string sourceName { get; set; }
    public int pageCount { get; set; }
    public int lastPage { get; set; }
    public double lastPageOffset { get; set; }
    public float zoom { get; set; } = 0.82f;
    public List<AreaHighlight> annotations { get; set; } = new List<AreaHighlight>();
    public string updatedUtc { get; set; }
}

[Serializable]
internal sealed class AreaHighlight
{
    public string id { get; set; }
    public string type { get; set; }
    public int pageIndex { get; set; }
    public string coordinateSpace { get; set; }
    public double x { get; set; }
    public double y { get; set; }
    public double width { get; set; }
    public double height { get; set; }
    public string color { get; set; }
    public string createdUtc { get; set; }
}

internal sealed class DocumentSession
{
    public object PdfDocument;
    public string PdfPath;
    public string PdfHash;
    public string SidecarPath;
    public int PageCount;
    public SidecarFile Data;
    public VersionRecord Record;
    public List<AreaHighlight> Annotations = new List<AreaHighlight>();
    public ContinuousPdfViewer Viewer;
    public TabPage Tab;
    public bool Dirty;
    public bool ModifiedByRecoveryOrMigration;
}

internal sealed class SidecarLoadResult
{
    public SidecarFile Data;
    public VersionRecord Record;
    public bool NeedsSave;
}

internal static class DocumentDataRepository
{
    private const int CurrentFormatVersion = 2;
    private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer
    {
        MaxJsonLength = Int32.MaxValue,
        RecursionLimit = 64
    };

    public static string CalculateSha256(string path)
    {
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }

    public static SidecarLoadResult LoadForPdf(string pdfPath, string hash, int pageCount, IWin32Window owner,
        Func<string, string, DialogResult> confirm = null)
    {
        string sidecarPath = Path.ChangeExtension(pdfPath, ".pdfdata");
        string temporaryPath = sidecarPath + ".tmp";
        string backupPath = sidecarPath + ".bak";
        string parseError;
        bool migrated;
        SidecarFile main = TryRead(sidecarPath, out migrated, out parseError);
        bool mainMigrated = migrated;
        SidecarFile temporary = TryRead(temporaryPath, out migrated, out _);
        bool temporaryMigrated = migrated;
        SidecarFile backup = TryRead(backupPath, out migrated, out _);
        bool backupMigrated = migrated;

        SidecarFile data = main;
        bool needsSave = mainMigrated;
        bool mainWasPresent = File.Exists(sidecarPath);
        if (main != null)
        {
            if (temporary != null && temporary.revision > main.revision)
            {
                DialogResult recover = Ask(owner, confirm,
                    "恢复未保存数据", "发现比正式数据更新的完整恢复文件，可能包含程序异常退出前的最近修改。是否恢复？",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (recover == DialogResult.Yes)
                {
                    data = temporary;
                    needsSave = true;
                }
            }
        }
        else if (backup != null || temporary != null)
        {
            bool chooseTemporary = temporary != null && (backup == null || temporary.revision > backup.revision);
            string candidatePath = chooseTemporary ? temporaryPath : backupPath;
            SidecarFile candidate = chooseTemporary ? temporary : backup;
            bool candidateMigrated = chooseTemporary ? temporaryMigrated : backupMigrated;
            DialogResult recover = Ask(owner, confirm,
                "恢复数据", "正式数据文件" + (mainWasPresent ? "无法读取" : "不存在") + "。发现可用的" + (chooseTemporary ? "较新异常退出恢复文件" : "上次保存备份") + "，是否恢复？" +
                (String.IsNullOrEmpty(parseError) ? String.Empty : "\r\n\r\n原文件错误：" + parseError),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (recover != DialogResult.Yes)
            {
                if (mainWasPresent) return null;
                data = new SidecarFile();
            }
            else
            {
                data = candidate;
                needsSave = true;
                try
                {
                    string preserved = sidecarPath + (chooseTemporary ? ".recovered.tmp" : ".recovered.bak");
                    File.Copy(candidatePath, preserved, true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(owner, "恢复文件已读入，但无法创建保护副本：\r\n\r\n" + ex.Message,
                        "恢复提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            needsSave |= candidateMigrated;
        }
        else if (mainWasPresent)
        {
            MessageBox.Show(owner,
                "数据文件无法读取，且没有有效的恢复副本。原文件已保留，本次不会覆盖它。" +
                (String.IsNullOrEmpty(parseError) ? String.Empty : "\r\n\r\n" + parseError),
                "数据文件错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
        else
        {
            data = new SidecarFile();
        }

        if (data == null) data = new SidecarFile();
        if (data.versions == null) data.versions = new List<VersionRecord>();
        VersionRecord record = data.versions.FirstOrDefault(v => String.Equals(v.sourceHash, hash, StringComparison.OrdinalIgnoreCase));
        if (record == null && data.versions.Count > 0)
        {
            DialogResult choice = Ask(owner, confirm,
                "PDF 内容已变化", "检测到 PDF 内容与已有数据绑定的版本不同。旧注释可能不再对应当前页面。\r\n\r\n是：为当前 PDF 版本新建空数据记录并保留旧记录。\r\n否：沿用最近记录的注释并关联到当前版本。\r\n取消：取消打开。",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (choice == DialogResult.Cancel) return null;
            if (choice == DialogResult.No)
            {
                DialogResult reuse = Ask(owner, confirm,
                    "确认沿用旧注释", "继续后，旧注释可能在页面上错位。旧版本数据仍会保留。是否继续沿用？",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (reuse != DialogResult.Yes) return null;
                VersionRecord prior = data.versions[data.versions.Count - 1];
                record = CloneRecord(prior);
                record.sourceHash = hash;
                record.sourceName = Path.GetFileName(pdfPath);
                record.pageCount = pageCount;
                record.lastPage = Math.Max(0, Math.Min(record.lastPage, pageCount - 1));
                record.updatedUtc = DateTime.UtcNow.ToString("o");
                data.versions.Add(record);
                needsSave = true;
            }
        }

        if (record == null)
        {
            record = new VersionRecord
            {
                sourceHash = hash,
                sourceName = Path.GetFileName(pdfPath),
                pageCount = pageCount,
                lastPage = 0,
                lastPageOffset = 0,
                zoom = 0.82f,
                annotations = new List<AreaHighlight>(),
                updatedUtc = DateTime.UtcNow.ToString("o")
            };
            data.versions.Add(record);
            needsSave = true;
        }

        if (record.annotations == null) record.annotations = new List<AreaHighlight>();
        record.lastPage = Math.Max(0, Math.Min(record.lastPage, pageCount - 1));
        record.lastPageOffset = Math.Max(0, Math.Min(1, record.lastPageOffset));
        record.zoom = Math.Max(0.35f, Math.Min(2.2f, record.zoom <= 0 ? 0.82f : record.zoom));
        return new SidecarLoadResult { Data = data, Record = record, NeedsSave = needsSave };
    }

    public static void Save(DocumentSession session)
    {
        session.Data.revision++;
        session.Data.lastSavedUtc = DateTime.UtcNow.ToString("o");
        session.Record.updatedUtc = session.Data.lastSavedUtc;
        string temporaryPath = session.SidecarPath + ".tmp";
        string backupPath = session.SidecarPath + ".bak";
        byte[] bytes = Encoding.UTF8.GetBytes(Serializer.Serialize(session.Data));
        using (FileStream stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }

        if (File.Exists(session.SidecarPath))
        {
            if (File.Exists(backupPath)) File.Delete(backupPath);
            File.Replace(temporaryPath, session.SidecarPath, backupPath);
        }
        else
        {
            File.Move(temporaryPath, session.SidecarPath);
        }
    }

    private static SidecarFile TryRead(string path, out bool migrated, out string error)
    {
        migrated = false;
        error = null;
        if (!File.Exists(path)) return null;
        try
        {
            SidecarFile data = Serializer.Deserialize<SidecarFile>(File.ReadAllText(path, Encoding.UTF8));
            if (data == null) throw new InvalidDataException("数据文件为空。");
            if (data.formatVersion == 1)
            {
                data.formatVersion = CurrentFormatVersion;
                data.revision = Math.Max(0, data.revision);
                if (data.versions == null) data.versions = new List<VersionRecord>();
                foreach (VersionRecord record in data.versions)
                {
                    if (record.annotations == null) record.annotations = new List<AreaHighlight>();
                    if (record.zoom <= 0) record.zoom = 0.82f;
                    record.lastPageOffset = Math.Max(0, Math.Min(1, record.lastPageOffset));
                }
                migrated = true;
            }
            else if (data.formatVersion != CurrentFormatVersion)
            {
                throw new InvalidDataException("不支持的数据格式版本：" + data.formatVersion + "。");
            }
            if (data.versions == null) data.versions = new List<VersionRecord>();
            return data;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static VersionRecord CloneRecord(VersionRecord source)
    {
        return Serializer.Deserialize<VersionRecord>(Serializer.Serialize(source));
    }

    private static DialogResult Ask(IWin32Window owner, Func<string, string, DialogResult> confirm,
        string caption, string message, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        return confirm == null ? MessageBox.Show(owner, message, caption, buttons, icon) : confirm(caption, message);
    }
}
