using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new PdfReaderForm(args.Length > 0 ? args[0] : null));
    }
}

internal sealed class PdfReaderForm : Form
{
    private readonly ToolStrip toolStrip = new ToolStrip();
    private readonly ToolStripButton openButton = new ToolStripButton("打开 PDF");
    private readonly ToolStripButton previousButton = new ToolStripButton("‹");
    private readonly ToolStripTextBox pageBox = new ToolStripTextBox { Width = 42, TextBoxTextAlign = HorizontalAlignment.Center, Enabled = false };
    private readonly ToolStripLabel pageTotalLabel = new ToolStripLabel("/ -");
    private readonly ToolStripButton nextButton = new ToolStripButton("›");
    private readonly ToolStripButton zoomOutButton = new ToolStripButton("−");
    private readonly ToolStripLabel zoomLabel = new ToolStripLabel("82%");
    private readonly ToolStripButton zoomInButton = new ToolStripButton("+");
    private readonly ToolStripButton highlightButton = new ToolStripButton("区域高亮：拖拽框选") { CheckOnClick = true, Enabled = false };
    private readonly ToolStripButton saveButton = new ToolStripButton("保存") { Enabled = false };
    private readonly ToolStripLabel documentLabel = new ToolStripLabel("请打开 PDF") { TextAlign = ContentAlignment.MiddleLeft };
    private readonly StatusStrip statusStrip = new StatusStrip();
    private readonly ToolStripStatusLabel statusLabel = new ToolStripStatusLabel("未打开文档");
    private readonly Panel scrollPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(233, 237, 242), Padding = new Padding(28) };
    private readonly PageCanvas pageCanvas = new PageCanvas { BackColor = Color.White };
    private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue, RecursionLimit = 64 };

    private object pdfDocument;
    private string pdfPath;
    private string sidecarPath;
    private string pdfHash;
    private int pageCount;
    private int pageIndex;
    private float zoom = 0.82f;
    private bool dirty;
    private SidecarFile sidecarData;
    private VersionRecord activeRecord;
    private readonly List<AreaHighlight> annotations = new List<AreaHighlight>();

    public PdfReaderForm(string initialPath)
    {
        Text = "PDF 阅读器 - 里程碑 1";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 560);
        Size = new Size(1120, 820);
        BackColor = Color.FromArgb(244, 246, 248);

        toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        toolStrip.Padding = new Padding(10, 4, 8, 4);
        toolStrip.Items.Add(openButton);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(previousButton);
        toolStrip.Items.Add(pageBox);
        toolStrip.Items.Add(pageTotalLabel);
        toolStrip.Items.Add(nextButton);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(zoomOutButton);
        toolStrip.Items.Add(zoomLabel);
        toolStrip.Items.Add(zoomInButton);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(highlightButton);
        toolStrip.Items.Add(saveButton);
        toolStrip.Dock = DockStyle.Top;
        toolStrip.BackColor = Color.White;

        statusStrip.Items.Add(documentLabel);
        statusStrip.Items.Add(statusLabel);
        statusStrip.Dock = DockStyle.Bottom;
        statusStrip.BackColor = Color.White;

        pageCanvas.Margin = new Padding(24);
        pageCanvas.Location = new Point(28, 28);
        scrollPanel.Controls.Add(pageCanvas);
        Controls.Add(scrollPanel);
        Controls.Add(statusStrip);
        Controls.Add(toolStrip);

        openButton.Click += delegate { ShowOpenDialog(); };
        previousButton.Click += delegate { NavigateTo(pageIndex - 1); };
        nextButton.Click += delegate { NavigateTo(pageIndex + 1); };
        zoomOutButton.Click += delegate { ChangeZoom(-0.1f); };
        zoomInButton.Click += delegate { ChangeZoom(0.1f); };
        saveButton.Click += delegate { SaveDocumentData(); };
        pageBox.KeyDown += PageBoxKeyDown;
        pageCanvas.HighlightCreated += OnHighlightCreated;
        pageCanvas.HighlightMode = false;
        highlightButton.CheckedChanged += delegate { pageCanvas.HighlightMode = highlightButton.Checked; };
        FormClosing += OnFormClosing;
        Shown += delegate { if (!String.IsNullOrWhiteSpace(initialPath) && File.Exists(initialPath)) OpenPdf(initialPath); };
        UpdateNavigationState();
    }

    private void ShowOpenDialog()
    {
        using (OpenFileDialog dialog = new OpenFileDialog())
        {
            dialog.Filter = "PDF 文件 (*.pdf)|*.pdf|所有文件 (*.*)|*.*";
            dialog.Multiselect = false;
            if (dialog.ShowDialog(this) == DialogResult.OK)
                OpenPdf(dialog.FileName);
        }
    }

    private void OpenPdf(string path)
    {
        if (dirty && !SaveDocumentData()) return;
        try
        {
            string fullPath = Path.GetFullPath(path);
            string hash = CalculateSha256(fullPath);
            object newDocument = PdfRenderer.Open(fullPath, out int newPageCount);
            VersionRecord record = LoadMatchingRecord(fullPath, hash, newPageCount);
            if (record == null)
            {
                PdfRenderer.DisposeDocument(newDocument);
                return;
            }

            PdfRenderer.DisposeDocument(pdfDocument);
            pdfDocument = newDocument;
            pdfPath = fullPath;
            sidecarPath = Path.ChangeExtension(fullPath, ".pdfdata");
            pdfHash = hash;
            pageCount = newPageCount;
            activeRecord = record;
            annotations.Clear();
            annotations.AddRange(record.annotations ?? new List<AreaHighlight>());
            pageIndex = Math.Max(0, Math.Min(record.lastPage, pageCount - 1));
            pageBox.Enabled = true;
            highlightButton.Enabled = true;
            saveButton.Enabled = true;
            dirty = false;
            Text = "PDF 阅读器 - " + Path.GetFileName(fullPath);
            documentLabel.Text = fullPath;
            SetStatus("已保存", Color.FromArgb(24, 121, 78));
            RenderCurrentPage();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法打开或渲染 PDF。\r\n\r\n" + Unwrap(ex).Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private VersionRecord LoadMatchingRecord(string path, string hash, int pages)
    {
        sidecarPath = Path.ChangeExtension(path, ".pdfdata");
        sidecarData = new SidecarFile();
        if (File.Exists(sidecarPath))
        {
            try
            {
                sidecarData = json.Deserialize<SidecarFile>(File.ReadAllText(sidecarPath)) ?? new SidecarFile();
                if (sidecarData.formatVersion != 1)
                {
                    MessageBox.Show(this, "数据文件格式暂不支持。为保护数据，本次不会写入此文件。", "数据格式不兼容", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                if (sidecarData.versions == null) sidecarData.versions = new List<VersionRecord>();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "数据文件无法读取，原文件已保留且不会被覆盖。\r\n\r\n" + ex.Message, "数据文件错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        VersionRecord match = sidecarData.versions.FirstOrDefault(v => String.Equals(v.sourceHash, hash, StringComparison.OrdinalIgnoreCase));
        if (match != null) return match;
        if (sidecarData.versions.Count > 0)
        {
            DialogResult choice = MessageBox.Show(this,
                "检测到 PDF 内容与已有数据绑定的版本不同。旧注释可能不再对应当前页面。\r\n\r\n是：为当前 PDF 版本新建空数据记录并保留旧记录。\r\n否：沿用最近记录的注释并关联到当前版本。\r\n取消：取消打开。",
                "PDF 内容已变化", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (choice == DialogResult.Cancel) return null;
            if (choice == DialogResult.No)
            {
                DialogResult confirm = MessageBox.Show(this, "继续后，旧注释可能在页面上错位。旧版本数据仍会保留。是否继续沿用？",
                    "确认沿用旧注释", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return null;
                VersionRecord prior = sidecarData.versions[sidecarData.versions.Count - 1];
                VersionRecord copy = CloneRecord(prior);
                copy.sourceHash = hash;
                copy.sourceName = Path.GetFileName(path);
                copy.pageCount = pages;
                copy.lastPage = Math.Max(0, Math.Min(copy.lastPage, pages - 1));
                copy.updatedUtc = DateTime.UtcNow.ToString("o");
                sidecarData.versions.Add(copy);
                return copy;
            }
        }

        VersionRecord created = new VersionRecord
        {
            sourceHash = hash,
            sourceName = Path.GetFileName(path),
            pageCount = pages,
            lastPage = 0,
            annotations = new List<AreaHighlight>(),
            updatedUtc = DateTime.UtcNow.ToString("o")
        };
        sidecarData.versions.Add(created);
        return created;
    }

    private static VersionRecord CloneRecord(VersionRecord source)
    {
        JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
        return serializer.Deserialize<VersionRecord>(serializer.Serialize(source));
    }

    private void RenderCurrentPage()
    {
        if (pdfDocument == null) return;
        try
        {
            Bitmap image = PdfRenderer.Render(pdfDocument, pageIndex, zoom);
            pageCanvas.SetPage(image, annotations.Where(a => a.pageIndex == pageIndex).ToList());
            pageBox.Text = (pageIndex + 1).ToString();
            pageTotalLabel.Text = "/ " + pageCount;
            zoomLabel.Text = Math.Round(zoom * 100).ToString() + "%";
            UpdateNavigationState();
            scrollPanel.AutoScrollPosition = new Point(0, 0);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法渲染 PDF 页面。\r\n\r\n" + Unwrap(ex).Message, "渲染失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void NavigateTo(int target)
    {
        if (pdfDocument == null || target < 0 || target >= pageCount || target == pageIndex) return;
        pageIndex = target;
        dirty = true;
        SaveDocumentData();
        RenderCurrentPage();
    }

    private void ChangeZoom(float delta)
    {
        if (pdfDocument == null) return;
        zoom = Math.Max(0.35f, Math.Min(2.2f, zoom + delta));
        RenderCurrentPage();
    }

    private void PageBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || pdfDocument == null) return;
        if (Int32.TryParse(pageBox.Text, out int requested)) NavigateTo(Math.Max(0, Math.Min(pageCount - 1, requested - 1)));
        e.SuppressKeyPress = true;
    }

    private void OnHighlightCreated(object sender, RectangleF rectangle)
    {
        AreaHighlight annotation = new AreaHighlight
        {
            id = Guid.NewGuid().ToString("N"),
            type = "area_highlight",
            pageIndex = pageIndex,
            coordinateSpace = "normalized_page",
            x = Math.Round(rectangle.X / pageCanvas.Width, 8),
            y = Math.Round(rectangle.Y / pageCanvas.Height, 8),
            width = Math.Round(rectangle.Width / pageCanvas.Width, 8),
            height = Math.Round(rectangle.Height / pageCanvas.Height, 8),
            color = "#FFFF66",
            createdUtc = DateTime.UtcNow.ToString("o")
        };
        annotations.Add(annotation);
        pageCanvas.SetPage(pageCanvas.PageImage, annotations.Where(a => a.pageIndex == pageIndex).ToList());
        dirty = true;
        SetStatus("未保存", Color.FromArgb(180, 83, 9));
        SaveDocumentData();
    }

    private bool SaveDocumentData()
    {
        if (String.IsNullOrEmpty(pdfPath) || activeRecord == null || !dirty) return true;
        try
        {
            activeRecord.lastPage = pageIndex;
            activeRecord.annotations = annotations.ToList();
            activeRecord.updatedUtc = DateTime.UtcNow.ToString("o");
            sidecarData.formatVersion = 1;
            sidecarData.lastSavedUtc = DateTime.UtcNow.ToString("o");
            string temporary = sidecarPath + ".tmp";
            File.WriteAllText(temporary, json.Serialize(sidecarData), new System.Text.UTF8Encoding(false));
            if (File.Exists(sidecarPath))
            {
                string backup = sidecarPath + ".bak";
                if (File.Exists(backup)) File.Delete(backup);
                File.Replace(temporary, sidecarPath, backup);
            }
            else
            {
                File.Move(temporary, sidecarPath);
            }
            dirty = false;
            SetStatus("已保存", Color.FromArgb(24, 121, 78));
            return true;
        }
        catch (Exception ex)
        {
            SetStatus("保存失败：请重试", Color.FromArgb(180, 35, 24));
            MessageBox.Show(this, "无法保存独立数据文件。原 PDF 未被修改。\r\n\r\n" + ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void SetStatus(string text, Color color)
    {
        statusLabel.Text = text;
        statusLabel.ForeColor = color;
    }

    private void UpdateNavigationState()
    {
        previousButton.Enabled = pdfDocument != null && pageIndex > 0;
        nextButton.Enabled = pdfDocument != null && pageIndex < pageCount - 1;
    }

    private void OnFormClosing(object sender, FormClosingEventArgs e)
    {
        if (dirty && !SaveDocumentData()) e.Cancel = true;
        if (!e.Cancel) PdfRenderer.DisposeDocument(pdfDocument);
    }

    private static string CalculateSha256(string path)
    {
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }

    private static Exception Unwrap(Exception ex)
    {
        AggregateException aggregate = ex as AggregateException;
        return aggregate != null && aggregate.InnerException != null ? aggregate.InnerException : ex;
    }
}

internal sealed class PageCanvas : Control
{
    private List<AreaHighlight> highlights = new List<AreaHighlight>();
    private bool dragging;
    private Point dragStart;
    private Point dragEnd;

    public bool HighlightMode { get; set; }
    public Bitmap PageImage { get; private set; }
    public event EventHandler<RectangleF> HighlightCreated;

    public PageCanvas()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
    }

    public void SetPage(Bitmap image, List<AreaHighlight> pageHighlights)
    {
        if (PageImage != null && !Object.ReferenceEquals(PageImage, image)) PageImage.Dispose();
        PageImage = image;
        highlights = pageHighlights ?? new List<AreaHighlight>();
        if (image != null)
        {
            Size = image.Size;
            MinimumSize = image.Size;
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (PageImage == null) return;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(PageImage, 0, 0, Width, Height);
        foreach (AreaHighlight annotation in highlights)
        {
            RectangleF rect = ToPixels(annotation);
            using (Brush fill = new SolidBrush(Color.FromArgb(112, 255, 230, 75)))
            using (Pen outline = new Pen(Color.FromArgb(215, 220, 158, 20), 1))
            {
                e.Graphics.FillRectangle(fill, rect);
                e.Graphics.DrawRectangle(outline, rect.X, rect.Y, rect.Width, rect.Height);
            }
        }
        if (dragging)
        {
            Rectangle rect = MakeRectangle(dragStart, dragEnd);
            using (Brush fill = new SolidBrush(Color.FromArgb(85, 255, 210, 65)))
            using (Pen outline = new Pen(Color.FromArgb(230, 218, 152, 15), 1))
            {
                e.Graphics.FillRectangle(fill, rect);
                e.Graphics.DrawRectangle(outline, rect);
            }
        }
    }

    private RectangleF ToPixels(AreaHighlight annotation)
    {
        return new RectangleF((float)(annotation.x * Width), (float)(annotation.y * Height),
            Math.Max(2, (float)(annotation.width * Width)), Math.Max(2, (float)(annotation.height * Height)));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !HighlightMode || PageImage == null) return;
        dragging = true;
        dragStart = e.Location;
        dragEnd = e.Location;
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging) return;
        dragEnd = e.Location;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!dragging || e.Button != MouseButtons.Left) return;
        dragging = false;
        Capture = false;
        dragEnd = e.Location;
        Rectangle rect = MakeRectangle(dragStart, dragEnd);
        Invalidate();
        if (rect.Width < 3 || rect.Height < 3 || Width <= 0 || Height <= 0) return;
        RectangleF normalized = new RectangleF((float)rect.X / Width, (float)rect.Y / Height,
            (float)rect.Width / Width, (float)rect.Height / Height);
        EventHandler<RectangleF> handler = HighlightCreated;
        if (handler != null) handler(this, normalized);
    }

    private static Rectangle MakeRectangle(Point first, Point second)
    {
        int x = Math.Min(first.X, second.X);
        int y = Math.Min(first.Y, second.Y);
        return new Rectangle(x, y, Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y));
    }
}

internal static class PdfRenderer
{
    private static readonly Type StorageFileType = ResolveWinRtType("Windows.Storage.StorageFile, Windows.Storage, ContentType=WindowsRuntime");
    private static readonly Type PdfDocumentType = ResolveWinRtType("Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType=WindowsRuntime");
    private static readonly Type PdfPageOptionsType = ResolveWinRtType("Windows.Data.Pdf.PdfPageRenderOptions, Windows.Data.Pdf, ContentType=WindowsRuntime");
    private static readonly Type RandomAccessStreamType = ResolveWinRtType("Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage, ContentType=WindowsRuntime");
    private static readonly Type DataReaderType = ResolveWinRtType("Windows.Storage.Streams.DataReader, Windows.Storage, ContentType=WindowsRuntime");

    public static object Open(string path, out int pageCount)
    {
        MethodInfo getFile = StorageFileType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "GetFileFromPathAsync" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(String));
        object storageFile = AwaitOperation(getFile.Invoke(null, new object[] { Path.GetFullPath(path) }), StorageFileType);
        MethodInfo load = PdfDocumentType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "LoadFromFileAsync" && m.GetParameters().Length == 1);
        object document = AwaitOperation(load.Invoke(null, new[] { storageFile }), PdfDocumentType);
        pageCount = Convert.ToInt32(PdfDocumentType.GetProperty("PageCount").GetValue(document, null));
        return document;
    }

    public static Bitmap Render(object document, int pageIndex, float zoom)
    {
        MethodInfo getPage = PdfDocumentType.GetMethod("GetPage", new[] { typeof(UInt32) });
        object page = getPage.Invoke(document, new object[] { (UInt32)pageIndex });
        Type pageType = page.GetType();
        object size = pageType.GetProperty("Size").GetValue(page, null);
        double width = Convert.ToDouble(size.GetType().GetProperty("Width").GetValue(size, null));
        double height = Convert.ToDouble(size.GetType().GetProperty("Height").GetValue(size, null));
        uint destinationWidth = (uint)Math.Max(1, Math.Ceiling(width * zoom));
        uint destinationHeight = (uint)Math.Max(1, Math.Ceiling(height * zoom));
        object options = Activator.CreateInstance(PdfPageOptionsType);
        PdfPageOptionsType.GetProperty("DestinationWidth").SetValue(options, destinationWidth, null);
        PdfPageOptionsType.GetProperty("DestinationHeight").SetValue(options, destinationHeight, null);
        object stream = Activator.CreateInstance(RandomAccessStreamType);
        MethodInfo render = pageType.GetMethods().First(m => m.Name == "RenderToStreamAsync" && m.GetParameters().Length == 2);
        AwaitAction(render.Invoke(page, new[] { stream, options }));
        ulong streamSize = Convert.ToUInt64(RandomAccessStreamType.GetProperty("Size").GetValue(stream, null));
        object inputStream = RandomAccessStreamType.GetMethod("GetInputStreamAt").Invoke(stream, new object[] { (ulong)0 });
        object reader = Activator.CreateInstance(DataReaderType, new[] { inputStream });
        MethodInfo loadReader = DataReaderType.GetMethod("LoadAsync", new[] { typeof(UInt32) });
        AwaitOperation(loadReader.Invoke(reader, new object[] { checked((uint)streamSize) }), typeof(UInt32));
        byte[] bytes = new byte[checked((int)streamSize)];
        DataReaderType.GetMethods().First(m => m.Name == "ReadBytes" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(byte[]))
            .Invoke(reader, new object[] { bytes });
        Bitmap result;
        using (MemoryStream memory = new MemoryStream(bytes))
        using (Image decoded = Image.FromStream(memory))
            result = new Bitmap(decoded);
        (page as IDisposable)?.Dispose();
        (stream as IDisposable)?.Dispose();
        (reader as IDisposable)?.Dispose();
        return result;
    }

    public static void DisposeDocument(object document)
    {
        IDisposable disposable = document as IDisposable;
        if (disposable != null) disposable.Dispose();
    }

    private static Type ResolveWinRtType(string name)
    {
        Type type = Type.GetType(name, false);
        if (type == null) throw new InvalidOperationException("Windows PDF rendering type is unavailable: " + name);
        return type;
    }

    private static object AwaitOperation(object operation, Type resultType)
    {
        MethodInfo asTask = typeof(System.WindowsRuntimeSystemExtensions).GetMethods()
            .First(m => m.Name == "AsTask" && m.IsGenericMethod && m.GetParameters().Length == 1);
        Task task = (Task)asTask.MakeGenericMethod(resultType).Invoke(null, new[] { operation });
        task.GetAwaiter().GetResult();
        return task.GetType().GetProperty("Result").GetValue(task, null);
    }

    private static void AwaitAction(object action)
    {
        MethodInfo asTask = typeof(System.WindowsRuntimeSystemExtensions).GetMethods()
            .First(m => m.Name == "AsTask" && !m.IsGenericMethod && m.GetParameters().Length == 1);
        Task task = (Task)asTask.Invoke(null, new[] { action });
        task.GetAwaiter().GetResult();
    }
}

[Serializable]
internal sealed class SidecarFile
{
    public int formatVersion { get; set; } = 1;
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
