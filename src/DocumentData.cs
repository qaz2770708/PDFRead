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
    public int formatVersion { get; set; } = 6;
    public int revision { get; set; }
    public List<VersionRecord> versions { get; set; } = new List<VersionRecord>();
    public string lastSavedUtc { get; set; }
    public string contentChecksum { get; set; }
}

[Serializable]
internal sealed class VersionRecord
{
    public string sourceHash { get; set; }
    public string sourceName { get; set; }
    public int pageCount { get; set; }
    public int lastPage { get; set; }
    public double lastPageOffset { get; set; }
    public double lastPageHorizontal { get; set; }
    public float zoom { get; set; } = 0.82f;
    public List<AreaHighlight> annotations { get; set; } = new List<AreaHighlight>();
    public List<UserBookmark> bookmarks { get; set; } = new List<UserBookmark>();
    public string updatedUtc { get; set; }
}

[Serializable]
internal sealed class UserBookmark
{
    public string id { get; set; }
    public string title { get; set; }
    public string parentId { get; set; }
    public int pageIndex { get; set; }
    public double pageOffset { get; set; }
    public double horizontalOffset { get; set; }
    public float zoom { get; set; }
    public string createdUtc { get; set; }
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
    public string modifiedUtc { get; set; }
    public string text { get; set; }
    public string subtitle { get; set; }
    public double opacity { get; set; } = .45;
    public float strokeWidth { get; set; } = 2;
    public float fontSize { get; set; } = 12;
    public string fontFamily { get; set; } = "Microsoft YaHei UI";
    public List<AnnotationPoint> points { get; set; } = new List<AnnotationPoint>();
    public List<AnnotationRegion> textRegions { get; set; } = new List<AnnotationRegion>();
}

internal sealed class AnnotationPoint
{
    public double x { get; set; }
    public double y { get; set; }
}
internal sealed class AnnotationRegion
{
    public double x { get; set; }
    public double y { get; set; }
    public double width { get; set; }
    public double height { get; set; }
    public List<AnnotationPoint> quad { get; set; } = new List<AnnotationPoint>();
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
    public PdfNavigationData Navigation;
    public DocumentThumbnails Thumbnails;
    public EventHandler AnnotationHandler;
    public EventHandler DraftHandler;
    public EventHandler SelectionHandler;
    public EventHandler<AnnotationEditEventArgs> EditHandler;
    public PdfTextRegions TextSource;
    public EventHandler<int> PageHandler;
    public EventHandler<ReadingPositionEventArgs> PositionHandler;
    public EventHandler<NavigationTarget> LinkHandler;
    public bool DataReadOnly;
    public bool DataBaselineCaptured;
    public string ExpectedDataHash;
    public bool SaveFailed;
    public bool Saving;
    public string IndexWarning;
    public bool Dirty;
    public bool Restoring;
    public List<string> KnownPaths = new List<string>();
    public bool ModifiedByRecoveryOrMigration;
}

internal sealed class SidecarLoadResult
{
    public SidecarFile Data;
    public VersionRecord Record;
    public bool NeedsSave;
    public string TargetPath;
    public bool ReadOnly;
    public string ExpectedDataHash;
}
