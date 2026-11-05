using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal sealed partial class PdfReaderForm
{
    private bool sessionResourcesDisposed;
    private void ToggleNavigation(int tab)
    {
        if (!navigationSplit.Panel2Collapsed && navigationTabs.SelectedIndex == tab) SetNavigationVisible(false);
        else ShowNavigation(tab);
    }
    private void ShowNavigation(int tab)
    {
        navigationTabs.SelectedIndex = tab;
        SetNavigationVisible(true);
    }
    private void SetNavigationVisible(bool visible)
    {
        navigationSplit.Panel2Collapsed = !visible;
        UpdateNavigationButtons();
        PositionReadingTools();
    }
    private void UpdateNavigationButtons()
    {
        bool shown = !navigationSplit.Panel2Collapsed;
        directoryButton.Checked = shown && navigationTabs.SelectedIndex == 0;
        bookmarksButton.Checked = shown && navigationTabs.SelectedIndex == 1;
        thumbnailsButton.Checked = shown && navigationTabs.SelectedIndex == 2;
    }
    private void RenderNavigationThumbnail()
    {
        if (movingSession || navigationSplit.Panel2Collapsed || !Visible || WindowState == FormWindowState.Minimized) return;
        var cache = navigationSession?.Thumbnails;
        if (cache == null) return;
        if (navigationTabs.SelectedIndex == 2)
        {
            foreach (int page in thumbnailView.VisiblePages()) if (cache.NeedsRender(page))
            { cache.Render(page); thumbnailView.Invalidate(); return; }
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            thumbnailTimer.Dispose();
            rightTools?.Dispose();
            DisposeAnnotationImages();
            DisposeSessions();
        }
        base.Dispose(disposing);
    }
}
