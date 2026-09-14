using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PhotoComp.Converters;
using PhotoComp.Models;

namespace PhotoComp.Views;

public partial class CompareDialog : Window
{
    private ImageItem? _leftImage;
    private ImageItem? _rightImage;
    private double _splitRatio = 0.5;
    private double _preClickSplitRatio = 0.5;
    private bool _isDragging;
    private bool _updatingRatio;

    private readonly ZoomState _zoom = new();
    private bool _isPanningImage;
    private Point _panStart;
    private double _panStartOffsetX;
    private double _panStartOffsetY;

    private const double MinScale = 1.0;
    private const double MaxScale = 10.0;
    private const double WheelZoomFactor = 1.15;

    // Parameterless ctor required by Avalonia's XAML runtime loader.
    public CompareDialog()
    {
        InitializeComponent();

        ImageContainer.AddHandler(PointerPressedEvent, OnContainerPointerPressed, RoutingStrategies.Bubble);
        ImageContainer.AddHandler(PointerMovedEvent, OnContainerPointerMoved, RoutingStrategies.Bubble);
        ImageContainer.AddHandler(PointerReleasedEvent, OnContainerPointerReleased, RoutingStrategies.Bubble);
        ImageContainer.AddHandler(PointerCaptureLostEvent, OnContainerPointerReleased, RoutingStrategies.Bubble);
        ImageContainer.AddHandler(PointerWheelChangedEvent, OnContainerPointerWheelChanged, RoutingStrategies.Bubble);
        ImageContainer.SizeChanged += (_, _) => UpdateSplit();

        DividerHandle.PointerPressed += OnDividerHandlePointerPressed;
        DividerHandle.PointerMoved += OnDividerHandlePointerMoved;
        DividerHandle.PointerReleased += OnDividerHandlePointerReleased;
        DividerHandle.PointerCaptureLost += (_, _) => _isDragging = false;

        _zoom.PropertyChanged += (_, _) =>
        {
            ApplyZoomTransform();
            UpdatePercentText();
        };

        SplitSlider.ValueChanged += (_, e) => SetSplitRatio(e.NewValue);

        CloseButton.Click += (_, _) => Close();
        ResetSplitButton.Click += (_, _) => SetSplitRatio(0.5);
        SwapButton.Click += (_, _) => SwapImages();

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
    }

    public CompareDialog(ImageItem leftImage, ImageItem rightImage) : this()
    {
        _leftImage = leftImage;
        _rightImage = rightImage;
        PopulateImages();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Focus();

        if (Owner is Window ownerWindow && ownerWindow.Bounds.Width > 0 && ownerWindow.Bounds.Height > 0)
        {
            var targetWidth = ownerWindow.Bounds.Width * 0.92;
            var targetHeight = ownerWindow.Bounds.Height * 0.92;

            if (targetWidth >= MinWidth && targetHeight >= MinHeight)
            {
                Width = targetWidth;
                Height = targetHeight;
            }
        }

        UpdateSplit();
    }

    private void PopulateImages()
    {
        if (_leftImage is not null)
        {
            LeftImage.Source = StringToBitmapConverter.Instance.Convert(
                _leftImage.FilePath, typeof(Bitmap), null, CultureInfo.InvariantCulture) as Bitmap;
            LeftFileNameText.Text = _leftImage.FileName;
        }

        if (_rightImage is not null)
        {
            RightImage.Source = StringToBitmapConverter.Instance.Convert(
                _rightImage.FilePath, typeof(Bitmap), null, CultureInfo.InvariantCulture) as Bitmap;
            RightFileNameText.Text = _rightImage.FileName;
        }
    }

    private void SwapImages()
    {
        (_leftImage, _rightImage) = (_rightImage, _leftImage);
        PopulateImages();
        UpdateSplit();
    }

