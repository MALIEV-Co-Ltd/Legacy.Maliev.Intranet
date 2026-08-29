using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Legacy.Maliev.Intranet.Tests;

public sealed partial class RoutedPageLocalizationParityTests
{
    [Fact]
    public void EveryOwnedRoutedPage_HasEnglishAndThaiResourcesWithMatchingKeysAndPlaceholders()
    {
        var root = FindRepositoryRoot();
        var pages = EnumerateOwnedRoutedPages(root).ToArray();

        Assert.NotEmpty(pages);
        Assert.Equal(43, pages.Length);

        foreach (var page in pages)
        {
            var source = File.ReadAllText(page);
            var englishPath = Path.ChangeExtension(page, ".resx");
            var thaiPath = Path.ChangeExtension(page, ".th.resx");

            Assert.True(File.Exists(englishPath), $"Missing English resources for {Path.GetRelativePath(root, page)}");
            Assert.True(File.Exists(thaiPath), $"Missing Thai resources for {Path.GetRelativePath(root, page)}");
            Assert.Contains("IStringLocalizer<", source, StringComparison.Ordinal);
            Assert.Contains("<PageTitle>", source, StringComparison.Ordinal);

            var english = ReadValues(englishPath);
            var thai = ReadValues(thaiPath);

            Assert.Contains("PageTitle", english.Keys);
            Assert.Equal(english.Keys.Order(StringComparer.Ordinal), thai.Keys.Order(StringComparer.Ordinal));
            Assert.All(thai, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), $"Empty Thai value {pair.Key} in {thaiPath}"));
            Assert.Contains(thai.Values, ContainsThai);

