using Maliev.ShadcnBlazor.Theming;

namespace Maliev.ShadcnBlazor.Showcase;

public sealed class ShowcaseState
{
    public bool IsDarkMode { get; private set; }

    public ShadcnDirection Direction { get; private set; } = ShadcnDirection.LeftToRight;

    public event EventHandler? Changed;

    public void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleDirection()
    {
        Direction = Direction == ShadcnDirection.LeftToRight
            ? ShadcnDirection.RightToLeft
            : ShadcnDirection.LeftToRight;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
