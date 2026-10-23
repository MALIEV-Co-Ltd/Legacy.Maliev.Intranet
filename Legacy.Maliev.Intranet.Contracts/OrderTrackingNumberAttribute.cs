using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Validates the qualified tracking-number storage boundary without changing the supplied value.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class OrderTrackingNumberAttribute : ValidationAttribute
{
    /// <summary>Creates a validation error without echoing the supplied tracking number.</summary>
    public OrderTrackingNumberAttribute() : base("Tracking number exceeds the supported length.") { }

    /// <summary>Accepts nullable text whose excess UTF-16 units are ordinary spaces.</summary>
    public override bool IsValid(object? value) => value is null || value is string text && Accepts(text);

    /// <summary>Preserves the first 250 UTF-16 units and permits only U+0020 beyond that boundary.</summary>
    public static bool Accepts(string? value)
    {
        if (value is null) return true;
        for (var index = 250; index < value.Length; index++)
        {
            if (value[index] != ' ') return false;
        }
        return true;
    }
}
