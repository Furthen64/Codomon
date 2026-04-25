using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Codomon.Desktop.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Codomon.Desktop.Controls;

public class MainCanvasControl : Control
{
    private readonly List<SystemBoxModel> _systems;
    private readonly List<ConnectionModel> _connections;

    private object? _selectedItem;
    private SystemBoxModel? _draggingSystem;
    private Point _dragOffset;

    public event Action<object?>? SelectionChanged;

    private static readonly IBrush SystemFill = new SolidColorBrush(Color.FromRgb(30, 58, 95));
    private static readonly IBrush SystemFillSelected = new SolidColorBrush(Color.FromRgb(0, 120, 215));
    private static readonly IBrush SystemStroke = new SolidColorBrush(Color.FromRgb(100, 160, 220));
    private static readonly IBrush ModuleFill = new SolidColorBrush(Color.FromRgb(50, 90, 130));
    private static readonly IBrush ModuleFillSelected = new SolidColorBrush(Color.FromRgb(0, 180, 100));
    private static readonly IBrush ModuleStroke = new SolidColorBrush(Color.FromRgb(100, 200, 160));
    private static readonly IBrush ConnectionBrush = new SolidColorBrush(Color.FromRgb(180, 180, 60));
    private static readonly IBrush LabelBrush = Brushes.White;
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(20, 25, 35));

    private static readonly Pen SystemPen = new Pen(SystemStroke, 2);
    private static readonly Pen SystemPenSelected = new Pen(SystemFillSelected, 3);
    private static readonly Pen ModulePen = new Pen(ModuleStroke, 1.5);
    private static readonly Pen ModulePenSelected = new Pen(ModuleFillSelected, 2);
    private static readonly Pen ConnectionPen = new Pen(ConnectionBrush, 1.5) { DashStyle = DashStyle.Dash };

    public MainCanvasControl(List<SystemBoxModel> systems, List<ConnectionModel> connections)
    {
        _systems = systems;
        _connections = connections;

        Focusable = true;
        ClipToBounds = true;

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
    }

    public object? SelectedItem => _selectedItem;

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(BackgroundBrush, new Rect(Bounds.Size));

        foreach (var conn in _connections)
        {
            DrawConnection(context, conn);
        }

        foreach (var sys in _systems)
        {
            DrawSystem(context, sys);
        }
    }

    private void DrawSystem(DrawingContext context, SystemBoxModel sys)
    {
        bool isSelected = _selectedItem == sys;
        var fill = isSelected ? SystemFillSelected : SystemFill;
        var pen = isSelected ? SystemPenSelected : SystemPen;

        context.FillRectangle(fill, sys.Bounds);
        context.DrawRectangle(pen, sys.Bounds);

        var tf = new FormattedText(
            sys.Name,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            14,
            LabelBrush);
        context.DrawText(tf, new Point(sys.Bounds.X + 8, sys.Bounds.Y + 8));

        foreach (var mod in sys.Modules)
        {
            bool modSelected = _selectedItem == mod;
            var mFill = modSelected ? ModuleFillSelected : ModuleFill;
            var mPen = modSelected ? ModulePenSelected : ModulePen;

            context.FillRectangle(mFill, mod.Bounds);
            context.DrawRectangle(mPen, mod.Bounds);

            var mtf = new FormattedText(
                mod.Name,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                11,
                LabelBrush);
            context.DrawText(mtf, new Point(mod.Bounds.X + 4, mod.Bounds.Y + 4));
        }
    }

    private void DrawConnection(DrawingContext context, ConnectionModel conn)
    {
        var fromSys = _systems.FirstOrDefault(s => s.Id == conn.FromId);
        var toSys = _systems.FirstOrDefault(s => s.Id == conn.ToId);
        if (fromSys == null || toSys == null) return;

        var fromCenter = new Point(fromSys.Bounds.X + fromSys.Bounds.Width / 2,
                                   fromSys.Bounds.Y + fromSys.Bounds.Height / 2);
        var toCenter = new Point(toSys.Bounds.X + toSys.Bounds.Width / 2,
                                 toSys.Bounds.Y + toSys.Bounds.Height / 2);

        context.DrawLine(ConnectionPen, fromCenter, toCenter);

        var midX = (fromCenter.X + toCenter.X) / 2;
        var midY = (fromCenter.Y + toCenter.Y) / 2;
        var ltf = new FormattedText(
            conn.Label,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            10,
            ConnectionBrush);
        context.DrawText(ltf, new Point(midX, midY));
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(this);
        Focus();

        object? hit = null;
        foreach (var sys in _systems)
        {
            foreach (var mod in sys.Modules)
            {
                if (mod.Bounds.Contains(pos))
                {
                    hit = mod;
                    break;
                }
            }
            if (hit != null) break;
        }

        if (hit == null)
        {
            foreach (var sys in _systems)
            {
                if (sys.Bounds.Contains(pos))
                {
                    hit = sys;
                    break;
                }
            }
        }

        if (hit is SystemBoxModel sysHit)
        {
            _draggingSystem = sysHit;
            _dragOffset = new Point(pos.X - sysHit.Bounds.X, pos.Y - sysHit.Bounds.Y);
            e.Pointer.Capture(this);
        }

        if (_selectedItem != hit)
        {
            _selectedItem = hit;
            SelectionChanged?.Invoke(_selectedItem);
        }

        InvalidateVisual();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingSystem == null) return;
        var pos = e.GetPosition(this);

        var newX = pos.X - _dragOffset.X;
        var newY = pos.Y - _dragOffset.Y;
        var dx = newX - _draggingSystem.Bounds.X;
        var dy = newY - _draggingSystem.Bounds.Y;

        _draggingSystem.Bounds = new Rect(newX, newY, _draggingSystem.Bounds.Width, _draggingSystem.Bounds.Height);

        foreach (var mod in _draggingSystem.Modules)
        {
            mod.Bounds = new Rect(mod.Bounds.X + dx, mod.Bounds.Y + dy, mod.Bounds.Width, mod.Bounds.Height);
        }

        InvalidateVisual();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggingSystem != null)
        {
            e.Pointer.Capture(null);
        }
        _draggingSystem = null;
    }
}
