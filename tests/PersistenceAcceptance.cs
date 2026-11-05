using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Windows.Forms;using System.Web.Script.Serialization;
internal static class PersistenceAcceptance
{
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=Int32.MaxValue};
 static string folder,pdf,hash;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static void Throws(Action action,string message){bool failed=false;try{action();}catch(Exception){failed=true;}Check(failed,message);}
 static SidecarLoadResult Load(string path=null,Func<string,string,DialogResult> prompt=null){path=path??pdf;return DocumentDataRepository.LoadForPdf(path,DocumentDataRepository.CalculateSha256(path),4,null,prompt??((a,b)=>DialogResult.Yes));}
 static DocumentSession Session(SidecarLoadResult l,string path=null){path=path??pdf;return new DocumentSession{PdfPath=path,PdfHash=DocumentDataRepository.CalculateSha256(path),PageCount=4,Data=l.Data,Record=l.Record,Annotations=l.Record.annotations,SidecarPath=l.TargetPath,ExpectedDataHash=l.ExpectedDataHash,DataBaselineCaptured=true,DataReadOnly=l.ReadOnly};}
 static void Root(string name){DocumentDataRepository.DataRootOverride=Path.Combine(folder,name,"data");Directory.CreateDirectory(DocumentDataRepository.DataRoot);}
 static IEnumerable<string> Protected(){return Directory.GetFiles(Path.Combine(DocumentDataRepository.DataRoot,"_backups"),"*.pdfdata",SearchOption.AllDirectories);}
 [STAThread]static int Main(string[] args)
 {
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  try
  {
   folder=Path.GetFullPath(Path.Combine("work","m5-data-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(folder);pdf=Path.Combine(folder,"报告.pdf");File.Copy(args[0],pdf);hash=DocumentDataRepository.CalculateSha256(pdf);Root("pc1");string firstRoot=DocumentDataRepository.DataRoot;
   var first=Load();var s=Session(first);Check(Path.GetFileName(first.TargetPath)=="报告.pdfdata" && Path.GetFileName(Path.GetDirectoryName(first.TargetPath))==hash,"PDF basename paired inside SHA namespace");
   s.Record.lastPage=1;s.Record.lastPageOffset=.3;s.Record.zoom=1.2f;
   s.Annotations.Add(new AreaHighlight{id="text",type="text_note",text="中文批注",fontFamily="SimSun",coordinateSpace="normalized_page",pageIndex=1,x=.1,y=.2,width=.3,height=.1,color="#2878D7"});
   s.Annotations.Add(new AreaHighlight{id="sticky",type="sticky_note",text="便签正文",subtitle="附加说明",coordinateSpace="normalized_page",pageIndex=2,x=.2,y=.4,width=.3,height=.1,color="#C47C0B"});
   s.Record.bookmarks.Add(new UserBookmark{id="b1",title="章节",pageIndex=1,pageOffset=.3,zoom=1.2f});s.Record.bookmarks.Add(new UserBookmark{id="b2",parentId="b1",title="子章节",pageIndex=2});
   DocumentDataRepository.Save(s);string initial=File.ReadAllText(s.SidecarPath);var reopened=Load();
   Check(!reopened.NeedsSave && reopened.Record.annotations.Count==2 && reopened.Record.bookmarks[1].parentId=="b1" && reopened.Record.lastPage==1 && Math.Abs(reopened.Record.zoom-1.2)<.001,"annotations sticky notes bookmark hierarchy and progress reopen");
   string renamed=Path.Combine(folder,"改名.pdf");File.Copy(pdf,renamed);var alias=Load(renamed);Check(alias.TargetPath==s.SidecarPath && !alias.NeedsSave,"same hash renamed PDF reuses one data file");
   string portableFolder=Path.Combine(folder,"portable");Directory.CreateDirectory(portableFolder);string portable=Path.Combine(portableFolder,"报告.pdfdata");DocumentDataRepository.SaveAs(s,portable,false);File.Copy(pdf,Path.Combine(portableFolder,"报告.pdf"));string portableBytes=File.ReadAllText(portable);
   Root("pc2");var imported=Load(Path.Combine(portableFolder,"报告.pdf"));Check(imported.NeedsSave&&imported.Record.annotations[1].subtitle=="附加说明","PDF plus same-name data load on another data root");DocumentDataRepository.Save(Session(imported,Path.Combine(portableFolder,"报告.pdf")));Check(File.ReadAllText(portable)==portableBytes,"portable source preserved during import");
   DocumentDataRepository.DataRootOverride=firstRoot;
   string changed=Path.Combine(folder,"changed.pdf");File.Copy(pdf,changed);DocumentDataRepository.RegisterLocation(changed,hash,s.SidecarPath);File.AppendAllText(changed,"\n% version 2\n");
   var none=Load(changed,(a,b)=>DialogResult.Ignore);Check(none.ReadOnly&&!none.NeedsSave&&none.Record.annotations.Count==0,"mismatch no-load is temporary and never auto writes");Throws(()=>DocumentDataRepository.Save(Session(none,changed)),"temporary no-load session cannot overwrite data");
   Check(Load(changed,(a,b)=>DialogResult.Cancel)==null,"mismatch cancel aborts");
   var fresh=Load(changed);Check(fresh.Data.versions.Count==2&&fresh.Record.annotations.Count==0,"mismatch new record keeps old data");
   var reuse=Load(changed,(a,b)=>a=="PDF 内容已变化"?DialogResult.No:DialogResult.Yes);Check(reuse.Record.annotations.Count==2&&reuse.Record.bookmarks.Count==2&&reuse.Data.versions[0].sourceHash==hash,"mismatch reuse copies old annotations notes and bookmarks");
   Check(File.ReadAllText(s.SidecarPath)==initial,"all mismatch decisions leave original data untouched before save");
   int revision=s.Data.revision;s.Record.annotations[0].text="保存失败时留在内存";
   using(var locked=new FileStream(s.SidecarPath,FileMode.Open,FileAccess.Read,FileShare.Read))Throws(()=>DocumentDataRepository.Save(s),"locked destination fails atomic replacement");
   Check(s.Data.revision==revision && File.ReadAllText(s.SidecarPath)==initial && s.Annotations[0].text=="保存失败时留在内存" && File.Exists(s.SidecarPath+".tmp"),"failed save keeps memory revision source and complete recovery snapshot");
   int recoverPrompts=0;var recovery=Load(null,(a,b)=>{recoverPrompts++;return DialogResult.Yes;});Check(recoverPrompts==1&&recovery.NeedsSave&&recovery.Record.annotations[0].text=="保存失败时留在内存","crash snapshot prompts and restores newer valid content");
   var recovered=Session(recovery);DocumentDataRepository.Save(recovered);Check(Protected().Any(p=>File.ReadAllText(p).Contains("保存失败时留在内存")),"recovered snapshot protected before replacement");
   var stale=Session(Load());var current=Session(Load());current.Annotations[0].text="其他窗口的新内容";DocumentDataRepository.Save(current);string currentBytes=File.ReadAllText(current.SidecarPath);stale.Annotations[0].text="过期内容";Throws(()=>DocumentDataRepository.Save(stale),"stale writer refuses external changes");Check(File.ReadAllText(current.SidecarPath)==currentBytes,"stale writer leaves valid newer data intact");
   using(var gate=new FileStream(current.SidecarPath+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))Throws(()=>DocumentDataRepository.Save(current),"concurrent save lock is respected");File.Delete(current.SidecarPath+".lock");
   string backup=File.ReadAllText(current.SidecarPath+".bak");File.WriteAllText(current.SidecarPath,currentBytes.Replace("其他窗口的新内容","校验已损坏内容"));File.WriteAllText(current.SidecarPath+".tmp","{partial");
   var fromBackup=Load();Check(fromBackup!=null&&fromBackup.NeedsSave && File.ReadAllText(current.SidecarPath+".bak")==backup,"checksum corruption recovers backup without overwriting valid backup");
   DocumentDataRepository.Save(Session(fromBackup));Check(Protected().Any(p=>File.ReadAllText(p).Contains("校验已损坏内容")),"damaged original archived before explicit recovered save");
   string valid=File.ReadAllText(current.SidecarPath);File.WriteAllText(current.SidecarPath,valid.Replace("\"formatVersion\":6","\"formatVersion\":99"));int unsupported=0;Check(Load(null,(a,b)=>{unsupported++;return DialogResult.Yes;})==null && unsupported==1,"future format stops instead of falling back and overwriting");File.WriteAllText(current.SidecarPath,valid);
   var migrateData=Json.Deserialize<SidecarFile>(valid);migrateData.formatVersion=5;migrateData.contentChecksum=null;string legacy=Json.Serialize(migrateData);Root("upgrade");string migrationPath=DocumentDataRepository.GetDataPath(hash,pdf);Directory.CreateDirectory(Path.GetDirectoryName(migrationPath));File.WriteAllText(migrationPath,legacy);
   var migration=Load();Check(migration.NeedsSave && migration.Data.formatVersion==6&&File.ReadAllText(migrationPath)==legacy && Protected().Any(p=>File.ReadAllText(p)==legacy),"old format backed up before migration; original bytes unchanged until save");DocumentDataRepository.Save(Session(migration));Check(File.ReadAllText(migrationPath)!=legacy&&Protected().Any(p=>File.ReadAllText(p)==legacy),"upgrade commits atomically while retaining original backup");
   Root("bad-upgrade");string bad=Json.Serialize(migrateData).Replace("\"type\":\"text_note\"","\"type\":\"unknown_type\"");string badPath=DocumentDataRepository.GetDataPath(hash,pdf);Directory.CreateDirectory(Path.GetDirectoryName(badPath));File.WriteAllText(badPath,bad);Check(Load()==null&&File.ReadAllText(badPath)==bad&&Protected().Any(p=>File.ReadAllText(p)==bad),"failed semantic migration never overwrites original");
   Root("blocked-backup");string blocked=DocumentDataRepository.GetDataPath(hash,pdf);Directory.CreateDirectory(Path.GetDirectoryName(blocked));File.WriteAllText(blocked,legacy);File.WriteAllText(Path.Combine(DocumentDataRepository.DataRoot,"_backups"),"not a directory");Check(Load()==null&&File.ReadAllText(blocked)==legacy,"backup creation failure aborts upgrade safely");
   DocumentDataRepository.DataRootOverride=firstRoot;var switched=Session(Load());string alternate=Path.Combine(folder,"alternate.pdfdata");DocumentDataRepository.SaveAs(switched,alternate,true);Check(Load().TargetPath==alternate,"alternate writable data location persists across reopen");
   Throws(()=>DocumentDataRepository.SaveAs(switched,pdf,true),"data save-as cannot overwrite original PDF");Check(DocumentDataRepository.CalculateSha256(pdf)==hash,"all persistence operations preserve original PDF bytes");
   Console.WriteLine("PERSISTENCE_ACCEPTANCE_PASS "+folder);return 0;
  }
  catch(Exception e){Console.WriteLine(e);return 1;}
 }
}
