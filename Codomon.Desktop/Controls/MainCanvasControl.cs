using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Codomon.Desktop.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Codomon.Desktop.Controls;

public class MainCanvasControl : Control
{
    private readonly WorkspaceModel _workspace;
    private readonly SelectionStateModel _selectionState;

    /// <summary>Invoked when the user finishes dragging a system box, marking the workspace as dirty.</summary>
    public Action? OnLayoutChanged;

    private SystemBoxModel? _draggingSystem;
    private Point _dragOffset;

    private static readonly IBrush SystemFill = new SolidColorBrush(Color.FromRgb(30, 58, 95));
    private static readonly IBrush SystemFillSelected = new SolidColorBrush(Color.FromRgb(0, 120, 215));
    private static readonly IBrush SystemFillSoftHighlight = new SolidColorBrush(Color.FromRgb(20, 100, 140));
    private static readonly IBrush SystemFillMuted = new SolidColorBrush(Color.FromArgb(80, 30, 58, 95));
    private static readonly IBrush SystemStroke = new SolidColorBrush(Color.FromRgb(100, 160, 220));
    private static readonly IBrush SystemStrokeMuted = new SolidColorBrush(Color.FromArgb(80, 100, 160, 220));
    private static readonly IBrush ModuleFill = new SolidColorBrush(Color.FromRgb(50, 90, 130));
    private static readonly IBrush ModuleFillSelected = new SolidColorBrush(Color.FromRgb(0, 180, 100));
    private static readonly IBrush ModuleFillHighlight = new SolidColorBrush(Color.FromRgb(0, 220, 160));
    private static readonly IBrush ModuleFillMuted = new SolidColorBrush(Color.FromArgb(80, 50, 90, 130));
    private static readonly IBrush ModuleStroke = new SolidColorBrush(Color.FromRgb(100, 200, 160));
    private static readonly IBrush ModuleStrokeMuted = new SolidColorBrush(Color.FromArgb(80, 100, 200, 160));
    private static readonly IBrush ModuleStrokeHighlight = new SolidColorBrush(Color.FromRgb(0, 255, 200));
    private static readonly IBrush ConnectionBrush = new SolidColorBrush(Color.FromRgb(180, 180, 60));
    private static readonly IBrush LabelBrush = Brushes.White;
    private static readonly IBrush LabelBrushMuted = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255));
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(20, 25, 35));

    private static readonly Pen SystemPen = new Pen(SystemStroke, 2);
    private static readonly Pen SystemPenSelected = new Pen(SystemFillSelected, 3);
    private static readonly Pen SystemPenSoftHighlight = new Pen(SystemFillSoftHighlight, 2.5);
    private static readonly Pen SystemPenMuted = new Pen(SystemStrokeMuted, 1);
    private static readonly Pen ModulePen = new Pen(ModuleStroke, 1.5);
    private static readonly Pen ModulePenSelected = new Pen(ModuleFillSelected, 2);
    private static readonly Pen ModulePenHighlight = new Pen(ModuleStrokeHighlight, 2.5);
    private static readonly Pen ModulePenMuted = new Pen(ModuleStrokeMuted, 1);
    private static readonly Pen ConnectionPen = new Pen(ConnectionBrush, 1.5) { DashStyle = DashStyle.Dash };

    // ── Highlight state ───────────────────────────────────────────────────────

    private static readonly TimeSpan HighlightDuration = TimeSpan.FromSeconds(2);
    private readonly Dictionary<string, DateTimeOffset> _moduleHighlights = new();
    private readonly Dictionary<string, DateTimeOffset> _systemSoftHighlights = new();
    private readonly DispatcherTimer _decayTimer;

    public MainCanvasControl(WorkspaceModel workspace, SelectionStateModel selectionState)
    {
        _workspace = workspace;
        _selectionState = selectionState;

        Focusable = true;
        ClipToBounds = true;

        _selectionState.PropertyChanged += (_, _) => InvalidateVisual();

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;

        // Decay timer: checks every 300 ms and redraws if any highlight has expired.
        _decayTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, OnDecayTick);
        _decayTimer.Start();
    }

    // ── Highlight API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Applies a strong highlight to <paramref name="moduleId"/> and a soft highlight to
    /// <paramref name="systemId"/>.  Both decay after <see cref="HighlightDuration"/>.
    /// </summary>
    public void HighlightModule(string moduleId, string systemId)
    {
        var expiry = DateTimeOffset.UtcNow + HighlightDuration;
        _moduleHighlights[moduleId] = expiry;
        _systemSoftHighlights[systemId] = expiry;
        InvalidateVisual();
    }

    /// <summary>
    /// Applies a soft highlight to <paramref name="systemId"/> only.
    /// </summary>
    public void HighlightSystem(string systemId)
    {
        _systemSoftHighlights[systemId] = DateTimeOffset.UtcNow + HighlightDuration;
        InvalidateVisual();
    }

    private void OnDecayTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        bool changed = false;

        foreach (var key in _moduleHighlights.Keys.ToList())
        {
            if (_moduleHighlights[key] <= now) { _moduleHighlights.Remove(key); changed = true; }
        }
        foreach (var key in _systemSoftHighlights.Keys.ToList())
        {
            if (_systemSoftHighlights[key] <= now) { _systemSoftHighlights.Remove(key); changed = true; }
        }

        if (changed) InvalidateVisual();
    }

    private static Rect GetModuleAbsoluteBounds(ModuleBoxModel mod, SystemBoxModel sys)
        => new Rect(sys.X + mod.RelativeX, sys.Y + mod.RelativeY, mod.Width, mod.Height);

    private static Rect GetSystemBounds(SystemBoxModel sys)
        => new Rect(sys.X, sys.Y, sys.Width, sys.Height);

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(BackgroundBrush, new Rect(Bounds.Size));

        foreach (var conn in _workspace.Connections)
        {
            DrawConnection(context, conn);
        }

        foreach (var sys in _workspace.Systems)
        {
            DrawSystem(context, sys);
        }
    }

    private void DrawSystem(DrawingContext context, SystemBoxModel sys)
    {
        bool isSelected = _selectionState.SelectedId == sys.Id;
        bool isSoftHighlight = _systemSoftHighlights.ContainsKey(sys.Id);
        bool isMuted = !sys.IsVisible;

        IBrush fill;
        Pen pen;
        IBrush labelBrush;

        if (isMuted)
        {
            fill = SystemFillMuted;
            pen  = SystemPenMuted;
            labelBrush = LabelBrushMuted;
        }
        else if (isSelected)
        {
            fill = SystemFillSelected;
            pen  = SystemPenSelected;
            labelBrush = LabelBrush;
        }
        else if (isSoftHighlight)
        {
            fill = SystemFillSoftHighlight;
            pen  = SystemPenSoftHighlight;
            labelBrush = LabelBrush;
        }
        else
        {
            fill = SystemFill;
            pen  = SystemPen;
            labelBrush = LabelBrush;
        }

        var bounds = GetSystemBounds(sys);
        context.FillRectangle(fill, bounds);
        context.DrawRectangle(pen, bounds);

        var tf = new FormattedText(
            sys.Name,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            14,
            labelBrush);
        context.DrawText(tf, new Point(bounds.X + 8, bounds.Y + 8));

        foreach (var mod in sys.Modules)
        {
            bool modMuted = !mod.IsVisible;
            bool modSelected = _selectionState.SelectedId == mod.Id;
            bool modHighlight = _moduleHighlights.ContainsKey(mod.Id);

            IBrush mFill;
            Pen mPen;
            IBrush mLabelBrush;

            if (modMuted)
            {
                mFill = ModuleFillMuted;
                mPen  = ModulePenMuted;
                mLabelBrush = LabelBrushMuted;
            }
            else if (modSelected)
            {
                mFill = ModuleFillSelected;
                mPen  = ModulePenSelected;
                mLabelBrush = LabelBrush;
            }
            else if (modHighlight)
            {
                mFill = ModuleFillHighlight;
                mPen  = ModulePenHighlight;
                mLabelBrush = LabelBrush;
            }
            else
            {
                mFill = ModuleFill;
                mPen  = ModulePen;
                mLabelBrush = LabelBrush;
            }

            var mBounds = GetModuleAbsoluteBounds(mod, sys);
            context.FillRectangle(mFill, mBounds);
            context.DrawRectangle(mPen, mBounds);

            var mtf = new FormattedText(
                mod.Name,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                11,
                mLabelBrush);
            context.DrawText(mtf, new Point(mBounds.X + 4, mBounds.Y + 4));
        }
    }

    private void DrawConnection(DrawingContext context, ConnectionModel conn)
    {
        var fromPt = ResolveConnectionPoint(conn.FromId);
        var toPt = ResolveConnectionPoint(conn.ToId);
        if (fromPt == null || toPt == null) return;

        context.DrawLine(ConnectionPen, fromPt.Value, toPt.Value);

        var midX = (fromPt.Value.X + toPt.Value.X) / 2;
        var midY = (fromPt.Value.Y + toPt.Value.Y) / 2;
        var ltf = new FormattedText(
            conn.Name,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            10,
            ConnectionBrush);
        context.DrawText(ltf, new Point(midX, midY));
    }

    private Point? ResolveConnectionPoint(string id)
    {
        var sys = _workspace.Systems.FirstOrDefault(s => s.Id == id);
        if (sys != null)
            return new Point(sys.X + sys.Width / 2, sys.Y + sys.Height / 2);

        foreach (var s in _workspace.Systems)
        {
            var mod = s.Modules.FirstOrDefault(m => m.Id == id);
            if (mod != null)
                return new Point(s.X + mod.RelativeX + mod.Width / 2, s.Y + mod.RelativeY + mod.Height / 2);
        }

        return null;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(this);
        Focus();

        // Check modules first (they are on top of systems)
        ModuleBoxModel? hitMod = null;
        SystemBoxModel? hitModParent = null;
        foreach (var sys in _workspace.Systems)
        {
            if (!sys.IsVisible) continue;
            foreach (var mod in sys.Modules)
            {
                if (!mod.IsVisible) continue;
                if (GetModuleAbsoluteBounds(mod, sys).Contains(pos))
                {
                    hitMod = mod;
                    hitModParent = sys;
                    break;
                }
            }
            if (hitMod != null) break;
        }

        SystemBoxModel? hitSys = null;
        if (hitMod == null)
        {
            foreach (var sys in _workspace.Systems)
            {
                if (!sys.IsVisible) continue;
                if (GetSystemBounds(sys).Contains(pos))
                {
                    hitSys = sys;
                    break;
                }
            }
        }

        if (hitSys != null)
        {
            _draggingSystem = hitSys;
            _dragOffset = new Point(pos.X - hitSys.X, pos.Y - hitSys.Y);
            e.Pointer.Capture(this);
        }

        string newId = hitMod?.Id ?? hitSys?.Id ?? string.Empty;
        if (_selectionState.SelectedId != newId)
        {
            _workspace.ClearSelection();
            if (hitMod != null)
            {
                hitMod.IsSelected = true;
                _selectionState.SelectedId = hitMod.Id;
                _selectionState.SelectedType = "Module";
                _selectionState.SelectedName = hitMod.Name;
            }
            else if (hitSys != null)
            {
                hitSys.IsSelected = true;
                _selectionState.SelectedId = hitSys.Id;
                _selectionState.SelectedType = "System";
                _selectionState.SelectedName = hitSys.Name;
            }
            else
            {
                _selectionState.SelectedId = string.Empty;
                _selectionState.SelectedType = string.Empty;
                _selectionState.SelectedName = string.Empty;
            }
        }

        InvalidateVisual();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingSystem == null) return;
        var pos = e.GetPosition(this);

        _draggingSystem.X = pos.X - _dragOffset.X;
        _draggingSystem.Y = pos.Y - _dragOffset.Y;

        InvalidateVisual();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggingSystem != null)
        {
            e.Pointer.Capture(null);
            OnLayoutChanged?.Invoke();
        }
        _draggingSystem = null;
    }
}