    private void SetSplitRatio(double ratio)
    {
        if (_updatingRatio) return;
        _updatingRatio = true;
        try
        {
            _splitRatio = Math.Clamp(ratio, 0.0, 1.0);
            if (Math.Abs(SplitSlider.Value - _splitRatio) > 0.0001)
            {
                SplitSlider.Value = _splitRatio;
            }
            UpdateSplit();
        }
        finally
        {
            _updatingRatio = false;
        }
    }

    private void UpdateSplit()
    {
        var containerWidth = ImageContainer.Bounds.Width;
        var containerHeight = ImageContainer.Bounds.Height;
        if (containerWidth <= 0 || containerHeight <= 0) return;

        double splitX = Math.Clamp(containerWidth * _splitRatio, 0, containerWidth);

        LeftImageClip.Clip = new RectangleGeometry(new Rect(0, 0, splitX, containerHeight));

        DividerLine.RenderTransform = new TranslateTransform(splitX, 0);

        UpdatePercentText();
    }

    private void UpdatePercentText()
    {
        PercentText.Text = _zoom.Scale > 1.0
            ? $"Split: {(_splitRatio * 100):F0}%  •  Zoom: {(_zoom.Scale * 100):F0}%"
            : $"Split: {(_splitRatio * 100):F0}%";
    }

