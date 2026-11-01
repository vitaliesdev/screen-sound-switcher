using MonitorTv.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MonitorTv.Core.Monitors;

namespace MonitorTv.App;

/// <summary>Drag-to-arrange view of the monitors in a layout, like Windows' display settings.</summary>
public sealed class ArrangementView : Border
{
    private sealed class Box(LayoutEditor.MonitorBox info)
    {
        public LayoutEditor.MonitorBox Info { get; } = info;
        public double X { get; set; } = info.X;
        public double Y { get; set; } = info.Y;
        public int W => Info.Width;
        public int H => Info.Height;
        public Border? Visual { get; set; }
    }

    private const double CanvasPadding = 18;

    private readonly Canvas _canvas = new() { ClipToBounds = true };
    private readonly List<Box> _boxes = [];
    private DisplayConfig.Layout? _layout;
    private Box? _primary;
    private Box? _dragging;
    private Point _dragStart;
    private double _startX, _startY;
    // The view stays fixed while a monitor is dragged; recomputing it from the moving box makes the box slide away from the pointer.
    private (double Scale, double OffsetX, double OffsetY) _dragView;
    private System.Windows.Shapes.Rectangle? _ghost;

    public event Action? Changed;

    public ArrangementView()
    {
        Height = 220;
        CornerRadius = new CornerRadius(16);
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "ControlFillColorDefaultBrush");
        SetResourceReference(BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        Child = _canvas;
        SizeChanged += (_, _) => Render();
    }

    public bool HasMonitors => _boxes.Count > 0;

    public void Load(DisplayConfig.Layout? layout)
    {
        _layout = layout;
        _boxes.Clear();
        _primary = null;
        if (layout != null)
        {
            _boxes.AddRange(LayoutEditor.Read(layout).Select(b => new Box(b)));
            _primary = _boxes.FirstOrDefault(b => b.X == 0 && b.Y == 0) ?? _boxes.FirstOrDefault();
        }
        Render();
    }

    /// <summary>The loaded layout with the positions currently shown (primary monitor at 0,0).</summary>
    public DisplayConfig.Layout? GetLayout()
    {
        if (_layout == null || _primary == null) return _layout;
        var positions = _boxes.ToDictionary(b => b.Info.ModeIndex, b => ((int)Math.Round(b.X - _primary.X), (int)Math.Round(b.Y - _primary.Y)));
        return LayoutEditor.WithPositions(_layout, positions);
    }

    private (double Scale, double OffsetX, double OffsetY) View()
    {
        var minX = _boxes.Min(b => b.X);
        var minY = _boxes.Min(b => b.Y);
        var maxX = _boxes.Max(b => b.X + b.W);
        var maxY = _boxes.Max(b => b.Y + b.H);
        // Leave room around the monitors so one can be dragged to any side of the others.
        var spanW = (maxX - minX) * 1.6;
        var spanH = (maxY - minY) * 1.6;
        var scale = Math.Min((ActualWidth - 2 * CanvasPadding) / spanW, (ActualHeight - 2 * CanvasPadding) / spanH);
        var offsetX = (ActualWidth - (maxX - minX) * scale) / 2 - minX * scale;
        var offsetY = (ActualHeight - (maxY - minY) * scale) / 2 - minY * scale;
        return (scale, offsetX, offsetY);
    }

