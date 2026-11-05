using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

internal static class M2VerificationProbe
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string source = Path.GetFullPath(args[0]);
            string folder = Path.GetFullPath(Path.Combine("work","storage-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(folder);
            DocumentDataRepository.DataRootOverride=Path.Combine(folder,"data");
            string pdfPath=Path.Combine(folder,"continuous.pdf");File.Copy(source,pdfPath);
            string copyPath = Path.Combine(Path.GetDirectoryName(pdfPath), "multipage-copy.pdf");
            string legacyPath = Path.Combine(Path.GetDirectoryName(pdfPath), "legacy-copy.pdf");
            File.Copy(pdfPath, copyPath, true);File.AppendAllText(copyPath,"\n% distinct tab\n");
            File.Copy(pdfPath, legacyPath, true);
            string beforeHash = Hash(pdfPath);

            int pageCount;
            object document = WindowsPdfRenderer.Open(pdfPath, out pageCount);
            Require(pageCount == 3, "Expected a three-page PDF.");
            ContinuousPdfViewer viewer = new ContinuousPdfViewer { Size = new Size(900, 620) };
            viewer.CreateControl();
            viewer.Configure(document, pageCount, 0.82f, new System.Collections.Generic.List<AreaHighlight>());
            viewer.SetActive(true);
            viewer.RenderPagesNearViewport();

            Require(viewer.PageRectangles.Count == 3, "Viewer did not lay out all pages.");
            Require(viewer.ContentSize.Height > viewer.ClientSize.Height, "The document is not vertically scrollable.");
            for (int i = 0; i < viewer.PageRectangles.Count; i++)
            {
                Rectangle page = viewer.PageRectangles[i];
                int expectedX = Math.Max(30, (viewer.ContentSize.Width - page.Width) / 2);
                Require(Math.Abs(page.X - expectedX) <= 1, "Page " + (i + 1) + " is not horizontally centered.");
            }
            Rectangle first = viewer.PageRectangles[0];
            Rectangle second = viewer.PageRectangles[1];
            Require(second.Top > first.Bottom && second.Top - first.Bottom == 34, "Adjacent pages do not form a continuous vertical stack.");
            Bitmap screenshot = new Bitmap(viewer.ClientSize.Width, viewer.ClientSize.Height);
            viewer.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
            screenshot.Save(Path.Combine(Path.GetDirectoryName(pdfPath), "continuous-view.png"), System.Drawing.Imaging.ImageFormat.Png);
            viewer.GoToPage(0, 0.85);
            Application.DoEvents();
            using (Bitmap seam = new Bitmap(viewer.ClientSize.Width, viewer.ClientSize.Height))
            {
                viewer.DrawToBitmap(seam, new Rectangle(Point.Empty, seam.Size));
                seam.Save(Path.Combine(Path.GetDirectoryName(pdfPath), "continuous-seam.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            viewer.GoToPage(1, 0.25);
            Application.DoEvents();
            int readingPage;
            double readingOffset;
            viewer.CaptureReadingPosition(out readingPage, out readingOffset);
            Require(readingPage == 1 && Math.Abs(readingOffset - 0.25) < 0.03, "Continuous scroll did not preserve a page-relative position.");
            Console.WriteLine("CENTERED_PAGES=PASS");
            Console.WriteLine("CONTINUOUS_VERTICAL_LAYOUT=PASS");
            Console.WriteLine("PAGE_RELATIVE_SCROLL_POSITION=PASS");
            viewer.Dispose();
            WindowsPdfRenderer.DisposeDocument(document);

            VerifySidecarProgressAndRecovery(pdfPath, beforeHash);
            VerifyHashRecognition(pdfPath);
            DocumentDataRepository.DataRootOverride=Path.Combine(folder,"legacy-data");
            VerifyLegacyMigration(legacyPath, beforeHash);
            VerifyTabs(pdfPath, copyPath);
            Require(Hash(pdfPath) == beforeHash, "The source PDF changed during verification.");
            Console.WriteLine("SOURCE_PDF_UNCHANGED=PASS");
            Console.WriteLine("MULTI_TAB=PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL=" + ex.GetType().FullName + ": " + ex.Message);
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static void VerifySidecarProgressAndRecovery(string path, string hash)
    {
        string sidecar = DocumentDataRepository.GetDataPath(hash,path);
        CleanupSidecar(sidecar);
        SidecarLoadResult loaded = DocumentDataRepository.LoadForPdf(path, hash, 3, null);
        Require(loaded != null && loaded.NeedsSave, "A new PDF did not receive an initial data record.");
        loaded.Record.lastPage = 1;
        loaded.Record.lastPageOffset = 0.25;
        loaded.Record.zoom = 0.91f;
        loaded.Record.annotations.Add(new AreaHighlight
        {
            id = "probe-highlight", type = "area_highlight", pageIndex = 1,
            coordinateSpace = "normalized_page", x = 0.1, y = 0.2, width = 0.3, height = 0.05,
            color = "#FFFF66", createdUtc = DateTime.UtcNow.ToString("o")
        });
        DocumentSession session = NewSession(path, hash, loaded);
        DocumentDataRepository.Save(session);

        SidecarLoadResult reopened = DocumentDataRepository.LoadForPdf(path, hash, 3, null);
        Require(reopened.Record.lastPage == 1 && Math.Abs(reopened.Record.lastPageOffset - 0.25) < 0.001,
            "Reading page and offset did not survive reopening.");
        Require(Math.Abs(reopened.Record.zoom - 0.91f) < 0.001f && reopened.Record.annotations.Count == 1,
            "Per-document zoom or annotations did not survive reopening.");
        Console.WriteLine("SIDECAR_PROGRESS_AND_ANNOTATION_REOPEN=PASS");

        reopened.Data.revision++;
        reopened.Record.lastPage = 2;
        File.WriteAllText(sidecar + ".tmp", DocumentDataRepository.SerializeData(reopened.Data));
        SidecarLoadResult recovered = DocumentDataRepository.LoadForPdf(path, hash, 3, null,
            (caption, message) => caption == "恢复未保存数据" ? DialogResult.Yes : DialogResult.Yes);
        Require(recovered.NeedsSave && recovered.Record.lastPage == 2, "A newer complete temporary snapshot was not recovered.");
        DocumentDataRepository.Save(NewSession(path, hash, recovered));
        Console.WriteLine("NEWER_TEMP_RECOVERY=PASS");

        File.WriteAllText(sidecar, "not valid json");
        SidecarLoadResult fromBackup = DocumentDataRepository.LoadForPdf(path, hash, 3, null,
            (caption, message) => DialogResult.Yes);
        Require(fromBackup != null && fromBackup.NeedsSave, "The last good backup was not offered for recovery.");
        Require(File.Exists(sidecar + ".recovered.bak"), "Recovery did not preserve a copy of the selected backup.");
        Console.WriteLine("LAST_GOOD_BACKUP_RECOVERY=PASS");

        SidecarFile newerSnapshot = fromBackup.Data;
        newerSnapshot.revision += 10;
        newerSnapshot.versions.First(v => v.sourceHash == hash).lastPage = 0;
        File.WriteAllText(sidecar + ".tmp", DocumentDataRepository.SerializeData(newerSnapshot));
        SidecarLoadResult newest = DocumentDataRepository.LoadForPdf(path, hash, 3, null,
            (caption, message) => DialogResult.Yes);
        Require(newest.Record.lastPage == 0 && File.Exists(sidecar + ".recovered.tmp"),
            "When both recovery candidates existed, the newer temporary snapshot was not selected and preserved.");
        Console.WriteLine("NEWEST_VALID_RECOVERY_CANDIDATE=PASS");
        CleanupSidecar(sidecar);
    }

    private static void VerifyLegacyMigration(string path, string hash)
    {
        string sidecar = Path.ChangeExtension(path, ".pdfdata");
        CleanupSidecar(sidecar);
        string legacy = "{\"formatVersion\":1,\"revision\":4,\"versions\":[{\"sourceHash\":\"" + hash +
            "\",\"sourceName\":\"legacy-copy.pdf\",\"pageCount\":3,\"lastPage\":2,\"annotations\":[]}]}";
        File.WriteAllText(sidecar, legacy);
        SidecarLoadResult migrated = DocumentDataRepository.LoadForPdf(path, hash, 3, null);
        Require(migrated.NeedsSave && migrated.Data.formatVersion == 6, "Version 1 data was not migrated to version 6.");
        Require(Math.Abs(migrated.Record.zoom - 0.82f) < 0.001f && migrated.Record.lastPageOffset == 0,
            "Migration did not populate the new progress defaults.");
        DocumentDataRepository.Save(NewSession(path, hash, migrated));
        Console.WriteLine("SIDECAR_FORMAT_MIGRATION=PASS");
        CleanupSidecar(sidecar);
    }

    private static void VerifyHashRecognition(string sourcePath)
    {
        string versionPath = Path.Combine(Path.GetDirectoryName(sourcePath), "hash-version.pdf");
        string sidecar = DocumentDataRepository.GetDataPath(Hash(sourcePath),sourcePath);
        CleanupSidecar(sidecar);
        File.Copy(sourcePath, versionPath, true);
        string firstHash = Hash(versionPath);
        SidecarLoadResult first = DocumentDataRepository.LoadForPdf(versionPath, firstHash, 3, null);
        first.Record.annotations.Add(new AreaHighlight
        {
            id = "old-version-mark", type = "area_highlight", pageIndex = 0,
            coordinateSpace = "normalized_page", x = 0.1, y = 0.1, width = 0.2, height = 0.05,
            color = "#FFFF66", createdUtc = DateTime.UtcNow.ToString("o")
        });
        DocumentDataRepository.Save(NewSession(versionPath, firstHash, first));
        sidecar=DocumentDataRepository.GetDataPath(firstHash,versionPath);

        File.AppendAllText(versionPath, "\n% revision two\n");
        string secondHash = Hash(versionPath);
        Require(!String.Equals(firstHash, secondHash, StringComparison.OrdinalIgnoreCase), "Changing PDF bytes did not change its hash.");
        int prompts = 0;
        SidecarLoadResult second = DocumentDataRepository.LoadForPdf(versionPath, secondHash, 3, null,
            (caption, message) => { prompts++; return DialogResult.Yes; });
        Require(prompts == 1 && second.NeedsSave, "A changed PDF did not prompt to create a new version record.");
        Require(second.Record.sourceHash == secondHash && second.Record.annotations.Count == 0 && second.Data.versions.Count == 2,
            "The changed PDF version did not get an empty separate record while preserving the old version.");
        Require(File.Exists(sidecar), "The sidecar was not paired with the PDF by basename.");
        DocumentDataRepository.Save(NewSession(versionPath, secondHash, second));
        SidecarLoadResult reopened = DocumentDataRepository.LoadForPdf(versionPath, secondHash, 3, null,
            (caption, message) => throw new InvalidOperationException("Existing hash unexpectedly prompted."));
        Require(reopened.Record.sourceHash == secondHash && reopened.Data.versions.Count == 2,
            "Reopening the same PDF hash did not select its existing sidecar record.");
        Console.WriteLine("PDF_HASH_VERSION_RECOGNITION_AND_PAIRING=PASS");
        CleanupSidecar(sidecar);
        File.Delete(versionPath);
    }

    private static void VerifyTabs(string firstPath, string secondPath)
    {
        PdfReaderForm form = new PdfReaderForm(new string[0]);
        IntPtr handle = form.Handle;
        MethodInfo open = typeof(PdfReaderForm).GetMethod("OpenPdf", BindingFlags.Instance | BindingFlags.NonPublic);
        open.Invoke(form, new object[] { firstPath });
        open.Invoke(form, new object[] { secondPath });
        open.Invoke(form, new object[] { firstPath });
        Application.DoEvents();
        TabControl tabs = (TabControl)typeof(PdfReaderForm).GetField("documentTabs", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        Require(tabs.TabPages.Count == 2, "Multiple PDFs did not open as distinct tabs or same-path deduplication failed.");
        form.Dispose();
        Console.WriteLine("MULTIPLE_PDFS_IN_TABS=PASS");
    }

    private static DocumentSession NewSession(string path, string hash, SidecarLoadResult result)
    {
        return new DocumentSession
        {
            PdfPath = path,
            PdfHash = hash,
            SidecarPath = result.TargetPath,
            PageCount = 3,
            Data = result.Data,
            Record = result.Record,
            Annotations = result.Record.annotations,
            Dirty = true
        };
    }

    private static void CleanupSidecar(string sidecar)
    {
        foreach (string suffix in new[] { "", ".tmp", ".bak", ".recovered.bak", ".recovered.tmp" })
            if (File.Exists(sidecar + suffix)) File.Delete(sidecar + suffix);
    }

    private static string Hash(string path)
    {
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
