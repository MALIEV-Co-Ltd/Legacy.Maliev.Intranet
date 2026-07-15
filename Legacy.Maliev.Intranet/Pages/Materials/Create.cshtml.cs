using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Materials;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Intranet.Pages.Materials;

/// <summary>Creates complete material records through CatalogService.</summary>
public sealed class CreateModel(ILegacyCatalogClient catalog, EmployeeSessionService sessions) : PageModel
{
    /// <summary>The validated material payload.</summary>
    [BindProperty] public MaterialInput Input { get; set; } = new();
    /// <summary>Available material groups.</summary>
    public IReadOnlyList<MaterialGroupResponse> MaterialGroups { get; private set; } = [];
    /// <summary>Available currencies.</summary>
    public IReadOnlyList<CurrencyResponse> Currencies { get; private set; } = [];

    /// <summary>Loads reference data for the material editor.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);
        if (token is null) return RedirectToPage("/Login");
        await LoadLookupsAsync(token, cancellationToken);
        return Page();
    }

    /// <summary>Creates the material and redirects to its editable view.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);
        if (token is null) return RedirectToPage("/Login");
        if (!ModelState.IsValid)
        {
            await LoadLookupsAsync(token, cancellationToken);
            return Page();
        }
        try
        {
            var created = await catalog.CreateMaterialAsync(Input.ToRequest(), token, cancellationToken);
            return RedirectToPage("/Materials/View", new { id = created.Id });
        }
        catch (HttpRequestException)
        {
            await LoadLookupsAsync(token, cancellationToken);
            ModelState.AddModelError(string.Empty, "The material could not be created. Please retry.");
            return Page();
        }
    }

    private Task<string?> GetTokenAsync(CancellationToken cancellationToken) => sessions.GetAccessTokenAsync(HttpContext, cancellationToken);
    private async Task LoadLookupsAsync(string token, CancellationToken cancellationToken)
    {
        var groups = catalog.GetMaterialGroupsAsync(token, cancellationToken);
        var currencies = catalog.GetCurrenciesAsync(token, cancellationToken);
        await Task.WhenAll(groups, currencies);
        MaterialGroups = await groups; Currencies = await currencies;
    }
}