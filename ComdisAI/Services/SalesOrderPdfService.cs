using System.Globalization;
using System.Reflection;
using ComdisAI.Models;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace ComdisAI.Services;

public sealed class SalesOrderPdfService
{
    private static readonly string[] CopyLabels =
    [
        "CUSTOMER COPY",
        "BUSINESS COPY",
        "DELIVERY NOTE"
    ];

    private const double PageMargin = 28;
    private const double TableRowHeight = 18;
    private const double FooterReserve = 104;
    private static readonly byte[] RobotoFont = ReadRobotoFont();
    private static readonly XFont TitleFont;
    private static readonly XFont HeaderFont;
    private static readonly XFont BodyFont;
    private static readonly XFont SmallFont;

    static SalesOrderPdfService()
    {
        GlobalFontSettings.FontResolver = new RobotoFontResolver();
        TitleFont = new XFont("Roboto", 13, XFontStyleEx.Regular);
        HeaderFont = new XFont("Roboto", 8, XFontStyleEx.Regular);
        BodyFont = new XFont("Roboto", 8, XFontStyleEx.Regular);
        SmallFont = new XFont("Roboto", 7, XFontStyleEx.Regular);
    }

    public byte[] Create(SalesOrderHeader order)
    {
        using var document = new PdfDocument();
        foreach (var copyLabel in CopyLabels)
            DrawCopy(document, order, copyLabel);

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    private static void DrawCopy(PdfDocument document, SalesOrderHeader order, string copyLabel)
    {
        var details = order.Details.OrderBy(detail => detail.Id).ToList();
        var detailIndex = 0;
        var pageNumber = 1;
        var page = AddPage(document, order, copyLabel, pageNumber);
        var y = DrawTableHeadings(page);

        while (detailIndex < details.Count)
        {
            if (y + TableRowHeight > page.Height.Point - FooterReserve)
            {
                page = AddPage(document, order, copyLabel, ++pageNumber);
                y = DrawTableHeadings(page);
            }

            var detail = details[detailIndex++];
            using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var columns = GetColumnPositions(page);
            graphics.DrawString(detail.Product.Name, SmallFont, XBrushes.Black,
                new XRect(columns[0], y, columns[1] - columns[0] - 4, TableRowHeight), XStringFormats.CenterLeft);
            graphics.DrawString(Format(detail.Qty), SmallFont, XBrushes.Black,
                new XRect(columns[1], y, columns[2] - columns[1] - 4, TableRowHeight), XStringFormats.CenterRight);
            graphics.DrawString(Format(detail.Price), SmallFont, XBrushes.Black,
                new XRect(columns[2], y, columns[3] - columns[2] - 4, TableRowHeight), XStringFormats.CenterRight);
            graphics.DrawString(Format(detail.SubTotal), SmallFont, XBrushes.Black,
                new XRect(columns[3], y, page.Width.Point - PageMargin - columns[3], TableRowHeight), XStringFormats.CenterRight);
            y += TableRowHeight;
        }

        using (var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
        {
            var left = PageMargin;
            var right = page.Width.Point - PageMargin;
            graphics.DrawLine(XPens.Black, left, y + 3, right, y + 3);
            graphics.DrawString($"Subtotal: {Format(order.Subtotal)}", BodyFont, XBrushes.Black,
                new XRect(left, y + 8, right - left, 14), XStringFormats.CenterRight);
            graphics.DrawString(
                $"Discounts: {Format(order.DiscountPercent1)}% / {Format(order.DiscountPercent2)}% / {Format(order.DiscountPercent3)}%",
                SmallFont, XBrushes.Black, new XRect(left, y + 24, right - left, 12), XStringFormats.CenterRight);
            graphics.DrawString($"Tax: {Format(order.TaxAmount)}", BodyFont, XBrushes.Black,
                new XRect(left, y + 38, right - left, 14), XStringFormats.CenterRight);
            graphics.DrawString($"TOTAL: {Format(order.Total)}", HeaderFont, XBrushes.Black,
                new XRect(left, y + 55, right - left, 15), XStringFormats.CenterRight);
            graphics.DrawString($"Tax: {order.Tax.Name} ({Format(order.Tax.Value * 100m)}%)", SmallFont, XBrushes.Black,
                new XRect(left, y + 73, right - left, 12), XStringFormats.CenterRight);
        }
    }

    private static PdfPage AddPage(
        PdfDocument document,
        SalesOrderHeader order,
        string copyLabel,
        int pageNumber)
    {
        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A5;
        using var graphics = XGraphics.FromPdfPage(page);
        var width = page.Width.Point - 2 * PageMargin;
        graphics.DrawString("SALES ORDER", TitleFont, XBrushes.Black,
            new XRect(PageMargin, 18, width, 20), XStringFormats.CenterLeft);
        graphics.DrawString(copyLabel, HeaderFont, XBrushes.Black,
            new XRect(PageMargin, 39, width, 14), XStringFormats.CenterLeft);
        graphics.DrawString($"Order: {order.OrderNumber}    Status: {order.Status.Name}", BodyFont, XBrushes.Black,
            new XRect(PageMargin, 57, width, 14), XStringFormats.CenterLeft);
        graphics.DrawString($"Customer: {order.Customer.Name}", BodyFont, XBrushes.Black,
            new XRect(PageMargin, 74, width, 14), XStringFormats.CenterLeft);
        graphics.DrawString($"Delivery date: {order.DeliveryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
            BodyFont, XBrushes.Black, new XRect(PageMargin, 91, width, 14), XStringFormats.CenterLeft);
        graphics.DrawString($"Delivery address: {order.DeliveryAddress}", SmallFont, XBrushes.Black,
            new XRect(PageMargin, 108, width, 27), XStringFormats.TopLeft);
        graphics.DrawString($"Page {pageNumber}", SmallFont, XBrushes.Black,
            new XRect(PageMargin, page.Height.Point - 22, width, 12), XStringFormats.CenterRight);
        return page;
    }

    private static double DrawTableHeadings(PdfPage page)
    {
        var y = 143d;
        var columns = GetColumnPositions(page);
        using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        graphics.DrawLine(XPens.Black, PageMargin, y, page.Width.Point - PageMargin, y);
        graphics.DrawString("Product", HeaderFont, XBrushes.Black,
            new XRect(columns[0], y + 2, columns[1] - columns[0] - 4, TableRowHeight), XStringFormats.CenterLeft);
        graphics.DrawString("Qty", HeaderFont, XBrushes.Black,
            new XRect(columns[1], y + 2, columns[2] - columns[1] - 4, TableRowHeight), XStringFormats.CenterRight);
        graphics.DrawString("Price", HeaderFont, XBrushes.Black,
            new XRect(columns[2], y + 2, columns[3] - columns[2] - 4, TableRowHeight), XStringFormats.CenterRight);
        graphics.DrawString("Subtotal", HeaderFont, XBrushes.Black,
            new XRect(columns[3], y + 2, page.Width.Point - PageMargin - columns[3], TableRowHeight), XStringFormats.CenterRight);
        graphics.DrawLine(XPens.Black, PageMargin, y + TableRowHeight, page.Width.Point - PageMargin, y + TableRowHeight);
        return y + TableRowHeight;
    }

    private static double[] GetColumnPositions(PdfPage page)
    {
        var usableWidth = page.Width.Point - 2 * PageMargin;
        return [PageMargin, PageMargin + usableWidth * 0.55, PageMargin + usableWidth * 0.68, PageMargin + usableWidth * 0.83];
    }

    private static string Format(decimal value) =>
        value.ToString("0.0000", CultureInfo.InvariantCulture);

    private static byte[] ReadRobotoFont()
    {
        using var stream = typeof(SalesOrderPdfService).Assembly.GetManifestResourceStream(
            "ComdisAI.Resources.Fonts.RobotoVariable.ttf")
            ?? throw new InvalidOperationException("The embedded Roboto font resource is missing.");
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    private sealed class RobotoFontResolver : IFontResolver
    {
        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            string.Equals(familyName, "Roboto", StringComparison.OrdinalIgnoreCase)
                ? new FontResolverInfo("RobotoVariable")
                : null;

        public byte[]? GetFont(string faceName) =>
            string.Equals(faceName, "RobotoVariable", StringComparison.Ordinal)
                ? RobotoFont
                : null;
    }
}
