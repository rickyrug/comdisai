using System.Globalization;
using ComdisAI.Models;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace ComdisAI.Services;

public sealed class PurchaseOrderPdfService
{
    private static readonly string[] CopyLabels =
    [
        "SUPPLIER COPY",
        "BUSINESS COPY",
        SupplyNoteLabel
    ];

    private const string SupplyNoteLabel = "SUPPLY NOTE";
    private const double PageMargin = 28;
    private const double TableRowHeight = 13;
    private const double PageBottomReserve = 34;
    private const double SummaryRowHeight = 12;
    private const double SummaryHeight = 2 * SummaryRowHeight + 6;
    private static readonly XFont TitleFont;
    private static readonly XFont HeaderFont;
    private static readonly XFont BodyFont;
    private static readonly XFont SmallFont;
    private static XFont SummaryLineFont => SmallFont;

    static PurchaseOrderPdfService()
    {
        EmbeddedRobotoFontResolver.EnsureConfigured();
        TitleFont = new XFont("Roboto", 13, XFontStyleEx.Regular);
        HeaderFont = new XFont("Roboto", 8, XFontStyleEx.Regular);
        BodyFont = new XFont("Roboto", 8, XFontStyleEx.Regular);
        SmallFont = new XFont("Roboto", 7, XFontStyleEx.Regular);
    }

