using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PhotoComp.Models;
using PhotoComp.ViewModels;

namespace PhotoComp.Views;

public partial class ImagePanelView : UserControl
{
    private Point _panStart;
    private double _panStartOffsetX;
    private double _panStartOffsetY;
    private bool _isPanning;
    private ZoomState? _attachedZoom;

    private const double MinScale = 0.1;
    private const double MaxScale = 10.0;
    private const double ZoomFactor = 1.15;

    public ImagePanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private ImagePanelViewModel? PanelViewModel => DataContext as ImagePanelViewModel;

    // ── ZoomState attachment ──────────────────────────────────────────

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_attachedZoom is not null)
            _attachedZoom.PropertyChanged -= OnZoomPropertyChanged;

        _attachedZoom = PanelViewModel?.SharedZoom;

        if (_attachedZoom is not null)
            _attachedZoom.PropertyChanged += OnZoomPropertyChanged;

        ApplyTransform();
    }

    private void OnZoomPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => ApplyTransform();

    private void ApplyTransform()
    {
        if (MainImage is null || _attachedZoom is null) return;

        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(_attachedZoom.Scale, _attachedZoom.Scale));
        group.Children.Add(new TranslateTransform(_attachedZoom.OffsetX, _attachedZoom.OffsetY));
        MainImage.RenderTransform = group;
    }

    // ── Zoom (mouse wheel) ────────────────────────────────────────────

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_attachedZoom is null) return;

        var pos = e.GetPosition(this);
        var oldScale = _attachedZoom.Scale;
        var rawScale = oldScale * (e.Delta.Y > 0 ? ZoomFactor : 1.0 / ZoomFactor);
        var newScale = Math.Clamp(rawScale, MinScale, MaxScale);
        var factor = newScale / oldScale;

        // Zoom towards cursor: keep the point under the cursor stationary
        _attachedZoom.OffsetX = pos.X - (pos.X - _attachedZoom.OffsetX) * factor;
        _attachedZoom.OffsetY = pos.Y - (pos.Y - _attachedZoom.OffsetY) * factor;
        _attachedZoom.Scale = newScale;

        e.Handled = true;
    }

    // ── Pan (left-button drag) ────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Focus the panel so arrow keys route here
        Focus();

        // Don't start pan if a child control (button) already handled the event
        if (e.Handled) return;

        if (e.ClickCount == 2)
        {
            _attachedZoom?.Reset();
            e.Handled = true;
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed && _attachedZoom is not null)
        {
            _panStart = e.GetPosition(this);
            _panStartOffsetX = _attachedZoom.OffsetX;
            _panStartOffsetY = _attachedZoom.OffsetY;
            _isPanning = true;
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isPanning || _attachedZoom is null) return;

        var pos = e.GetPosition(this);
        _attachedZoom.OffsetX = _panStartOffsetX + (pos.X - _panStart.X);
        _attachedZoom.OffsetY = _panStartOffsetY + (pos.Y - _panStart.Y);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isPanning)
        {
            _isPanning = false;
            e.Pointer.Capture(null);
        }
    }

    // ── Keyboard navigation ───────────────────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var vm = PanelViewModel;
        if (vm is null) return;

        switch (e.Key)
        {
            case Key.Left:
                vm.NavigatePreviousCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right:
                vm.NavigateNextCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
