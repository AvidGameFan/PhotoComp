using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
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

    private bool _isPinching;
    private double _lastPinchScale = 1.0;

    private const double MinScale = 0.1;
    private const double MaxScale = 10.0;
    private const double ZoomFactor = 1.15;

    public ImagePanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        GestureRecognizers.Add(new PinchGestureRecognizer());
        AddHandler(InputElement.PinchEvent, OnPinch);
        AddHandler(InputElement.PinchEndedEvent, OnPinchEnded);
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

    // ── Zoom (touchscreen pinch) ─────────────────────────────────────

    private void OnPinch(object? sender, PinchEventArgs e)
    {
        if (_attachedZoom is null) return;

        // Cancel any pan that started from the first touch contact
        _isPanning = false;

        if (!_isPinching)
        {
            _isPinching = true;
            _lastPinchScale = 1.0;
        }

        var pinchCenter = e.ScaleOrigin;

        // e.Scale is cumulative from gesture start; derive a per-event delta
        var scaleDelta = e.Scale / _lastPinchScale;
        _lastPinchScale = e.Scale;

        var oldScale = _attachedZoom.Scale;
        var newScale = Math.Clamp(oldScale * scaleDelta, MinScale, MaxScale);
        var factor = newScale / oldScale;

        _attachedZoom.OffsetX = pinchCenter.X - (pinchCenter.X - _attachedZoom.OffsetX) * factor;
        _attachedZoom.OffsetY = pinchCenter.Y - (pinchCenter.Y - _attachedZoom.OffsetY) * factor;
        _attachedZoom.Scale = newScale;

        e.Handled = true;
    }

    private void OnPinchEnded(object? sender, PinchEndedEventArgs e)
    {
        _isPinching = false;
        _lastPinchScale = 1.0;
    }

    // ── Zoom (mouse wheel / touchpad) ─────────────────────────────────

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
            if (_attachedZoom is not null && _attachedZoom.Scale == 1.0)
            {
                // Already at 100% fit → zoom to pixel-perfect (1 image px = 1 screen px).
                var image = PanelViewModel?.CurrentImage;
                if (image is not null && image.Width > 0 && image.Height > 0)
                {
                    // fitScale: how many DIPs one image pixel occupies at Scale=1.0 (Stretch="Uniform")
                    var fitScale = Math.Min(Bounds.Width / image.Width,
                                           Bounds.Height / image.Height);
                    // renderScaling: physical pixels per DIP (e.g. 1.5 at 150% DPI)
                    var renderScaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
                    var pixelScale = Math.Clamp(1.0 / (fitScale * renderScaling), MinScale, MaxScale);

                    // Zoom towards the click point so it stays under the cursor.
                    var pos = e.GetPosition(this);
                    _attachedZoom.OffsetX = pos.X * (1.0 - pixelScale); // oldOffsetX=0, oldScale=1
                    _attachedZoom.OffsetY = pos.Y * (1.0 - pixelScale);
                    _attachedZoom.Scale   = pixelScale;
                }
            }
            else
            {
                _attachedZoom?.Reset();
            }
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
            case Key.Delete:
                vm.DeleteCurrentCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.C when e.KeyModifiers == KeyModifiers.Control:
                vm.CopyToClipboardCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
