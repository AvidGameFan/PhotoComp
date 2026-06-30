using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Avalonia.Data.Converters;

namespace PhotoComp.Converters;

/// <summary>
/// Converts a string to formatted XML or JSON if valid, otherwise returns it unchanged.
/// Used to format XML/JSON in tooltips and overlays.
/// </summary>
public sealed class XmlFormatterConverter : IValueConverter
{
    public static readonly XmlFormatterConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
            return value;

        // Unescape JSON escape sequences
        var unescaped = UnescapeJsonString(text);

        // Try XML first
        if (TryFormatXml(unescaped, out var xmlResult))
            return xmlResult;

        // Try JSON if XML fails
        if (TryFormatJson(unescaped, out var jsonResult))
            return jsonResult;

        // Return original if neither XML nor JSON
        return value;
    }

    private static bool TryFormatXml(string text, out string? result)
    {
        result = null;
        try
        {
            var doc = XDocument.Parse(text);

            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = true,
                ConformanceLevel = ConformanceLevel.Document
            };

            using (var writer = new StringWriter())
            {
                using (var xmlWriter = XmlWriter.Create(writer, settings))
                {
                    doc.WriteTo(xmlWriter);
                }
                result = writer.ToString();
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool TryFormatJson(string text, out string? result)
    {
        result = null;
        try
        {
            using (var doc = JsonDocument.Parse(text))
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                result = JsonSerializer.Serialize(doc.RootElement, options);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private static string UnescapeJsonString(string text)
    {
        try
        {
            // Try to deserialize by wrapping as a JSON string
            var jsonWrapped = $"\"{text}\"";
            return JsonSerializer.Deserialize<string>(jsonWrapped) ?? text;
        }
        catch
        {
            // If that fails, try manual unescaping
            return text
                .Replace("\\\"", "\"")  //probably not needed, but just in case
                .Replace("\\\\", "\\")
                .Replace("\\{", "{")  //Escape sequences for braces, which are needed for ED's parser
                .Replace("\\}", "}");
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
