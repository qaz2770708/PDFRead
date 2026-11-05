using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

internal sealed class LibraryLocation
{
    public string path { get; set; }
    public string hash { get; set; }
}
internal sealed class LibraryIndex
{
    public int formatVersion { get; set; } = 1;
    public List<LibraryLocation> locations { get; set; } = new List<LibraryLocation>();
}
internal static partial class DocumentDataRepository
{
    internal static string DataRootOverride = null;
    public static string DataRoot { get { return DataRootOverride ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"); } }
    public static string GetDataPath(string hash)
    {
        if (String.IsNullOrEmpty(hash) || hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("PDF 哈希无效。");
        return Path.Combine(DataRoot, hash.ToLowerInvariant() + ".pdfdata");
    }
    private static bool HasData(string path) { return File.Exists(path) || File.Exists(path + ".tmp") || File.Exists(path + ".bak"); }
    private static LibraryIndex ReadIndex()
    {
        foreach (string suffix in new[] { "", ".bak" })
        {
            try
            {
                string path = Path.Combine(DataRoot, "index.json") + suffix;
                if (!File.Exists(path)) continue;
                var index = Serializer.Deserialize<LibraryIndex>(File.ReadAllText(path, Encoding.UTF8));
                if (index != null && index.locations != null) return index;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Data index: " + ex.Message); }
        }
        return new LibraryIndex();
    }
    private static string FindExistingData(string pdfPath, string hash)
    {
        string canonical = GetDataPath(hash);
        if (HasData(canonical)) return canonical;
        string full = Path.GetFullPath(pdfPath);
        var previous = ReadIndex().locations.LastOrDefault(x => String.Equals(x.path, full, StringComparison.OrdinalIgnoreCase));
        if (previous != null && previous.hash != hash)
        {
            try { string old = GetDataPath(previous.hash); if (HasData(old)) return old; }
            catch (InvalidDataException) { }
        }
        // Import the previous releases' sidecar once; never write back beside the PDF.
        string legacy = Path.ChangeExtension(full, ".pdfdata");
        return HasData(legacy) ? legacy : canonical;
    }
    internal static void RegisterLocation(string pdfPath, string hash)
    {
        string full = Path.GetFullPath(pdfPath);
        LibraryIndex index = ReadIndex();
        var existing = index.locations.FirstOrDefault(x => String.Equals(x.path, full, StringComparison.OrdinalIgnoreCase));
        if (existing != null && existing.hash == hash) return;
        if (existing == null) index.locations.Add(new LibraryLocation { path = full, hash = hash });
        else existing.hash = hash;
        Directory.CreateDirectory(DataRoot);
        string path = Path.Combine(DataRoot, "index.json"), temp = path + ".tmp";
        byte[] bytes = Encoding.UTF8.GetBytes(Serializer.Serialize(index));
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
        else File.Move(temp, path);
    }
}
