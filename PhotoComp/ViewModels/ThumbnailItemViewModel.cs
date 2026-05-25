using System.ComponentModel;
using Avalonia;
using Avalonia.Media.Imaging;
using PhotoComp.Converters;

namespace PhotoComp.ViewModels;

/// <summary>
/// View model for a single cell in the thumbnail picker grid.
/// The bitmap is loaded lazily on a background thread, throttled to four concurrent disk reads.
/// Scaled thumbnails are kept in a static cache so subsequent dialog opens are instant.
/// </summary>
public sealed class ThumbnailItemViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Caps concurrent <em>disk reads</em>. Cache hits bypass this entirely
    /// because no I/O or CPU scaling is needed.
    /// </summary>
    private static readonly SemaphoreSlim _loadGate = new(4);

    /// <summary>
    /// Stores the already-scaled 240-px thumbnail for each file path across dialog opens.
    /// Keyed by absolute file path; never evicted (thumbnails are tiny — ~50 KB each).
    /// </summary>
    private static readonly Dictionary<string, Bitmap> _thumbnailCache = new();

    public int Index { get; }
    public string FilePath { get; }
    public string FileName { get; }

    /// <summary>True when this item matched the panel's current index at dialog open time.</summary>
    public bool IsCurrentImage { get; }

    /// <summary>True when this image has been hearted (selected for export).</summary>
    public bool IsHearted { get; }

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

    /// <summary>
    /// Drops all cached scaled thumbnails. Call when loading a new folder so memory
    /// from the previous folder's images can be reclaimed by the GC.
    /// </summary>
    public static void ClearCache() => _thumbnailCache.Clear();

    public ThumbnailItemViewModel(int index, string filePath, string fileName, bool isCurrentImage, bool isHearted = false)
    {
        Index          = index;
        FilePath       = filePath;
        FileName       = fileName;
        IsCurrentImage = isCurrentImage;
        IsHearted      = isHearted;

        // Pre-populate from the static cache synchronously so the UI has something to
        // display immediately on the second open, before LoadAsync even starts.
        if (_thumbnailCache.TryGetValue(filePath, out var cached))
            _thumbnail = cached;
    }

    /// <summary>
    /// Asynchronously loads a downsampled thumbnail (max 240 px on the longest side).
    /// Cache hits complete synchronously without touching the semaphore or thread pool.
    /// </summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        // Already populated from the static cache in the constructor — nothing to do.
        if (_thumbnail is not null) return;

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
        // Check the static thumbnail cache first — another Task.Run call for the same
        // path may have populated it while this one was queued behind the gate.
        if (_thumbnailCache.TryGetValue(path, out var cached))
            return cached;

        try
        {
            const int maxDim = 240;

            // Reuse an already-loaded full-res bitmap from the panel view cache
            // rather than reading the file from disk again.
            if (!StringToBitmapConverter.TryGetCached(path, out var full) || full is null)
                full = new Bitmap(path);

            double scale = Math.Min((double)maxDim / full.PixelSize.Width,
                                    (double)maxDim / full.PixelSize.Height);

            var thumb = scale >= 1.0
                ? full
                : full.CreateScaledBitmap(
                    new PixelSize(
                        Math.Max(1, (int)(full.PixelSize.Width  * scale)),
                        Math.Max(1, (int)(full.PixelSize.Height * scale))),
                    BitmapInterpolationMode.LowQuality);

            if (thumb is not null)
                _thumbnailCache[path] = thumb;

            return thumb;
        }
        catch { return null; }
    }
}
