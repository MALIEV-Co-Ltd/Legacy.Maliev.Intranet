# Maliev.ShadcnBlazor

Reusable Shadcn Base/Vega/Neutral components for .NET 10 Blazor, backed by MudBlazor 9.7.0.

## Register

```csharp
builder.Services.AddMalievShadcn(options =>
    options.FontFamily = "IBM Plex Sans Thai, sans-serif");
```

## Load assets

```html
<link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />
<link href="_content/Maliev.ShadcnBlazor/css/shadcn-base.css" rel="stylesheet" />
<script src="_content/MudBlazor/MudBlazor.min.js"></script>
```

## Provide theme and portals

```razor
<ShadcnThemeProvider IsDarkMode="@isDarkMode" Direction="ShadcnDirection.LeftToRight">
    @Body
</ShadcnThemeProvider>
```

Do not also render `MudThemeProvider`, `MudPopoverProvider`, `MudDialogProvider`, or `MudSnackbarProvider` in the same application root.
