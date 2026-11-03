using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;

internal static class WindowsPdfRenderer
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

    public static SizeF GetPageSize(object document, int pageIndex)
    {
        object page = GetPage(document, pageIndex);
        try
        {
            object size = page.GetType().GetProperty("Size").GetValue(page, null);
            return new SizeF(
                Convert.ToSingle(size.GetType().GetProperty("Width").GetValue(size, null)),
                Convert.ToSingle(size.GetType().GetProperty("Height").GetValue(size, null)));
        }
        finally
        {
            (page as IDisposable)?.Dispose();
        }
    }

    public static Bitmap Render(object document, int pageIndex, float zoom)
    {
        object page = GetPage(document, pageIndex);
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
        object reader = null;
        try
        {
            MethodInfo render = pageType.GetMethods().First(m => m.Name == "RenderToStreamAsync" && m.GetParameters().Length == 2);
            AwaitAction(render.Invoke(page, new[] { stream, options }));
            ulong streamSize = Convert.ToUInt64(RandomAccessStreamType.GetProperty("Size").GetValue(stream, null));
            object inputStream = RandomAccessStreamType.GetMethod("GetInputStreamAt").Invoke(stream, new object[] { (ulong)0 });
            reader = Activator.CreateInstance(DataReaderType, new[] { inputStream });
            MethodInfo loadReader = DataReaderType.GetMethod("LoadAsync", new[] { typeof(UInt32) });
            AwaitOperation(loadReader.Invoke(reader, new object[] { checked((uint)streamSize) }), typeof(UInt32));
            byte[] bytes = new byte[checked((int)streamSize)];
            DataReaderType.GetMethods().First(m => m.Name == "ReadBytes" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(byte[]))
                .Invoke(reader, new object[] { bytes });
            using (MemoryStream memory = new MemoryStream(bytes))
            using (Image decoded = Image.FromStream(memory))
                return new Bitmap(decoded);
        }
        finally
        {
            (page as IDisposable)?.Dispose();
            (stream as IDisposable)?.Dispose();
            (reader as IDisposable)?.Dispose();
        }
    }

    public static void DisposeDocument(object document)
    {
        (document as IDisposable)?.Dispose();
    }

    private static object GetPage(object document, int pageIndex)
    {
        MethodInfo getPage = PdfDocumentType.GetMethod("GetPage", new[] { typeof(UInt32) });
        return getPage.Invoke(document, new object[] { (UInt32)pageIndex });
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
