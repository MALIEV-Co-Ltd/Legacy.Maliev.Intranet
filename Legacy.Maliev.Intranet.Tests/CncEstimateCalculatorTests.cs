using Legacy.Maliev.Intranet.Client.Features.Quotations;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class CncEstimateCalculatorTests
{
    [Fact]
    public void DefaultsPreserveLegacyBrowserFormulaAndRounding()
    {
        var estimate = CncEstimateCalculator.Calculate(CncEstimateInput.LegacyDefaults);

        Assert.Equal(24m, estimate.ReturnOnInvestmentMonths);
        Assert.Equal(504m, estimate.ReturnOnInvestmentDays);
        Assert.Equal(4032m, estimate.ReturnOnInvestmentHours);
        Assert.Equal(570.44m, estimate.MachineCostPerHour);
        Assert.Equal(2m, estimate.TimePerPartHours);
        Assert.Equal(20m, estimate.TotalMachiningHours);
        Assert.Equal(11408.80m, estimate.MachineCost);
        Assert.Equal(1_000_000m, estimate.StockVolumeCubicMillimetres);
        Assert.Equal(287.50m, estimate.MaterialCostPerPart);
        Assert.Equal(2875m, estimate.MaterialCost);
        Assert.Equal(448m, estimate.TotalPowerConsumptionKilowattHours);
        Assert.Equal(2462.71m, estimate.ElectricityCost);
        Assert.Equal(600m, estimate.SetupCostPerPart);
        Assert.Equal(6000m, estimate.SetupCost);
        Assert.Equal(22746.51m, estimate.GrandTotal);
    }

    [Fact]
    public void RecalculatesDerivedValuesFromEditedInputs()
    {
        var input = CncEstimateInput.LegacyDefaults with
        {
            MachineCost = 1000m,
            ReturnOnInvestmentYears = 1m,
            WeekdaysPerMonth = 10,
            WorkingHoursPerDay = 10,
            PartCount = 2,
            TimePerPartHours = 1,
            TimePerPartMinutes = 30,
            StockCostPerKilogram = 100m,
            StockWeightKilograms = 2m,
            StockMarkupPercent = 10m,
            MaximumPowerKilowatts = 2m,
            ElectricityBaseCost = 5m,
            ElectricityCostPerKilowattHour = 3m,
            NumberOfSetups = 1,
            CostPerSetup = 50m,
        };

        var estimate = CncEstimateCalculator.Calculate(input);

        Assert.Equal(0.83m, estimate.MachineCostPerHour);
        Assert.Equal(1.5m, estimate.TimePerPartHours);
        Assert.Equal(3m, estimate.TotalMachiningHours);
        Assert.Equal(2.49m, estimate.MachineCost);
        Assert.Equal(220m, estimate.MaterialCostPerPart);
        Assert.Equal(440m, estimate.MaterialCost);
        Assert.Equal(6m, estimate.TotalPowerConsumptionKilowattHours);
        Assert.Equal(23m, estimate.ElectricityCost);
        Assert.Equal(50m, estimate.SetupCostPerPart);
        Assert.Equal(100m, estimate.SetupCost);
        Assert.Equal(565.49m, estimate.GrandTotal);
    }

    [Fact]
    public void InvalidOrZeroInputsFailClosedWithoutNonFiniteCosts()
    {
        var input = CncEstimateInput.LegacyDefaults with
        {
            MachineCost = -1m,
            ReturnOnInvestmentYears = 0m,
            WeekdaysPerMonth = 0,
            WorkingHoursPerDay = 0,
            PartCount = -2,
            TimePerPartHours = -1,
            TimePerPartMinutes = -30,
            ElectricityBaseCost = -10m,
        };

        var estimate = CncEstimateCalculator.Calculate(input);

        Assert.Equal(0m, estimate.MachineCostPerHour);
        Assert.Equal(0m, estimate.TotalMachiningHours);
        Assert.Equal(0m, estimate.MachineCost);
        Assert.Equal(0m, estimate.MaterialCost);
        Assert.Equal(0m, estimate.ElectricityCost);
        Assert.Equal(0m, estimate.SetupCost);
        Assert.Equal(0m, estimate.GrandTotal);
    }
}
