using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Codomon.Desktop.Models;
using Codomon.Desktop.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Codomon.Desktop.Controls;

/// <summary>
/// Renders a day-based aggregated timeline with one row per visible System.
/// Bars represent bucketed event counts; a vertical cursor tracks log replay progress.
/// </summary>
public class TimelineControl : Control
{
    private readonly TimelineViewModel _vm;

    // ── Layout constants ──────────────────────────────────────────────────────

    private const double LabelWidth   = 130.0; // left column for system names
    private const double RowHeight    = 36.0;  // height of each system row
    private const double RowSpacing   = 6.0;   // gap between rows
    private const double AxisHeight   = 20.0;  // top axis area
    private const double BarMaxHeight = 24.0;  // tallest bar in a row
    private const double Padding      = 8.0;   // outer horizontal padding (right side)

    // Total minutes in a day (timeline range 00:00 – 24:00).
    private const double DayMinutes = 24 * 60.0;

    // ── Brushes / pens ────────────────────────────────────────────────────────

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(14, 20, 30));
    private static readonly IBrush RowEvenBrush    = new SolidColorBrush(Color.FromArgb(30, 40, 80, 120));
    private static readonly IBrush RowOddBrush     = new SolidColorBrush(Color.FromArgb(20, 30, 60, 100));
    private static readonly IBrush BarBrush        = new SolidColorBrush(Color.FromRgb(0, 160, 200));
    private static readonly IBrush BarHoverBrush   = new SolidColorBrush(Color.FromRgb(0, 220, 255));
    private static readonly IBrush CursorBrush     = new SolidColorBrush(Color.FromRgb(255, 160, 40));
    private static readonly IBrush LabelBrush      = new SolidColorBrush(Color.FromRgb(170, 190, 210));
    private static readonly IBrush AxisLabelBrush  = new SolidColorBrush(Color.FromRgb(100, 130, 160));
    private static readonly IBrush GridLineBrush   = new SolidColorBrush(Color.FromArgb(40, 100, 160, 220));
    private static readonly IBrush EmptyBrush      = new SolidColorBrush(Color.FromRgb(80, 90, 100));
    private static readonly Pen    CursorPen       = new Pen(CursorBrush, 2);
    private static readonly Pen    GridLinePen     = new Pen(GridLineBrush, 1) { DashStyle = DashStyle.Dash };

    private static readonly Typeface LabelTypeface = new Typeface("Monospace");
    private const double LabelFontSize  = 11.0;
    private const double AxisFontSize   = 10.0;

    // ── Hover state ───────────────────────────────────────────────────────────

    private TimelineBucket? _hoveredBucket;

    // ── Selected bucket ───────────────────────────────────────────────────────

    /// <summary>
    /// Fired when the user clicks a bucket. The list contains the matching log-entry indices.
    /// </summary>
    public event Action<TimelineBucket>? BucketSelected;

    // ── Constructor ───────────────────────────────────────────────────────────

    public TimelineControl(TimelineViewModel vm)
    {
        _vm = vm;
        _vm.PropertyChanged += OnVmPropertyChanged;

        PointerMoved   += OnPointerMoved;
        PointerExited  += OnPointerExited;
        PointerPressed += OnPointerPressed;
    }

    // ── ViewModel reaction ────────────────────────────────────────────────────

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Any change to the VM invalidates the visual.
        InvalidateVisual();
        // Recompute the desired height so the parent ScrollViewer adapts.
        InvalidateMeasure();
    }

    // ── Measure ───────────────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        var systems = _vm.ActiveSystems;
        var rowCount = systems.Count > 0 ? systems.Count : 1;
        var height = AxisHeight + rowCount * (RowHeight + RowSpacing);
        return new Size(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, height);
    }

    // ── Render ────────────────────────────────────────────────────────────────

    public override void Render(DrawingContext ctx)
    {
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        // Background
        ctx.FillRectangle(BackgroundBrush, bounds);

        var systems = _vm.ActiveSystems;

        if (!_vm.HasTimestamps || systems.Count == 0)
        {
            DrawPlaceholder(ctx, bounds);
            return;
        }

        double chartLeft  = LabelWidth;
        double chartRight = bounds.Width - Padding;
        double chartWidth = Math.Max(1, chartRight - chartLeft);

        DrawAxisLabels(ctx, chartLeft, chartWidth, bounds);
        DrawGridLines(ctx, chartLeft, chartWidth, bounds);

        for (int i = 0; i < systems.Count; i++)
        {
            var sys = systems[i];
            double rowTop = AxisHeight + i * (RowHeight + RowSpacing);
            DrawRow(ctx, sys, i, rowTop, chartLeft, chartWidth);
        }

        DrawCursor(ctx, chartLeft, chartWidth, bounds);
    }

    // ── Drawing helpers ───────────────────────────────────────────────────────

    private void DrawPlaceholder(DrawingContext ctx, Rect bounds)
    {
        var msg = _vm.HasTimestamps
            ? "No visible Systems with activity."
            : "No timestamped entries loaded. Import a log file with timestamps to see the timeline.";

        var ft = new FormattedText(
            msg,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            LabelFontSize,
            EmptyBrush);

        ctx.DrawText(ft, new Point(
            (bounds.Width  - ft.Width)  / 2,
            (bounds.Height - ft.Height) / 2));
    }

    private void DrawAxisLabels(DrawingContext ctx, double chartLeft, double chartWidth, Rect bounds)
    {
        // Draw hour labels every 3 hours (00:00, 03:00, … 21:00, 24:00)
        for (int hour = 0; hour <= 24; hour += 3)
        {
            double x = chartLeft + (hour * 60.0 / DayMinutes) * chartWidth;
            var label = $"{hour:D2}:00";
            var ft = new FormattedText(
                label,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                AxisFontSize,
                AxisLabelBrush);

            // Centre-align the label on x, keep within chart.
            double lx = x - ft.Width / 2;
            ctx.DrawText(ft, new Point(lx, 2));
        }
    }

    private void DrawGridLines(DrawingContext ctx, double chartLeft, double chartWidth, Rect bounds)
    {
        for (int hour = 0; hour <= 24; hour += 3)
        {
            double x = chartLeft + (hour * 60.0 / DayMinutes) * chartWidth;
            ctx.DrawLine(GridLinePen,
                new Point(x, AxisHeight),
                new Point(x, bounds.Height));
        }
    }

    private void DrawRow(DrawingContext ctx, SystemBoxModel sys, int rowIndex, double rowTop,
                         double chartLeft, double chartWidth)
    {
        // Row background
        var rowBrush = rowIndex % 2 == 0 ? RowEvenBrush : RowOddBrush;
        ctx.FillRectangle(rowBrush, new Rect(0, rowTop, LabelWidth + chartWidth + Padding, RowHeight));

        // System label
        var ft = new FormattedText(
            sys.Name,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            LabelFontSize,
            LabelBrush);

        double labelY = rowTop + (RowHeight - ft.Height) / 2;
        // Clip label to label column.
        using (ctx.PushClip(new Rect(2, rowTop, LabelWidth - 6, RowHeight)))
            ctx.DrawText(ft, new Point(4, labelY));

        // Bars
        if (!_vm.BucketsBySystem.TryGetValue(sys.Id, out var buckets)) return;

        double bucketWidthPx = (TimelineViewModel.BucketSize.TotalMinutes / DayMinutes) * chartWidth;
        double maxCount      = Math.Max(1, _vm.MaxBucketCount);

        foreach (var bucket in buckets)
        {
            double startFrac = bucket.StartTime.TotalMinutes / DayMinutes;
            double bx = chartLeft + startFrac * chartWidth;

            double heightFrac = bucket.Count / maxCount;
            double barH = Math.Max(2, heightFrac * BarMaxHeight);
            double by   = rowTop + RowHeight - barH - 2; // pin to bottom of row

            var brush = ReferenceEquals(bucket, _hoveredBucket) ? BarHoverBrush : BarBrush;
            ctx.FillRectangle(brush,
                new Rect(bx + 1, by, Math.Max(2, bucketWidthPx - 2), barH));
        }
    }

    private void DrawCursor(DrawingContext ctx, double chartLeft, double chartWidth, Rect bounds)
    {
        var cursorTime = _vm.ReplayCursorTime;
        if (cursorTime == null) return;

        double frac = cursorTime.Value.TotalMinutes / DayMinutes;
        double x    = chartLeft + frac * chartWidth;

        ctx.DrawLine(CursorPen,
            new Point(x, AxisHeight),
            new Point(x, bounds.Height));

        // Small label above the axis.
        var label = cursorTime.Value.ToString(@"HH\:mm");
        var ft = new FormattedText(
            label,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            AxisFontSize,
            CursorBrush);

        ctx.DrawText(ft, new Point(x - ft.Width / 2, 2));
    }

    // ── Hit-test helpers ──────────────────────────────────────────────────────

    private TimelineBucket? HitTestBucket(Point pt)
    {
        var systems = _vm.ActiveSystems;
        if (!_vm.HasTimestamps || systems.Count == 0) return null;

        double chartLeft  = LabelWidth;
        double chartRight = Bounds.Width - Padding;
        double chartWidth = Math.Max(1, chartRight - chartLeft);

        if (pt.X < chartLeft || pt.X > chartRight) return null;

        for (int i = 0; i < systems.Count; i++)
        {
            double rowTop    = AxisHeight + i * (RowHeight + RowSpacing);
            double rowBottom = rowTop + RowHeight;
            if (pt.Y < rowTop || pt.Y > rowBottom) continue;

            var sys = systems[i];
            if (!_vm.BucketsBySystem.TryGetValue(sys.Id, out var buckets)) continue;

            double timeFrac  = (pt.X - chartLeft) / chartWidth;
            double clickMins = timeFrac * DayMinutes;
            var clickTime    = TimeSpan.FromMinutes(clickMins);

            foreach (var bucket in buckets)
            {
                if (clickTime >= bucket.StartTime && clickTime < bucket.EndTime)
                    return bucket;
            }
        }
        return null;
    }

    // ── Pointer handlers ──────────────────────────────────────────────────────

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pt  = e.GetPosition(this);
        var hit = HitTestBucket(pt);
        if (!ReferenceEquals(hit, _hoveredBucket))
        {
            _hoveredBucket = hit;
            Cursor = hit != null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            InvalidateVisual();
        }
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (_hoveredBucket != null)
        {
            _hoveredBucket = null;
            Cursor = Cursor.Default;
            InvalidateVisual();
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pt  = e.GetPosition(this);
        var hit = HitTestBucket(pt);
        if (hit != null)
            BucketSelected?.Invoke(hit);
    }
}
