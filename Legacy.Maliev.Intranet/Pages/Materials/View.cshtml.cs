using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Materials;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Intranet.Pages.Materials;

/// <summary>Displays and edits a material and its color and finish associations.</summary>
public sealed class ViewModel(ILegacyCatalogClient catalog, EmployeeSessionService sessions, ILogger<ViewModel> logger) : PageModel
{
    /// <summary>The legacy material identifier.</summary>
    public int MaterialId { get; private set; }
    /// <summary>The complete editable material payload.</summary>
    [BindProperty] public MaterialInput Input { get; set; } = new();
    /// <summary>Selected color identifiers.</summary>
    [BindProperty] public int[] SelectedColorIds { get; set; } = [];
    /// <summary>Selected surface-finish identifiers.</summary>
    [BindProperty] public int[] SelectedSurfaceFinishIds { get; set; } = [];
    /// <summary>Available material groups.</summary>
    public IReadOnlyList<MaterialGroupResponse> MaterialGroups { get; private set; } = [];
    /// <summary>Available currencies.</summary>
    public IReadOnlyList<CurrencyResponse> Currencies { get; private set; } = [];
    /// <summary>Available colors.</summary>
    public IReadOnlyList<ColorResponse> Colors { get; private set; } = [];
    /// <summary>Available surface finishes.</summary>
    public IReadOnlyList<SurfaceFinishResponse> SurfaceFinishes { get; private set; } = [];
    /// <summary>Safe operation result text.</summary>
    public string? Notification { get; private set; }

    /// <summary>Loads the material and all editor reference data.</summary>
    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login");
        var materialTask = catalog.GetMaterialAsync(id, token, cancellationToken);
        await LoadLookupsAsync(id, token, cancellationToken);
        var material = await materialTask;
        if (material is null) return NotFound();
        MaterialId = id; Input = MaterialInput.From(material);
        return Page();
    }

    /// <summary>Updates all material properties and synchronizes associations.</summary>
    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        MaterialId = id;
        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login");
        if (!ModelState.IsValid) { await LoadLookupsAsync(id, token, cancellationToken, preserveSelections: true); return Page(); }
        try
        {
            await catalog.UpdateMaterialAsync(id, Input.ToRequest(), token, cancellationToken);
            await Task.WhenAll(
                catalog.SyncMaterialColorsAsync(id, SelectedColorIds, token, cancellationToken),
                catalog.SyncMaterialSurfaceFinishesAsync(id, SelectedSurfaceFinishIds, token, cancellationToken));
            Notification = "Material saved.";
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Catalog update failed for material {MaterialId}", id);
            ModelState.AddModelError(string.Empty, "The material could not be saved. Please retry.");
        }
        await LoadLookupsAsync(id, token, cancellationToken, preserveSelections: true);
        return Page();
    }

    private async Task LoadLookupsAsync(int id, string token, CancellationToken cancellationToken, bool preserveSelections = false)
    {
        var groups = catalog.GetMaterialGroupsAsync(token, cancellationToken);
        var currencies = catalog.GetCurrenciesAsync(token, cancellationToken);
        var colors = catalog.GetColorsAsync(token, cancellationToken);
        var finishes = catalog.GetSurfaceFinishesAsync(token, cancellationToken);
        var selectedColors = preserveSelections ? Task.FromResult<IReadOnlyList<ColorResponse>>([]) : catalog.GetMaterialColorsAsync(id, token, cancellationToken);
        var selectedFinishes = preserveSelections ? Task.FromResult<IReadOnlyList<SurfaceFinishResponse>>([]) : catalog.GetMaterialSurfaceFinishesAsync(id, token, cancellationToken);
        await Task.WhenAll(groups, currencies, colors, finishes, selectedColors, selectedFinishes);
        MaterialGroups = await groups; Currencies = await currencies; Colors = await colors; SurfaceFinishes = await finishes;
        if (!preserveSelections)
        {
            SelectedColorIds = (await selectedColors).Select(x => x.Id).ToArray();
            SelectedSurfaceFinishIds = (await selectedFinishes).Select(x => x.Id).ToArray();
        }
    }
}
