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
    private bool _isDragging;
    private bool _updatingRatio;

    // Parameterless ctor required by Avalonia's XAML runtime loader.
    public CompareDialog()
    {
        InitializeComponent();

        ImageContainer.AddHandler(PointerPressedEvent, OnContainerPointerPressed, RoutingStrategies.Bubble);
        ImageContainer.AddHandler(PointerMovedEvent, OnContainerPointerMoved, RoutingStrategies.Bubble);
        ImageContainer.AddHandler(PointerReleasedEvent, OnContainerPointerReleased, RoutingStrategies.Bubble);
        ImageContainer.AddHandler(PointerCaptureLostEvent, OnContainerPointerReleased, RoutingStrategies.Bubble);
        ImageContainer.SizeChanged += (_, _) => UpdateSplit();

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

        PercentText.Text = $"Split: {(_splitRatio * 100):F0}%";
    }

    private void OnContainerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(ImageContainer).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            e.Pointer.Capture(ImageContainer);
            UpdateSplitFromPointer(e);
        }
    }

    private void OnContainerPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDragging)
        {
            UpdateSplitFromPointer(e);
        }
    }

    private void OnContainerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
        }
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