            foreach (var key in english.Keys)
            {
                Assert.Equal(
                    Placeholders(english[key]),
                    Placeholders(thai[key]));
            }
        }
    }

    [Fact]
    public void NewlyLocalizedPages_DoNotReintroduceKnownHardcodedEnglishCopy()
    {
        var root = FindRepositoryRoot();
        var relativePaths = new[]
        {
            "Legacy.Maliev.Intranet.Client/Pages/CompatibilityDetailRedirect.razor",
            "Legacy.Maliev.Intranet.Client/Pages/Foundation.razor",
            "Legacy.Maliev.Intranet.Client/Pages/NotFound.razor",
            "Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/FinanceCreate.razor",
            "Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeProfile.razor"
        };

        foreach (var relativePath in relativePaths)
        {
            var source = File.ReadAllText(Path.Combine(root, relativePath));
            var markup = MarkupBeforeCodeBlock(source);
            Assert.DoesNotMatch(VisibleEnglishElement(), markup);
            Assert.DoesNotMatch(HardcodedVisibleAttribute(), markup);
        }
    }

    [Fact]
    public void LocalizedDateAndMoneyContracts_PreserveThaiBusinessConventions()
    {
        var root = FindRepositoryRoot();
        var financeCreate = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client.Features.Accounting",
            "Pages",
            "FinanceCreate.razor"));
        var employeeProfile = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client.Features.Employees",
            "Pages",
            "EmployeeProfile.razor"));

        Assert.Contains("Asia/Bangkok", financeCreate, StringComparison.Ordinal);
        Assert.Contains("CultureInfo.CurrentCulture", employeeProfile, StringComparison.Ordinal);
        Assert.DoesNotContain("ToString(\"yyyy-MM-dd\")", employeeProfile, StringComparison.Ordinal);

        var finances = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client.Features.Accounting",
            "Pages",
            "Finances.razor"));
        Assert.Contains("CultureInfo.CurrentCulture", finances, StringComparison.Ordinal);
        Assert.Contains("thbCurrencyId", finances, StringComparison.Ordinal);
    }

    [Fact]
    public void ThaiOperationsGlossary_UsesProfessionalManufacturingAndAccountingTerms()
    {
        var root = FindRepositoryRoot();

        var expectedTerms = new (string RelativePath, string Key, string Value)[]
        {
            ("Legacy.Maliev.Intranet.Client.Features.Orders/Pages/Orders.th.resx", "Orders", "ใบสั่งผลิต"),
            ("Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderDetail.th.resx", "PromisedDate", "วันที่กำหนดส่ง"),
            ("Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/Suppliers.th.resx", "Suppliers", "ผู้ขาย"),
            ("Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/SupplierCreate.th.resx", "State", "จังหวัด/รัฐ"),
            ("Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrders.th.resx", "PurchaseOrders", "ใบสั่งซื้อ"),
            ("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Invoices.th.resx", "Outstanding", "ยอดค้างชำระ"),
            ("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/NetProfitChart.th.resx", "NetIncome", "กำไรสุทธิ"),
            ("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/Index.th.resx", "Decision", "ผลการพิจารณา"),
            ("Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/MaterialCreate.th.resx", "MachinabilityPercent", "ความสามารถในการตัดเฉือน (%)"),
            ("Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/MaterialCreate.th.resx", "Aisi", "AISI"),
            ("Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/MaterialCreate.th.resx", "En", "EN")
        };

        foreach (var (relativePath, key, value) in expectedTerms)
        {
            var resources = ReadValues(Path.Combine(root, relativePath));
            Assert.Equal(value, resources[key]);
        }
    }

    [Fact]
    public void ThaiRouteResources_DoNotContainKnownMachineTranslationDefects()
    {
        var root = FindRepositoryRoot();
        var thaiResources = Directory.EnumerateFiles(root, "*.th.resx", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.worktrees{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => path.Contains("Legacy.Maliev.Intranet.Client", StringComparison.Ordinal));

        var forbiddenPhrases = new[]
        {
            "ซัพพลายเออร์",
            "ผู้จัดหา",
            "ผู้จัดจำหน่าย",
            "ผู้จำหน่าย",
            "โดดเด่น",
            "ประหยัด...",
            "ประหยัด…",
            "วันที่สัญญาไว้",
            "ทุกออเดอร์",
            "สร้างสื่อการสอน",
            "การขึ้นต่อกันอย่างใดอย่างหนึ่ง",
            "ความล้มเหลวในการบริการ",
            "รหัสความสัมพันธ์",
            "เส้นทางที่แก้ไขแล้ว",
            "การวินิจฉัยการปฏิบัติงาน"
        };

        foreach (var path in thaiResources)
        {
            var values = ReadValues(path).Values;
            foreach (var phrase in forbiddenPhrases)
            {
                Assert.DoesNotContain(values, value => value.Contains(phrase, StringComparison.Ordinal));
            }
        }
    }

    private static IEnumerable<string> EnumerateOwnedRoutedPages(string root)
    {
        var projectDirectories = Directory.EnumerateDirectories(root, "Legacy.Maliev.Intranet.Client*")
            .Where(path => Path.GetFileName(path).Equals("Legacy.Maliev.Intranet.Client", StringComparison.Ordinal)
                || Path.GetFileName(path).StartsWith("Legacy.Maliev.Intranet.Client.Features.", StringComparison.Ordinal));

        return projectDirectories
            .SelectMany(path => Directory.EnumerateFiles(path, "*.razor", SearchOption.AllDirectories))
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Pages{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetFileName(path).Equals("Dashboard.razor", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("@page ", StringComparison.Ordinal));
    }

    private static Dictionary<string, string> ReadValues(string path) =>
        XDocument.Load(path)
            .Root!
            .Elements("data")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);

    private static string[] Placeholders(string value) =>
        Placeholder().Matches(value).Select(match => match.Value).Order(StringComparer.Ordinal).ToArray();

    private static bool ContainsThai(string value) => value.Any(character => character is >= '\u0E00' and <= '\u0E7F');

    private static string MarkupBeforeCodeBlock(string source)
    {
        var codeBlock = source.IndexOf("@code", StringComparison.Ordinal);
        return codeBlock >= 0 ? source[..codeBlock] : source;
    }

    [GeneratedRegex(@"(?<!=)>[ \t]*[A-Za-z][^<@{}\r\n]*<", RegexOptions.CultureInvariant)]
    private static partial Regex VisibleEnglishElement();

    [GeneratedRegex(@"(?:Label|Placeholder|HelperText|aria-label)=""[A-Za-z]", RegexOptions.CultureInvariant)]
    private static partial Regex HardcodedVisibleAttribute();

    [GeneratedRegex(@"\{\d+\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Legacy.Maliev.Intranet repository root.");
    }
}