    private void OnContainerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled) return; // divider handle already handled it

        if (e.ClickCount == 2)
        {
            ToggleZoom(e.GetPosition(ImageContainer), GetImageUnderPoint(e.GetPosition(ImageContainer).X, _preClickSplitRatio));
            e.Handled = true;
            return;
        }

        if (!e.GetCurrentPoint(ImageContainer).Properties.IsLeftButtonPressed) return;

        if (_zoom.Scale > 1.0)
        {
            _isPanningImage = true;
            _panStart = e.GetPosition(ImageContainer);
            _panStartOffsetX = _zoom.OffsetX;
            _panStartOffsetY = _zoom.OffsetY;
            e.Pointer.Capture(ImageContainer);
        }
        else
        {
            _preClickSplitRatio = _splitRatio; // remember the ratio before this click moves it, for a following double-click
            _isDragging = true;
            e.Pointer.Capture(ImageContainer);
            UpdateSplitFromPointer(e);
        }
    }

    private void OnContainerPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isPanningImage)
        {
            var pos = e.GetPosition(ImageContainer);
            _zoom.OffsetX = _panStartOffsetX + (pos.X - _panStart.X);
            _zoom.OffsetY = _panStartOffsetY + (pos.Y - _panStart.Y);
        }
        else if (_isDragging)
        {
            UpdateSplitFromPointer(e);
        }
    }

    private void OnContainerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isPanningImage)
        {
            _isPanningImage = false;
            e.Pointer.Capture(null);
        }
        else if (_isDragging)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
        }
    }

    private void OnContainerPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var pos = e.GetPosition(ImageContainer);
        var (localX, localY) = ToImageLocalPoint(pos, GetImageUnderPoint(pos.X, _splitRatio));

        var oldScale = _zoom.Scale;
        var rawScale = oldScale * (e.Delta.Y > 0 ? WheelZoomFactor : 1.0 / WheelZoomFactor);
        var newScale = Math.Clamp(rawScale, MinScale, MaxScale);
        var factor = newScale / oldScale;

        _zoom.OffsetX = localX - (localX - _zoom.OffsetX) * factor;
        _zoom.OffsetY = localY - (localY - _zoom.OffsetY) * factor;
        _zoom.Scale = newScale;

        e.Handled = true;
    }

    /// <summary>
    /// Double-click zoom: zooms to 100% (pixel-perfect on the given image) if not
    /// already zoomed, otherwise resets back to fit.
    /// </summary>
    private void ToggleZoom(Point pos, ImageItem? image)
    {
        if (_zoom.Scale > 1.0)
        {
            _zoom.Reset();
            return;
        }

        var containerWidth = ImageContainer.Bounds.Width;
        var containerHeight = ImageContainer.Bounds.Height;
        if (image is null || image.Width <= 0 || image.Height <= 0 || containerWidth <= 0 || containerHeight <= 0)
        {
            _zoom.Scale = 2.5;
            return;
        }

        var renderScaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var fitScale = Math.Min(containerWidth / image.Width, containerHeight / image.Height);
        var pixelScale = Math.Clamp(1.0 / (fitScale * renderScaling), MinScale, MaxScale);

        var (localX, localY) = ToImageLocalPoint(pos, image);
        _zoom.OffsetX = localX * (1.0 - pixelScale);
        _zoom.OffsetY = localY * (1.0 - pixelScale);
        _zoom.Scale = pixelScale;
    }

    private ImageItem? GetImageUnderPoint(double x, double splitRatio)
    {
        var splitX = ImageContainer.Bounds.Width * splitRatio;
        return x <= splitX ? _leftImage : _rightImage;
    }

    /// <summary>
    /// Images are centered/letterboxed within the container (Stretch="Uniform"), so a
    /// point measured against the container must be re-based onto the image's own
    /// top-left before it can be used with our (RenderTransformOrigin="0,0") zoom math.
    /// </summary>
    private (double X, double Y) ToImageLocalPoint(Point pos, ImageItem? image)
    {
        var containerWidth = ImageContainer.Bounds.Width;
        var containerHeight = ImageContainer.Bounds.Height;
        if (image is null || image.Width <= 0 || image.Height <= 0 || containerWidth <= 0 || containerHeight <= 0)
        {
            return (pos.X, pos.Y);
        }

        var fitScale = Math.Min(containerWidth / image.Width, containerHeight / image.Height);
        var renderedWidth = image.Width * fitScale;
        var renderedHeight = image.Height * fitScale;
        var offsetX = (containerWidth - renderedWidth) / 2.0;
        var offsetY = (containerHeight - renderedHeight) / 2.0;

        return (pos.X - offsetX, pos.Y - offsetY);
    }

    private void ApplyZoomTransform()
    {
        LeftImage.RenderTransform = new TransformGroup
        {
            Children =
            {
                new ScaleTransform(_zoom.Scale, _zoom.Scale),
                new TranslateTransform(_zoom.OffsetX, _zoom.OffsetY),
            },
        };
        RightImage.RenderTransform = new TransformGroup
        {
            Children =
            {
                new ScaleTransform(_zoom.Scale, _zoom.Scale),
                new TranslateTransform(_zoom.OffsetX, _zoom.OffsetY),
            },
        };
    }

    private void OnDividerHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            var pos = e.GetPosition(ImageContainer);
            ToggleZoom(pos, GetImageUnderPoint(pos.X, _preClickSplitRatio));
            e.Handled = true;
            return;
        }

        if (!e.GetCurrentPoint(DividerHandle).Properties.IsLeftButtonPressed) return;

        _preClickSplitRatio = _splitRatio; // remember the ratio before this click moves it, for a following double-click
        _isDragging = true;
        e.Pointer.Capture(DividerHandle);
        UpdateSplitFromPointer(e);
        e.Handled = true;
    }

    private void OnDividerHandlePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging) return;
        UpdateSplitFromPointer(e);
        e.Handled = true;
    }

    private void OnDividerHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void UpdateSplitFromPointer(PointerEventArgs e)
    {
        var width = ImageContainer.Bounds.Width;
        if (width > 0)
        {
            var x = e.GetPosition(ImageContainer).X;
            SetSplitRatio(x / width);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            SetSplitRatio(_splitRatio - 0.05);
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            SetSplitRatio(_splitRatio + 0.05);
            e.Handled = true;
        }
        else if (e.Key == Key.Home)
        {
            SetSplitRatio(0.0);
            e.Handled = true;
        }
        else if (e.Key == Key.End)
        {
            SetSplitRatio(1.0);
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            SetSplitRatio(_splitRatio > 0.5 ? 0.0 : 1.0);
            e.Handled = true;
        }
    }
}