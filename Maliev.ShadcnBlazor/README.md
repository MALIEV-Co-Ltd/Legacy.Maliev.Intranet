# Maliev.ShadcnBlazor

Reusable Shadcn Base/Vega/Neutral components for .NET 10 Blazor, backed by MudBlazor 9.7.0.

## Register

```csharp
using Maliev.ShadcnBlazor;
using Maliev.ShadcnBlazor.Theming;

builder.Services.AddMalievShadcn(options =>
{
    options.FontFamily = "IBM Plex Sans Thai, sans-serif";
    options.DefaultDarkMode = false;
    options.DefaultDirection = ShadcnDirection.LeftToRight;
});
```

The configured font family is applied to both MudBlazor typography and the scoped
`--shadcn-font-sans` semantic token. Provider parameters override the configured defaults.

## Load assets in this order

```html
<link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />
<link href="css/ibm-plex-sans-thai.css" rel="stylesheet" />
<link href="_content/Maliev.ShadcnBlazor/css/shadcn-base.css" rel="stylesheet" />
<link href="_content/Maliev.ShadcnBlazor/css/shadcn-mudblazor.css" rel="stylesheet" />
<script src="_content/MudBlazor/MudBlazor.min.js"></script>
```

## Provide theme and portals

Add the component and theme namespaces to the consuming application's `_Imports.razor`:

```razor
@using Maliev.ShadcnBlazor.Components
@using Maliev.ShadcnBlazor.Theming
```

Then wrap the application content at its root:

```razor
<ShadcnThemeProvider>
    @Body
</ShadcnThemeProvider>
```

Set `IsDarkMode` or `Direction` on the provider when an application needs to override either
configured default dynamically.

Do not also render `MudThemeProvider`, `MudPopoverProvider`, `MudDialogProvider`, or `MudSnackbarProvider` in the same application root.

## MudBlazor version boundary

`Maliev.ShadcnBlazor` is built against MudBlazor **9.7.0** and its adapter selectors, state classes, and portal markup are supported only within the MudBlazor 9.7 line. Keep the consuming application on MudBlazor 9.7.x (the package pins 9.7.0); upgrading MudBlazor requires revalidating the adapter contracts and browser inventory before adoption.
