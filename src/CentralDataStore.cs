using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

internal sealed class LibraryLocation
{
    public string path { get; set; }
    public string hash { get; set; }
    public string dataPath { get; set; }
}
internal sealed class LibraryIndex
{
    public int formatVersion { get; set; } = 2;
    public List<LibraryLocation> locations { get; set; } = new List<LibraryLocation>();
}
internal static partial class DocumentDataRepository
{
    internal static string DataRootOverride=null;
    public static string DataRoot { get { return DataRootOverride ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data"); } }
    private static void CheckHash(string hash)
    {
        if(String.IsNullOrEmpty(hash) || hash.Length!=64 || !hash.All(Uri.IsHexDigit))throw new InvalidDataException("PDF SHA-256 无效。");
    }
    // A hash namespace avoids collisions while preserving the portable PDF basename.
    public static string GetDataPath(string hash,string pdfPath=null)
    {
        CheckHash(hash);string folder=Path.Combine(DataRoot,hash.ToLowerInvariant());
        if(Directory.Exists(folder))
        {
            string existing=Directory.GetFiles(folder,"*.pdfdata").OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if(existing!=null)return existing;
            var recovery=Directory.GetFiles(folder,"*.pdfdata.tmp").Concat(Directory.GetFiles(folder,"*.pdfdata.bak")).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if(recovery!=null)return recovery.Substring(0,recovery.Length-4);
        }
        if(pdfPath!=null)return Path.Combine(folder,Path.GetFileNameWithoutExtension(pdfPath)+".pdfdata");
        return Path.Combine(DataRoot,hash.ToLowerInvariant()+".pdfdata"); // pre-M5 compatibility lookup
    }
    private static bool HasData(string path){return File.Exists(path)||File.Exists(path+".tmp")||File.Exists(path+".bak");}
    private static LibraryIndex ReadIndex()
    {
        foreach(string suffix in new[]{"",".bak"})try
        {
            string path=Path.Combine(DataRoot,"index.json")+suffix;if(!File.Exists(path))continue;
            var index=Serializer.Deserialize<LibraryIndex>(File.ReadAllText(path,Encoding.UTF8));
            if(index!=null && index.locations!=null && index.locations.All(x=>x!=null))return index;
        }catch(Exception ex){System.Diagnostics.Debug.WriteLine(ex.Message);}
        return new LibraryIndex();
    }
    private static string FindExistingData(string pdfPath,string hash)
    {
        string paired=GetDataPath(hash,pdfPath);
        var active=ReadIndex().locations.LastOrDefault(x=>String.Equals(x.path,Path.GetFullPath(pdfPath),StringComparison.OrdinalIgnoreCase)&&x.hash==hash);
        if(active!=null && !String.IsNullOrEmpty(active.dataPath) && HasData(active.dataPath))return active.dataPath;
        if(HasData(paired))return paired;
        string oldHash=Path.Combine(DataRoot,hash.ToLowerInvariant()+".pdfdata");if(HasData(oldHash))return oldHash;
        string full=Path.GetFullPath(pdfPath);var previous=ReadIndex().locations.LastOrDefault(x=>String.Equals(x.path,full,StringComparison.OrdinalIgnoreCase));
        if(previous!=null)
        {
            if(!String.IsNullOrEmpty(previous.dataPath) && HasData(previous.dataPath))return previous.dataPath;
            try {string old=GetDataPath(previous.hash);if(HasData(old))return old;}catch(InvalidDataException){}
        }
        string portable=Path.ChangeExtension(full,".pdfdata");if(HasData(portable))return portable;
        string rootCopy=Path.Combine(DataRoot,Path.GetFileName(portable));if(HasData(rootCopy))return rootCopy;
        if(Directory.Exists(DataRoot))
        {
            var named=Directory.GetFiles(DataRoot,Path.GetFileName(portable)).Concat(Directory.GetDirectories(DataRoot).Where(d=>Path.GetFileName(d).Length==64 && Path.GetFileName(d).All(Uri.IsHexDigit)).SelectMany(d=>Directory.GetFiles(d,Path.GetFileName(portable)))).ToArray();
            if(named.Length==1)return named[0]; // ambiguous names require explicit data-menu import
        }
        return paired;
    }
    internal static void RegisterLocation(string pdfPath,string hash,string dataPath=null)
    {
        string full=Path.GetFullPath(pdfPath);LibraryIndex index=ReadIndex();
        var existing=index.locations.FirstOrDefault(x=>String.Equals(x.path,full,StringComparison.OrdinalIgnoreCase));
        string target=dataPath??GetDataPath(hash,pdfPath);
        if(existing!=null && existing.hash==hash && existing.dataPath==target)return;
        if(existing==null){existing=new LibraryLocation{path=full};index.locations.Add(existing);}
        existing.hash=hash;existing.dataPath=target;index.formatVersion=2;
        Directory.CreateDirectory(DataRoot);string path=Path.Combine(DataRoot,"index.json"),temp=path+".tmp";
        if(File.Exists(temp))PreserveFile(temp,"previous-temp");
        WriteDurable(temp,Encoding.UTF8.GetBytes(Serializer.Serialize(index)));
        if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);
    }
}
