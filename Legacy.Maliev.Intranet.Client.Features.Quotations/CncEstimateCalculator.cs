namespace Legacy.Maliev.Intranet.Client.Features.Quotations;

/// <summary>Inputs preserved from the legacy browser-only CNC quotation estimator.</summary>
public sealed record CncEstimateInput(
    decimal MachineCost,
    decimal ReturnOnInvestmentYears,
    int WeekdaysPerMonth,
    int WorkingHoursPerDay,
    int PartCount,
    int TimePerPartHours,
    int TimePerPartMinutes,
    decimal StockLengthMillimetres,
    decimal StockWidthMillimetres,
    decimal StockHeightMillimetres,
    decimal StockCostPerKilogram,
    decimal StockWeightKilograms,
    decimal StockMarkupPercent,
    decimal MaximumPowerKilowatts,
    decimal ElectricityBaseCost,
    decimal ElectricityCostPerKilowattHour,
    int NumberOfSetups,
    decimal CostPerSetup)
{
    /// <summary>Gets the values historically loaded by the Razor Page.</summary>
    public static CncEstimateInput LegacyDefaults { get; } = new(
        2_300_000m,
        2m,
        21,
        8,
        10,
        2,
        0,
        100m,
        100m,
        100m,
        250m,
        1m,
        15m,
        22.4m,
        523m,
        4.3297m,
        2,
        300m);
}

/// <summary>Calculated CNC cost breakdown.</summary>
public sealed record CncEstimateResult(
    decimal ReturnOnInvestmentMonths,
    decimal ReturnOnInvestmentDays,
    decimal ReturnOnInvestmentHours,
    decimal MachineCostPerHour,
    decimal TimePerPartHours,
    decimal TotalMachiningHours,
    decimal MachineCost,
    decimal StockVolumeCubicMillimetres,
    decimal MaterialCostPerPart,
    decimal MaterialCost,
    decimal TotalPowerConsumptionKilowattHours,
    decimal ElectricityCost,
    decimal SetupCostPerPart,
    decimal SetupCost,
    decimal GrandTotal);

/// <summary>Deterministic replacement for the legacy inline JavaScript calculator.</summary>
public static class CncEstimateCalculator
{
    /// <summary>Calculates the complete legacy estimate without network or persistence side effects.</summary>
    public static CncEstimateResult Calculate(CncEstimateInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var roiMonths = NonNegative(input.ReturnOnInvestmentYears) * 12m;
        var roiDays = roiMonths * Math.Max(0, input.WeekdaysPerMonth);
        var roiHours = roiDays * Math.Max(0, input.WorkingHoursPerDay);
        var machineCostPerHour = roiHours == 0m ? 0m : Round(NonNegative(input.MachineCost) / roiHours);

        var partCount = Math.Max(0, input.PartCount);
        var timePerPart = Round(Math.Max(0, input.TimePerPartHours) + Math.Max(0, input.TimePerPartMinutes) / 60m);
        var totalMachiningHours = timePerPart * partCount;
        var machineCost = Round(machineCostPerHour * totalMachiningHours);

        var stockVolume = NonNegative(input.StockLengthMillimetres)
            * NonNegative(input.StockWidthMillimetres)
            * NonNegative(input.StockHeightMillimetres);
        var materialCostPerPart = Round(
            NonNegative(input.StockCostPerKilogram)
            * NonNegative(input.StockWeightKilograms)
            * (1m + NonNegative(input.StockMarkupPercent) / 100m));
        var materialCost = Round(materialCostPerPart * partCount);

        var totalPower = Round(NonNegative(input.MaximumPowerKilowatts) * totalMachiningHours);
        var electricityCost = Round(
            totalPower * NonNegative(input.ElectricityCostPerKilowattHour)
            + NonNegative(input.ElectricityBaseCost));

        var setupCostPerPart = Round(Math.Max(0, input.NumberOfSetups) * NonNegative(input.CostPerSetup));
        var setupCost = Round(setupCostPerPart * partCount);
        var grandTotal = Round(machineCost + materialCost + electricityCost + setupCost);

        return new(
            roiMonths,
            roiDays,
            roiHours,
            machineCostPerHour,
            timePerPart,
            totalMachiningHours,
            machineCost,
            stockVolume,
            materialCostPerPart,
            materialCost,
            totalPower,
            electricityCost,
            setupCostPerPart,
            setupCost,
            grandTotal);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal NonNegative(decimal value) => Math.Max(0m, value);
}
