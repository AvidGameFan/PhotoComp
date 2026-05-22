using System.ComponentModel;
using Avalonia;
using Avalonia.Media.Imaging;

namespace PhotoComp.ViewModels;

/// <summary>
/// View model for a single cell in the thumbnail picker grid.
/// The bitmap is loaded lazily on a background thread, throttled to four concurrent reads.
/// </summary>
public sealed class ThumbnailItemViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private static readonly SemaphoreSlim _loadGate = new(4);

    public int Index { get; }
    public string FilePath { get; }
    public string FileName { get; }

    /// <summary>True when this item matched the panel's current index at dialog open time.</summary>
    public bool IsCurrentImage { get; }

    private Bitmap? _thumbnail;
    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        private set
        {
            _thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        }
    }

    public ThumbnailItemViewModel(int index, string filePath, string fileName, bool isCurrentImage)
    {
        Index          = index;
        FilePath       = filePath;
        FileName       = fileName;
        IsCurrentImage = isCurrentImage;
    }

    /// <summary>
    /// Asynchronously loads a downsampled thumbnail (max 240 px on the longest side).
    /// Uses <see cref="_loadGate"/> to cap concurrent file reads.
    /// </summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        bool entered = false;
        try
        {
            await _loadGate.WaitAsync(ct).ConfigureAwait(false);
            entered = true;

            if (ct.IsCancellationRequested) return;
            Thumbnail = await Task.Run(() => CreateThumbnail(FilePath), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (entered) _loadGate.Release();
        }
    }

    private static Bitmap? CreateThumbnail(string path)
    {
        try
        {
            const int maxDim = 240;
            var full = new Bitmap(path);

            double scale = Math.Min((double)maxDim / full.PixelSize.Width,
                                    (double)maxDim / full.PixelSize.Height);
            if (scale >= 1.0) return full; // Already small — use as-is

            int w = Math.Max(1, (int)(full.PixelSize.Width  * scale));
            int h = Math.Max(1, (int)(full.PixelSize.Height * scale));

            // We intentionally do NOT dispose `full` here.  Avalonia's renderer may still
            // hold a reference to a bitmap for one or two frames after it leaves a binding
            // (same rationale as StringToBitmapConverter).  Let the GC collect it.
            return full.CreateScaledBitmap(new PixelSize(w, h), BitmapInterpolationMode.LowQuality);
        }
        catch { return null; }
    }
}