    private void Render()
    {
        _canvas.Children.Clear();
        if (_boxes.Count == 0 || ActualWidth < 50 || ActualHeight < 50) return;

        var (scale, ox, oy) = View();
        foreach (var box in _boxes)
        {
            var isPrimary = box == _primary;
            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            label.Children.Add(new TextBlock { Text = box.Info.Name, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            label.Children.Add(new TextBlock { Text = $"{box.W}×{box.H}{(isPrimary ? " · " + Loc.T("principal") : "")}", FontSize = 11, Opacity = 0.8, TextAlignment = TextAlignment.Center });

            var visual = new Border
            {
                Width = Math.Max(20, box.W * scale - 2),
                Height = Math.Max(20, box.H * scale - 2),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1.5),
                Padding = new Thickness(4),
                Cursor = Cursors.SizeAll,
                Child = label,
                Tag = box,
            };
            if (isPrimary)
            {
                visual.Background = (Brush)Application.Current.FindResource("TileBlue");
                visual.BorderBrush = Brushes.Transparent;
            }
            else
            {
                visual.SetResourceReference(BackgroundProperty, "ControlFillColorSecondaryBrush");
                visual.SetResourceReference(BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            }
            foreach (var t in label.Children.OfType<TextBlock>())
            {
                if (isPrimary) t.Foreground = Brushes.White;
                else t.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            }

            visual.Focusable = true;
            visual.FocusVisualStyle = null;
            System.Windows.Automation.AutomationProperties.SetName(visual,
                Loc.T("{0}, {1} por {2}", box.Info.Name, box.W, box.H) + (isPrimary ? ", " + Loc.T("pantalla principal") : ""));
            visual.KeyDown += Box_KeyDown;
            visual.GotKeyboardFocus += (_, _) => { visual.BorderThickness = new Thickness(3); visual.SetResourceReference(BorderBrushProperty, "AccentFillColorDefaultBrush"); };
            visual.LostKeyboardFocus += (_, _) => { visual.BorderThickness = new Thickness(1.5); if (!isPrimary) visual.SetResourceReference(BorderBrushProperty, "ControlStrokeColorDefaultBrush"); else visual.BorderBrush = Brushes.Transparent; };
            visual.MouseLeftButtonDown += Box_MouseDown;
            visual.MouseMove += Box_MouseMove;
            visual.MouseLeftButtonUp += Box_MouseUp;
            visual.ContextMenu = BuildMenu(box);

            Canvas.SetLeft(visual, box.X * scale + ox + 1);
            Canvas.SetTop(visual, box.Y * scale + oy + 1);
            box.Visual = visual;
            _canvas.Children.Add(visual);
        }
    }

    private ContextMenu BuildMenu(Box box)
    {
        var item = new MenuItem { Header = Loc.T("Establecer como principal"), IsEnabled = box != _primary };
        item.Click += (_, _) =>
        {
            _primary = box;
            Render();
            Changed?.Invoke();
        };
        return new ContextMenu { Items = { item } };
    }

    /// <summary>Arrow keys move the selected monitor to the next position along that direction; P makes it the primary one.</summary>
    private void Box_KeyDown(object sender, KeyEventArgs e)
    {
        var box = (Box)((Border)sender).Tag;

        if (e.Key == Key.P)
        {
            e.Handled = true;
            _primary = box;
            Render();
            box.Visual?.Focus();
            Changed?.Invoke();
            return;
        }

        const double step = 200; // larger than the snap distance, so every press lands on a new position
        var (dx, dy) = e.Key switch
        {
            Key.Left => (-step, 0.0),
            Key.Right => (step, 0.0),
            Key.Up => (0.0, -step),
            Key.Down => (0.0, step),
            _ => (0.0, 0.0),
        };
        if (dx == 0 && dy == 0) return;

        e.Handled = true;
        _startX = box.X;
        _startY = box.Y;
        box.X += dx;
        box.Y += dy;
        SnapToNeighbours(box);
        Heal();
        Render();
        box.Visual?.Focus();
        Changed?.Invoke();
    }

    private void Box_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var visual = (Border)sender;
        _dragging = (Box)visual.Tag;
        _dragStart = e.GetPosition(_canvas);
        _startX = _dragging.X;
        _startY = _dragging.Y;
        _dragView = View();
        visual.CaptureMouse();
        Panel.SetZIndex(visual, 10);
    }

    private void Box_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging == null || e.LeftButton != MouseButtonState.Pressed) return;
        var (scale, ox, oy) = _dragView;
        var p = e.GetPosition(_canvas);
        _dragging.X = _startX + (p.X - _dragStart.X) / scale;
        _dragging.Y = _startY + (p.Y - _dragStart.Y) / scale;
        Canvas.SetLeft(_dragging.Visual!, _dragging.X * scale + ox + 1);
        Canvas.SetTop(_dragging.Visual!, _dragging.Y * scale + oy + 1);
        ShowGhost(_dragging, scale, ox, oy);
    }

    /// <summary>An outline where the dragged monitor will land when released.</summary>
    private void ShowGhost(Box box, double scale, double ox, double oy)
    {
        var landing = FindSnap(box, box.X, box.Y, _boxes.Where(b => b != box).ToList());
        if (landing is not { } at)
        {
            if (_ghost != null) _ghost.Visibility = Visibility.Collapsed;
            return;
        }
        if (_ghost == null)
        {
            _ghost = new System.Windows.Shapes.Rectangle { RadiusX = 4, RadiusY = 4, StrokeThickness = 2, StrokeDashArray = [4, 3], IsHitTestVisible = false };
            _ghost.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentFillColorDefaultBrush");
        }
        if (!_canvas.Children.Contains(_ghost)) _canvas.Children.Add(_ghost);
        _ghost.Visibility = Visibility.Visible;
        _ghost.Width = Math.Max(20, box.W * scale - 2);
        _ghost.Height = Math.Max(20, box.H * scale - 2);
        Canvas.SetLeft(_ghost, at.X * scale + ox + 1);
        Canvas.SetTop(_ghost, at.Y * scale + oy + 1);
    }

    private void Box_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging == null) return;
        ((Border)sender).ReleaseMouseCapture();
        SnapToNeighbours(_dragging);
        _dragging = null;
        Heal();
        Render();
        Changed?.Invoke();
    }

    /// <summary>Windows needs monitors to touch edge to edge: put the dropped monitor against the closest side of another one.</summary>
    private void SnapToNeighbours(Box box)
    {
        if (FindSnap(box, box.X, box.Y, _boxes.Where(b => b != box).ToList()) is { } at) { box.X = at.X; box.Y = at.Y; }
        else { box.X = _startX; box.Y = _startY; }
    }

    private static bool Overlaps(Box box, double x, double y, Box o) =>
        x < o.X + o.W && x + box.W > o.X && y < o.Y + o.H && y + box.H > o.Y;

    /// <summary>The position closest to (px, py) where <paramref name="box"/> touches one of <paramref name="targets"/> edge to edge without overlapping any monitor.</summary>
    private (double X, double Y)? FindSnap(Box box, double px, double py, List<Box> targets)
    {
        var obstacles = _boxes.Where(b => b != box).ToList();
        (double X, double Y)? best = null;
        var bestDistance = double.MaxValue;

        void Consider(double x, double y)
        {
            var d = Math.Pow(x - px, 2) + Math.Pow(y - py, 2);
            if (d >= bestDistance) return;
            if (obstacles.Any(o => Overlaps(box, x, y, o))) return;
            bestDistance = d;
            best = (x, y);
        }

        foreach (var o in targets)
        {
            // Slide along the touching edge, snapping to start / centre / end alignment when close.
            foreach (var y in AlongEdge(py, box.H, o.Y, o.H))
            {
                Consider(o.X + o.W, y);   // right of o
                Consider(o.X - box.W, y); // left of o
            }
            foreach (var x in AlongEdge(px, box.W, o.X, o.W))
            {
                Consider(x, o.Y + o.H);   // below o
                Consider(x, o.Y - box.H); // above o
            }
        }
        return best;
    }

    private static bool Touches(Box a, Box b)
    {
        var overlapX = Math.Min(a.X + a.W, b.X + b.W) - Math.Max(a.X, b.X);
        var overlapY = Math.Min(a.Y + a.H, b.Y + b.H) - Math.Max(a.Y, b.Y);
        return (Math.Abs(a.X + a.W - b.X) < 0.5 || Math.Abs(b.X + b.W - a.X) < 0.5) && overlapY > 0
            || (Math.Abs(a.Y + a.H - b.Y) < 0.5 || Math.Abs(b.Y + b.H - a.Y) < 0.5) && overlapX > 0;
    }

    /// <summary>
    /// Moving a monitor can leave another one touching nothing, which Windows does not accept. Every monitor that is not
    /// connected to the primary one through touching edges is attached to the nearest side of the connected group.
    /// </summary>
    public void Heal()
    {
        if (_boxes.Count < 2) return;
        var connected = new List<Box> { _primary ?? _boxes[0] };
        var pending = _boxes.Where(b => b != connected[0]).ToList();

        while (pending.Count > 0)
        {
            var touching = pending.Where(b => connected.Any(c => Touches(b, c))).ToList();
            if (touching.Count > 0)
            {
                connected.AddRange(touching);
                pending.RemoveAll(touching.Contains);
                continue;
            }

            Box? pick = null;
            (double X, double Y) at = default;
            var bestDistance = double.MaxValue;
            foreach (var b in pending)
            {
                if (FindSnap(b, b.X, b.Y, connected) is not { } s) continue;
                var d = Math.Pow(s.X - b.X, 2) + Math.Pow(s.Y - b.Y, 2);
                if (d < bestDistance) { bestDistance = d; pick = b; at = s; }
            }
            if (pick == null) return;
            pick.X = at.X;
            pick.Y = at.Y;
            connected.Add(pick);
            pending.Remove(pick);
        }
        Render();
    }

    private static IEnumerable<double> AlongEdge(double pos, double size, double otherPos, double otherSize)
    {
        // keep at least 1px of shared edge
        var min = otherPos - size + 1;
        var max = otherPos + otherSize - 1;
        var snaps = new[] { otherPos, otherPos + (otherSize - size) / 2, otherPos + otherSize - size };
        const double threshold = 160; // desktop pixels
        var nearest = snaps.MinBy(s => Math.Abs(pos - s));
        yield return Math.Abs(pos - nearest) < threshold ? Math.Clamp(nearest, min, max) : Math.Clamp(pos, min, max);
    }
}