    public byte[] Create(PurchaseOrderHeader order)
    {
        using var document = new PdfDocument();
        foreach (var copyLabel in CopyLabels)
            DrawCopy(document, order, copyLabel);

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    private static void DrawCopy(PdfDocument document, PurchaseOrderHeader order, string copyLabel)
    {
        var details = order.Details
            .OrderBy(detail => detail.Product.ProductCategory?.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(detail => detail.Product.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(detail => detail.Id)
            .ToList();
        var quantitiesOnly = copyLabel == SupplyNoteLabel;
        var detailIndex = 0;
        var pageNumber = 1;
        var page = AddPage(document, order, copyLabel, pageNumber);
        var y = DrawTableHeadings(page, quantitiesOnly);
        var totals = PurchaseOrderTotals.From(order);

        while (detailIndex < details.Count)
        {
            var remaining = details.Count - detailIndex;
            var allFit = y + remaining * TableRowHeight + SummaryHeight + 6 <=
                page.Height.Point - PageBottomReserve;
            if (!allFit && y + TableRowHeight > page.Height.Point - PageBottomReserve)
            {
                page = AddPage(document, order, copyLabel, ++pageNumber);
                y = DrawTableHeadings(page, quantitiesOnly);
            }

            var detail = details[detailIndex++];
            var gross = detail.Qty * detail.Price;
            var discount = gross - detail.SubTotal;
            using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var columns = GetColumnPositions(page, quantitiesOnly);
            var rowCells = new[]
            {
                null,
                detail.Qty.ToString("0.####", CultureInfo.InvariantCulture),
                detail.Product.Uom?.Name ?? "",
                Money(detail.Price),
                (detail.DiscountPercent ?? 0m).ToString("0.####", CultureInfo.InvariantCulture) + "%",
                discount > 0m ? "-" + Money(discount) : Money(0m),
                Money(detail.SubTotal)
            };
            if (quantitiesOnly)
                rowCells = rowCells[..3];
            graphics.DrawString(
                Fit(graphics, detail.Product.Name, columns[1] - columns[0] - 4),
                SmallFont, XBrushes.Black,
                new XRect(columns[0], y, columns[1] - columns[0] - 4, TableRowHeight), XStringFormats.CenterLeft);
            for (var i = 1; i < columns.Length; i++)
            {
                var end = i + 1 < columns.Length ? columns[i + 1] - 3 : page.Width.Point - PageMargin;
                graphics.DrawString(Fit(graphics, rowCells[i]!, end - columns[i]), SmallFont, XBrushes.Black,
                    new XRect(columns[i], y, end - columns[i], TableRowHeight), XStringFormats.CenterRight);
            }
            y += TableRowHeight;
        }

        if (quantitiesOnly)
        {
            if (y + 40 > page.Height.Point - PageBottomReserve)
            {
                page = AddPage(document, order, copyLabel, ++pageNumber);
                y = DrawTableHeadings(page, quantitiesOnly);
            }
            using var note = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            note.DrawLine(XPens.Black, PageMargin, y, page.Width.Point - PageMargin, y);
            DrawReceivedBy(note, page);
            return;
        }

        if (y + SummaryHeight + 6 > page.Height.Point - PageBottomReserve)
        {
            page = AddPage(document, order, copyLabel, ++pageNumber);
            y = DrawTableHeadings(page, quantitiesOnly);
        }

        using var summary = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var left = PageMargin;
        var right = page.Width.Point - PageMargin;
        summary.DrawLine(XPens.Black, left, y, right, y);
        y += 6;

        var discounts = totals.Discount1 + totals.Discount2 + totals.Discount3;
        var line = $"Subtotal {Money(order.Subtotal)}    " +
            $"Total discounts ({Pct(order.DiscountPercent1)}% + {Pct(order.DiscountPercent2)}% + {Pct(order.DiscountPercent3)}%) -{Money(discounts)}    " +
            $"Tax: {order.Tax.Name} ({Pct(order.Tax.Value * 100m)}%) {Money(order.TaxAmount)}";
        var lineFont = SummaryLineFont;
        if (summary.MeasureString(line, lineFont).Width > right - left)
            lineFont = new XFont("Roboto", 6, XFontStyleEx.Regular);
        summary.DrawString(line, lineFont, XBrushes.Black,
            new XRect(left, y, right - left, SummaryRowHeight), XStringFormats.CenterRight);
        y += SummaryRowHeight;
        summary.DrawString($"TOTAL {Money(order.Total)}", HeaderFont, XBrushes.Black,
            new XRect(left, y, right - left, SummaryRowHeight), XStringFormats.CenterRight);

    }

    private static void DrawReceivedBy(XGraphics graphics, PdfPage page)
    {
        var width = page.Width.Point - 2 * PageMargin;
        var signatureY = page.Height.Point - PageBottomReserve - 12;
        graphics.DrawLine(XPens.Black, PageMargin + width * 0.5, signatureY, PageMargin + width, signatureY);
        graphics.DrawString("Received by (name and signature)", SmallFont, XBrushes.Black,
            new XRect(PageMargin + width * 0.5, signatureY + 1, width * 0.5, 10), XStringFormats.Center);
    }

    private static List<string> Wrap(XGraphics graphics, string text, double width, int maxLines)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && graphics.MeasureString(candidate, SmallFont).Width > width)
            {
                lines.Add(current);
                current = word;
            }
            else
                current = candidate;
        }
        if (current.Length > 0)
            lines.Add(current);
        if (lines.Count > maxLines)
        {
            lines = lines.Take(maxLines).ToList();
            lines[^1] = Fit(graphics, lines[^1] + "...", width);
        }
        return lines.Select(line => Fit(graphics, line, width)).ToList();
    }

    private static string Fit(XGraphics graphics, string text, double width)
    {
        if (graphics.MeasureString(text, SmallFont).Width <= width)
            return text;
        while (text.Length > 1 && graphics.MeasureString(text + "...", SmallFont).Width > width)
            text = text[..^1];
        return text + "...";
    }

