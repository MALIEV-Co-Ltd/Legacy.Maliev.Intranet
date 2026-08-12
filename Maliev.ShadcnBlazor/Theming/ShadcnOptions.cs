namespace Maliev.ShadcnBlazor.Theming;

public sealed class ShadcnOptions
{
    public string FontFamily { get; set; } = "ui-sans-serif, system-ui, sans-serif";
    public bool DefaultDarkMode { get; set; }
    public ShadcnDirection DefaultDirection { get; set; } = ShadcnDirection.LeftToRight;
}
