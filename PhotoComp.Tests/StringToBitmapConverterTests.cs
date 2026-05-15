using System.Globalization;
using PhotoComp.Converters;

namespace PhotoComp.Tests;

public class StringToBitmapConverterTests
{
    private static readonly StringToBitmapConverter Converter = StringToBitmapConverter.Instance;

    // ── Null / empty / wrong-type inputs ─────────────────────────────

    [Fact]
    public void Convert_Null_ReturnsNull()
    {
        var result = Converter.Convert(null, typeof(object), null, CultureInfo.InvariantCulture);
        Assert.Null(result);
    }

    [Fact]
    public void Convert_EmptyString_ReturnsNull()
    {
        var result = Converter.Convert(string.Empty, typeof(object), null, CultureInfo.InvariantCulture);
        Assert.Null(result);
    }

    [Fact]
    public void Convert_NonString_ReturnsNull()
    {
        var result = Converter.Convert(42, typeof(object), null, CultureInfo.InvariantCulture);
        Assert.Null(result);
    }

    // ── Invalid / missing file path ───────────────────────────────────

    [Fact]
    public void Convert_NonExistentPath_ReturnsNull_WithoutThrowing()
    {
        var result = Converter.Convert(
            @"C:\no\such\file_abc123.jpg",
            typeof(object), null, CultureInfo.InvariantCulture);
        Assert.Null(result);
    }

    [Fact]
    public void Convert_GarbagePath_ReturnsNull_WithoutThrowing()
    {
        var ex = Record.Exception(() =>
            Converter.Convert("|||invalid|||", typeof(object), null, CultureInfo.InvariantCulture));
        Assert.Null(ex);
    }

    // ── ConvertBack ───────────────────────────────────────────────────

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            Converter.ConvertBack(null, typeof(string), null, CultureInfo.InvariantCulture));
    }
}
