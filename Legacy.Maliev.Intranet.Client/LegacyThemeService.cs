using Microsoft.JSInterop;

namespace Legacy.Maliev.Intranet.Client;

/// <summary>Coordinates the current Intranet theme preference with the blocking browser bootstrap.</summary>
public sealed class LegacyThemeService(IJSRuntime jsRuntime, ILogger<LegacyThemeService> logger)
{
    private bool _initialized;

    /// <summary>Gets whether the effective workspace theme is dark.</summary>
    public bool IsDarkMode { get; private set; }

    /// <summary>Raised after the effective theme changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Reads the already-applied browser theme without causing a first-render flash.</summary>
    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            IsDarkMode = await jsRuntime.InvokeAsync<bool>("malievTheme.isDark");
        }
        catch (JSDisconnectedException)
        {
            logger.LogDebug("Theme interop disconnected during initialization.");
        }
        catch (JSException exception)
        {
            logger.LogDebug(exception, "Theme bootstrap is unavailable during initialization.");
        }
        catch (InvalidOperationException exception)
        {
            logger.LogDebug(exception, "Theme interop is unavailable during initialization.");
        }
        finally
        {
            _initialized = true;
        }
    }

    /// <summary>Toggles the persisted light/dark preference and publishes the new state.</summary>
    public async Task ToggleAsync()
    {
        try
        {
            IsDarkMode = await jsRuntime.InvokeAsync<bool>("malievTheme.toggle");
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (JSDisconnectedException)
        {
            logger.LogDebug("Theme interop disconnected while toggling.");
        }
        catch (JSException exception)
        {
            logger.LogWarning(exception, "Theme toggle is unavailable; retaining the current theme.");
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "Theme toggle is unavailable; retaining the current theme.");
        }
    }
}
