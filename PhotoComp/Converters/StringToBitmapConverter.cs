using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using PhotoComp.Infrastructure;

namespace PhotoComp.Converters;

/// <summary>
/// Converts a file path string to an Avalonia <see cref="Bitmap"/>.
/// Bitmaps are cached in an LRU cache to improve performance during image navigation.
/// 
/// NOTE: We do NOT dispose evicted bitmaps because Avalonia's rendering pipeline may
/// still hold references to them (especially in dual-panel view). Disposing a bitmap
/// while it's being rendered causes ObjectDisposedException. Instead, we let the GC
/// collect bitmaps naturally when no longer referenced.
/// 
/// Returns null for null/empty paths or files that cannot be loaded.
/// </summary>
public sealed class StringToBitmapConverter : IValueConverter
{
    public static readonly StringToBitmapConverter Instance = new();

    private static readonly LruCache<string, Bitmap> Cache = new(
        capacity: 50,
        onEvict: null); // Don't dispose - let GC handle it to avoid ObjectDisposedException during rendering

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
            return null;

        if (Cache.TryGet(path, out var cached))
            return cached;

        try
        {
            var bmp = new Bitmap(path);
            Cache.Set(path, bmp);
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
