using System.Globalization;
using System.Xml.Linq;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class PresentationBoundaryTests
{
    [Theory]
    [InlineData("en-US", "03 Aug 2026, 19:30")]
    [InlineData("th-TH", "03 ส.ค. 2569, 19:30")]
    public void FormatUtcDateTime_ConvertsStoredUtcToBangkokAndUsesCurrentCalendar(
        string cultureName,
        string expected)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var storedUtc = new DateTime(2026, 8, 3, 12, 30, 0, DateTimeKind.Utc);

        var displayed = LegacyPresentation.FormatUtcDateTime(storedUtc, culture, "-");

        Assert.Equal(expected, displayed);
        Assert.Equal(DateTimeKind.Utc, storedUtc.Kind);
    }

    [Fact]
    public void FormatUtcDateTime_TreatsUnspecifiedApiTimestampAsUtc()
    {
        var unspecified = new DateTime(2026, 8, 3, 12, 30, 0, DateTimeKind.Unspecified);

        var displayed = LegacyPresentation.FormatUtcDateTime(
            unspecified,
            CultureInfo.GetCultureInfo("en-US"),
            "-");

        Assert.Equal("03 Aug 2026, 19:30", displayed);
        Assert.Equal(DateTimeKind.Unspecified, unspecified.Kind);
    }

    [Fact]
    public void FormatCalendarDate_DoesNotShiftDateOnlyValuesAcrossTimeZones()
    {
        var dateOfBirth = new DateTime(1988, 2, 29, 0, 0, 0, DateTimeKind.Unspecified);

        var displayed = LegacyPresentation.FormatCalendarDate(
            dateOfBirth,
            CultureInfo.GetCultureInfo("en-GB"),
            "-");

        Assert.Equal("29/02/1988", displayed);
    }

    [Fact]
    public void FormatUtcDate_UsesBangkokCalendarDay()
    {
        var storedUtc = new DateTime(2026, 8, 3, 18, 30, 0, DateTimeKind.Utc);

        var displayed = LegacyPresentation.FormatUtcDate(
            storedUtc,
            CultureInfo.GetCultureInfo("en-GB"),
            "-");

        Assert.Equal("04/08/2026", displayed);
    }

    [Fact]
    public void CreateRequestTimeout_BoundsUiReads()
    {
        using var timeout = LegacyPresentation.CreateRequestTimeout();

        Assert.False(timeout.IsCancellationRequested);
        Assert.Equal(TimeSpan.FromSeconds(15), LegacyPresentation.RequestTimeout);
    }

    [Fact]
    public async Task GetForPresentationAsync_TransitionsHungReadToTerminalCancellation()
    {
        using var client = new HttpClient(new NeverCompletesHandler())
        {
            BaseAddress = new Uri("https://localhost"),
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetForPresentationAsync("/bff/hung", TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public async Task GetForPresentationAsync_HonorsCallerCancellationBeforeTerminalTimeout()
    {
        using var client = new HttpClient(new NeverCompletesHandler())
        {
            BaseAddress = new Uri("https://localhost"),
        };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetForPresentationAsync(
                "/bff/superseded",
                cancellation.Token,
                TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void MigratedFeatureReads_AreBoundedAndDetailFailuresOfferRecovery()
    {
        var root = FindRepositoryRoot();
        var featureFolders = new[]
        {
            "Legacy.Maliev.Intranet.Client.Features.Accounting",
            "Legacy.Maliev.Intranet.Client.Features.Catalog",
            "Legacy.Maliev.Intranet.Client.Features.Customers",
            "Legacy.Maliev.Intranet.Client.Features.Diagnostics",
            "Legacy.Maliev.Intranet.Client.Features.Employees",
            "Legacy.Maliev.Intranet.Client.Features.Orders",
            "Legacy.Maliev.Intranet.Client.Features.Procurement",
            "Legacy.Maliev.Intranet.Client.Features.Quotations",
            "Legacy.Maliev.Intranet.Client",
        };

        foreach (var featureFolder in featureFolders)
        {
            foreach (var page in Directory.EnumerateFiles(
                         Path.Combine(root, featureFolder),
                         "*.razor",
                         SearchOption.AllDirectories))
            {
                var source = File.ReadAllText(page);
                Assert.DoesNotContain("Http.GetAsync(", source, StringComparison.Ordinal);
                Assert.DoesNotContain("Http.GetFromJsonAsync", source, StringComparison.Ordinal);
            }
        }

        var recoverableDetails = new[]
        {
            ("Legacy.Maliev.Intranet.Client.Features.Accounting", "Pages", "FinanceView.razor"),
            ("Legacy.Maliev.Intranet.Client.Features.Accounting", "Pages", "InvoiceView.razor"),
            ("Legacy.Maliev.Intranet.Client.Features.Catalog", "Pages", "MaterialDetail.razor"),
            ("Legacy.Maliev.Intranet.Client.Features.Customers", "Pages", "CustomerView.razor"),
            ("Legacy.Maliev.Intranet.Client.Features.Employees", "Pages", "EmployeeView.razor"),
            ("Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages/QuotationRequests", "View.razor"),
            ("Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages/Quotations", "View.razor"),
        };

        foreach (var (project, folder, file) in recoverableDetails)
        {
            var source = File.ReadAllText(Path.Combine(root, project, folder, file));
            Assert.Contains("OnClick=\"LoadAsync\"", source, StringComparison.Ordinal);
            Assert.Contains("Text[\"Retry\"]", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ServerOrchestration_DoesNotBlockOnCompletedTasks()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "Legacy.Maliev.Intranet.Server", "PurchaseOrders", "PurchaseOrderDetailService.cs"),
            Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Dashboard", "LegacyDashboardAggregator.cs"),
            Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Orders", "OrderDetailAggregator.cs"),
            Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Procurement", "PurchaseOrderCreationGateway.cs"),
            Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Quotations", "QuotationCreationGateway.cs"),
        };

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain(".Result", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GetAwaiter().GetResult", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MaterialSortResources_CoverEveryExposedEnumValueInBothLanguages()
    {
        var root = FindRepositoryRoot();
        var folder = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Catalog", "Pages");
        var english = ReadResourceKeys(Path.Combine(folder, "Materials.resx"));
        var thai = ReadResourceKeys(Path.Combine(folder, "Materials.th.resx"));

        foreach (var sort in Enum.GetNames<CatalogMaterialSort>())
        {
            Assert.Contains(sort, english);
            Assert.Contains(sort, thai);
        }
    }

    private sealed class NeverCompletesHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }

    private static HashSet<string> ReadResourceKeys(string path) =>
        XDocument.Load(path)
            .Root!
            .Elements("data")
            .Select(element => (string?)element.Attribute("name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Legacy.Maliev.Intranet.Contracts")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Legacy.Maliev.Intranet repository root.");
    }
}
