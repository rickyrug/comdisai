using System.Globalization;
using ComdisAI.Models;

namespace ComdisAI.Services;

public static class PurchaseOrderPricing
{
    private const decimal MaximumPersistedValue = 99_999_999_999_999.9999m;

    public static void Recalculate(PurchaseOrderHeader header)
    {
        foreach (var detail in header.Details)
        {
            var discount = detail.DiscountPercent ?? 0m;
            detail.SubTotal = Round(detail.Price * detail.Qty * (1m - discount / 100m));
        }

        header.Subtotal = Round(header.Details.Sum(detail => detail.SubTotal));
        var taxableAmount = ApplyDiscount(header.Subtotal, header.DiscountPercent1);
        taxableAmount = ApplyDiscount(taxableAmount, header.DiscountPercent2);
        taxableAmount = ApplyDiscount(taxableAmount, header.DiscountPercent3);
        header.TaxAmount = Round(taxableAmount * header.Tax.Value);
        header.Total = Round(taxableAmount + header.TaxAmount);
    }

    public static decimal Round(decimal value)
    {
        var rounded = decimal.Round(value, 4, MidpointRounding.AwayFromZero);
        if (rounded > MaximumPersistedValue || rounded < -MaximumPersistedValue)
            throw new ArgumentOutOfRangeException(nameof(value), "Calculated amount must fit decimal(18,4).");
        return rounded;
    }

    private static decimal ApplyDiscount(decimal amount, decimal discountPercent) =>
        Round(amount * (1m - discountPercent / 100m));
}

public static class PurchaseOrderNumberGenerator
{
    public static string NextNumber(int year, IEnumerable<string> existingNumbers)
    {
        var prefix = year.ToString("0000", CultureInfo.InvariantCulture) + "-";
        var sequence = 0;
        foreach (var orderNumber in existingNumbers)
        {
            if (!orderNumber.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var suffix = orderNumber[prefix.Length..];
            if (suffix.Length != 5 ||
                !int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
                parsed is < 1 or > 99_999)
            {
                throw new InvalidOperationException(
                    $"Purchase order number '{orderNumber}' does not match the required YYYY-00001 format.");
            }

            sequence = Math.Max(sequence, parsed);
        }

        if (sequence >= 99_999)
            throw new InvalidOperationException($"The purchase order sequence for {year} is exhausted.");

        return $"{prefix}{(sequence + 1).ToString("D5", CultureInfo.InvariantCulture)}";
    }
}