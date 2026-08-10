using System.Text;

namespace Maliev.ShadcnBlazor.BrowserTests.Infrastructure;

internal sealed class BoundedDiagnostics
{
    private readonly int _maximumLength;
    private readonly StringBuilder _contents = new();

    public BoundedDiagnostics(int maximumLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);
        _maximumLength = maximumLength;
    }

    public void Append(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return;

        if (value.Length >= _maximumLength)
        {
            _contents.Clear();
            _contents.Append(value[^_maximumLength..]);
            return;
        }

        var overflow = _contents.Length + value.Length - _maximumLength;
        if (overflow > 0)
            _contents.Remove(0, overflow);

        _contents.Append(value);
    }

    public override string ToString() => _contents.ToString();
}
