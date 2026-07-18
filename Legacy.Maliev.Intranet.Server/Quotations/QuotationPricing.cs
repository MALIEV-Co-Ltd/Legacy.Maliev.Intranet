using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Server.Quotations;

/// <summary>Authoritative deterministic pricing for the legacy quotation create workflow.</summary>
public static class QuotationPricing
{
    /// <summary>Calculates rounded line and quotation totals using the preserved legacy rules.</summary>
    public static PricedQuotation Calculate(QuotationCreateRequest request, DateTimeOffset now)
    {
        var lines = request.Lines.Select(line =>
        {
            var discount = line.UnitPrice * (line.DiscountPercent / 100m);
            var unitPrice = decimal.Round(line.UnitPrice - discount, 2, MidpointRounding.AwayFromZero);
            var subtotal = decimal.Round(unitPrice * line.Quantity, 2, MidpointRounding.AwayFromZero);
            return new PricedQuotationLine(line.OrderId, line.Description, line.Quantity, unitPrice, subtotal);
        }).ToArray();
        var subtotal = decimal.Round(lines.Sum(line => line.Subtotal), 2, MidpointRounding.AwayFromZero);
        var vat = decimal.Round(subtotal * 0.07m, 2, MidpointRounding.AwayFromZero);
        var total = decimal.Round(subtotal + vat, 2, MidpointRounding.AwayFromZero);
        var withholding = request.WithholdingTaxEnabled && subtotal > 1000m
            ? decimal.Round(subtotal * WithholdingRate(now.UtcDateTime), 2, MidpointRounding.AwayFromZero)
            : 0m;
        return new(
            lines,
            subtotal,
            vat,
            total,
            withholding,
            decimal.Round(total - withholding, 2, MidpointRounding.AwayFromZero));
    }

    private static decimal WithholdingRate(DateTime utcNow)
    {
        if (utcNow >= new DateTime(2020, 4, 1, 0, 0, 0, DateTimeKind.Utc) &&
            utcNow < new DateTime(2020, 9, 30, 0, 0, 0, DateTimeKind.Utc))
        {
            return 0.015m;
        }

        if (utcNow >= new DateTime(2020, 10, 1, 0, 0, 0, DateTimeKind.Utc) &&
            utcNow < new DateTime(2021, 12, 31, 0, 0, 0, DateTimeKind.Utc))
        {
            return 0.02m;
        }

        return 0.03m;
    }
}

/// <summary>One server-priced quotation line.</summary>
public sealed record PricedQuotationLine(int? OrderId, string Description, int Quantity, decimal UnitPrice, decimal Subtotal);

/// <summary>Authoritative calculated quotation totals.</summary>
public sealed record PricedQuotation(
    IReadOnlyList<PricedQuotationLine> Lines,
    decimal Subtotal,
    decimal Vat,
    decimal Total,
    decimal WithholdingTax,
    decimal QuotedAmount);
