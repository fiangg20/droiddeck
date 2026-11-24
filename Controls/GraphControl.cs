using System.Windows;
using System.Windows.Media;

namespace Droideck.Controls;

public class GraphControl : FrameworkElement
{
    readonly List<double> vals = new();

    public int Capacity { get; set; } = 60;
    public double MaxValue { get; set; } = 100;
    public Brush Stroke { get; set; } = new SolidColorBrush(Color.FromRgb(53, 116, 240));

    static readonly Pen GridPen = MakeGridPen();

    static Pen MakeGridPen()
    {
        var b = new SolidColorBrush(Color.FromRgb(57, 59, 64));
        b.Freeze();
        var p = new Pen(b, 1);
        p.Freeze();
        return p;
    }

    public void Add(double v)
    {
        vals.Add(v);
        while (vals.Count > Capacity) vals.RemoveAt(0);
        InvalidateVisual();
    }

    public void Clear()
    {
        vals.Clear();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 4 || h < 4) return;

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        for (int i = 1; i < 4; i++)
        {
            double y = h * i / 4.0;
            dc.DrawLine(GridPen, new Point(0, y), new Point(w, y));
        }
        if (vals.Count < 2) return;

        double max = MaxValue > 0 ? MaxValue : Math.Max(1, vals.Max() * 1.2);
        double step = w / (Capacity - 1);
        int n = vals.Count;

        Point P(int i) => new Point(w - (n - 1 - i) * step, h - Math.Clamp(vals[i] / max, 0, 1) * (h - 4) - 2);

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(P(0), false, false);
            for (int i = 1; i < n; i++) ctx.LineTo(P(i), true, false);
        }
        line.Freeze();

        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(P(0).X, h), true, true);
            for (int i = 0; i < n; i++) ctx.LineTo(P(i), true, false);
            ctx.LineTo(new Point(w, h), true, false);
        }
        area.Freeze();

        var fill = Stroke.Clone();
        fill.Opacity = 0.18;
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, new Pen(Stroke, 2), line);
    }
}
