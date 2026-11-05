using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed class TabDetachEventArgs : EventArgs
{
    public TabPage Tab;
    public Point ScreenLocation;
}

internal sealed class DocumentTabControl : TabControl
{
    private TabPage pressedTab;
    private TabPage closeTab;
    private Point pressPoint;
    private bool dragging;
    public bool Reordering { get; private set; }
    public event EventHandler<TabPage> CloseRequested;
    public event EventHandler<TabDetachEventArgs> DetachRequested;

    public DocumentTabControl()
    {
        Dock = DockStyle.Fill;
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(210, 32);
        Padding = new Point(12, 6);
        ShowToolTips = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }

    internal Rectangle CloseBounds(int index)
    {
        Rectangle r = GetTabRect(index);
        return new Rectangle(r.Right - 26, r.Top + (r.Height - 20) / 2, 20, 20);
    }

    private TabPage TabAt(Point point)
    {
        for (int i = 0; i < TabCount; i++) if (GetTabRect(i).Contains(point)) return TabPages[i];
        return null;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= TabCount) return;
        Rectangle r = GetTabRect(e.Index);
        bool selected = SelectedIndex == e.Index;
        using (var brush = new SolidBrush(selected ? Color.White : Color.FromArgb(232, 237, 243))) e.Graphics.FillRectangle(brush, r);
        Rectangle label = new Rectangle(r.Left + 12, r.Top + 2, r.Width - 44, r.Height - 4);
        TextRenderer.DrawText(e.Graphics, TabPages[e.Index].Text, Font, label, Color.FromArgb(41, 51, 65),
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        Rectangle close = CloseBounds(e.Index);
        using (var pen = new Pen(Color.FromArgb(89, 102, 119), 1.5f))
        {
            e.Graphics.DrawLine(pen, close.Left + 6, close.Top + 6, close.Right - 6, close.Bottom - 6);
            e.Graphics.DrawLine(pen, close.Left + 6, close.Bottom - 6, close.Right - 6, close.Top + 6);
        }
        if (selected) using (var pen = new Pen(Color.FromArgb(58, 118, 210), 3)) e.Graphics.DrawLine(pen, r.Left + 2, r.Bottom - 2, r.Right - 2, r.Bottom - 2);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        pressedTab = TabAt(e.Location);
        closeTab = null;
        pressPoint = e.Location;
        dragging = false;
        if (pressedTab == null) return;
        if (CloseBounds(TabPages.IndexOf(pressedTab)).Contains(e.Location)) closeTab = pressedTab;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (pressedTab == null || closeTab != null || (e.Button & MouseButtons.Left) == 0) return;
        Size threshold = SystemInformation.DragSize;
        if (!dragging && (Math.Abs(e.X - pressPoint.X) > threshold.Width || Math.Abs(e.Y - pressPoint.Y) > threshold.Height)) dragging = true;
        if (!dragging) return;
        Cursor = Cursors.SizeAll;
        TabPage over = TabAt(e.Location);
        if (over != null && over != pressedTab) MoveTab(pressedTab, TabPages.IndexOf(over));
    }

    internal void MoveTab(TabPage tab, int index)
    {
        if (!TabPages.Contains(tab) || index < 0 || index >= TabCount || TabPages.IndexOf(tab) == index) return;
        TabPage selected = SelectedTab;
        Reordering = true;
        try { TabPages.Remove(tab); TabPages.Insert(index, tab); SelectedTab = selected; }
        finally { Reordering = false; }
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        ReleasePointer(e);
    }

    // Native TabControl may release capture before raising MouseUp. Finish the
    // drop while our captured tab is still known, then let WinForms dispatch.
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0203) // WM_LBUTTONDBLCLK on a native tab header
        {
            long point = m.LParam.ToInt64();
            Point location = new Point((short)(point & 0xffff), (short)((point >> 16) & 0xffff));
            if (CloseDoubleClickedTab(location)) { m.Result = IntPtr.Zero; return; }
        }
        if (m.Msg == 0x0202 && pressedTab != null) // WM_LBUTTONUP
        {
            long point = m.LParam.ToInt64();
            ReleasePointer(new MouseEventArgs(MouseButtons.Left, 1, (short)(point & 0xffff), (short)((point >> 16) & 0xffff), 0));
        }
        base.WndProc(ref m);
    }

    internal void CancelDrag()
    {
        pressedTab = closeTab = null;
        dragging = false;
        Capture = false;
        Cursor = Cursors.Default;
    }

    private void ReleasePointer(MouseEventArgs e)
    {
        TabPage tab = pressedTab;
        TabPage close = closeTab;
        bool moved = dragging;
        pressedTab = closeTab = null;
        dragging = false;
        Capture = false;
        Cursor = Cursors.Default;
        if (e.Button != MouseButtons.Left || tab == null || !TabPages.Contains(tab)) return;
        if (close == tab && CloseBounds(TabPages.IndexOf(tab)).Contains(e.Location)) CloseRequested?.Invoke(this, tab);
        else if (moved) CompleteDrop(tab, PointToScreen(e.Location));
    }

    internal void CompleteDrop(TabPage tab, Point screenLocation)
    {
        Form form = FindForm();
        if (TabPages.Contains(tab) && form != null && !form.Bounds.Contains(screenLocation))
            DetachRequested?.Invoke(this, new TabDetachEventArgs { Tab = tab, ScreenLocation = screenLocation });
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left) CloseDoubleClickedTab(e.Location);
    }

    private bool CloseDoubleClickedTab(Point location)
    {
        TabPage tab = TabAt(location);
        if (tab == null || CloseBounds(TabPages.IndexOf(tab)).Contains(location)) return false;
        CancelDrag();
        CloseRequested?.Invoke(this, tab);
        return true;
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) { pressedTab = closeTab = null; dragging = false; Cursor = Cursors.Default; }
    }
}
