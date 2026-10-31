using PdfSharp.Fonts;

namespace ComdisAI.Services;

internal static class EmbeddedRobotoFontResolver
{
    private static readonly byte[] Font = ReadRobotoFont();

    static EmbeddedRobotoFontResolver()
    {
        GlobalFontSettings.FontResolver = new Resolver();
    }

    public static void EnsureConfigured()
    {
    }

    private static byte[] ReadRobotoFont()
    {
        using var stream = typeof(EmbeddedRobotoFontResolver).Assembly.GetManifestResourceStream(
            "ComdisAI.Resources.Fonts.RobotoVariable.ttf")
            ?? throw new InvalidOperationException("The embedded Roboto font resource is missing.");
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    private sealed class Resolver : IFontResolver
    {
        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            string.Equals(familyName, "Roboto", StringComparison.OrdinalIgnoreCase)
                ? new FontResolverInfo("RobotoVariable")
                : null;

        public byte[]? GetFont(string faceName) =>
            string.Equals(faceName, "RobotoVariable", StringComparison.Ordinal)
                ? Font
                : null;
    }
}