    private static string Pct(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Money(decimal value) => value.ToString("C2", CultureInfo.GetCultureInfo("en-US"));

    private sealed record PurchaseOrderTotals(
        decimal Gross, decimal LineDiscounts, decimal Discount1, decimal Discount2, decimal Discount3, decimal Taxable)
    {
        public static PurchaseOrderTotals From(PurchaseOrderHeader order)
        {
            var gross = order.Details.Sum(d => d.Qty * d.Price);
            var lineDiscounts = gross - order.Details.Sum(d => d.SubTotal);
            var d1 = order.Subtotal * order.DiscountPercent1 / 100m;
            var afterD1 = order.Subtotal - d1;
            var d2 = afterD1 * order.DiscountPercent2 / 100m;
            var afterD2 = afterD1 - d2;
            var d3 = afterD2 * order.DiscountPercent3 / 100m;
            return new PurchaseOrderTotals(gross, lineDiscounts, d1, d2, d3, afterD2 - d3);
        }
    }

    private static PdfPage AddPage(
        PdfDocument document,
        PurchaseOrderHeader order,
        string copyLabel,
        int pageNumber)
    {
        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A5;
        using var graphics = XGraphics.FromPdfPage(page);
        var width = page.Width.Point - 2 * PageMargin;
        graphics.DrawString($"PURCHASE ORDER {order.OrderNumber}    {copyLabel}", TitleFont, XBrushes.Black,
            new XRect(PageMargin, 18, width, 20), XStringFormats.CenterLeft);
        graphics.DrawString(
            $"Expected supply: {order.SupplyDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
            BodyFont, XBrushes.Black, new XRect(PageMargin, 18, width, 20), XStringFormats.CenterRight);
        var lines = Wrap(graphics, $"Supplier: {order.Supplier.Name} (RFC {order.Supplier.RFC})", width, 2);
        for (var i = 0; i < lines.Count; i++)
            graphics.DrawString(lines[i], SmallFont, XBrushes.Black,
                new XRect(PageMargin, 44 + i * 10, width, 10), XStringFormats.CenterLeft);
        var supplierAddress = Fit(graphics, $"Supplier address: {order.Supplier.Address}", width);
        graphics.DrawString(supplierAddress, SmallFont, XBrushes.Black,
            new XRect(PageMargin, 64, width, 10), XStringFormats.CenterLeft);
        var supplyAddress = Fit(graphics, $"Supply to: {order.SupplyAddress}", width);
        graphics.DrawString(supplyAddress, SmallFont, XBrushes.Black,
            new XRect(PageMargin, 74, width, 10), XStringFormats.CenterLeft);
        graphics.DrawString($"Page {pageNumber}", SmallFont, XBrushes.Black,
            new XRect(PageMargin, page.Height.Point - 22, width, 12), XStringFormats.CenterRight);
        return page;
    }

    private static double DrawTableHeadings(PdfPage page, bool quantitiesOnly)
    {
        var y = 92d;
        var columns = GetColumnPositions(page, quantitiesOnly);
        using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        graphics.DrawLine(XPens.Black, PageMargin, y, page.Width.Point - PageMargin, y);
        string[] titles = quantitiesOnly
            ? ["Product", "Qty", "UOM"]
            : ["Product", "Qty", "UOM", "Price", "Disc %", "Disc amt", "Subtotal"];
        for (var i = 0; i < titles.Length; i++)
        {
            var end = i + 1 < columns.Length ? columns[i + 1] - 3 : page.Width.Point - PageMargin;
            graphics.DrawString(titles[i], SmallFont, XBrushes.Black,
                new XRect(columns[i], y + 2, end - columns[i], TableRowHeight),
                i == 0 ? XStringFormats.CenterLeft : XStringFormats.CenterRight);
        }
        graphics.DrawLine(XPens.Black, PageMargin, y + TableRowHeight, page.Width.Point - PageMargin, y + TableRowHeight);
        return y + TableRowHeight;
    }

    private static double[] GetColumnPositions(PdfPage page, bool quantitiesOnly)
    {
        var usableWidth = page.Width.Point - 2 * PageMargin;
        var fractions = quantitiesOnly
            ? new[] { 0d, 0.60, 0.80 }
            : new[] { 0d, 0.30, 0.40, 0.50, 0.64, 0.74, 0.86 };
        return fractions.Select(f => PageMargin + usableWidth * f).ToArray();
    }

}
