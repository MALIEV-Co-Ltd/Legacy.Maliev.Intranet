using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Employee-only customer remark returned exclusively through the authenticated Intranet BFF.</summary>
public sealed record CustomerInternalRemarkResponse(int CustomerId, string? InternalRemark);

/// <summary>Bounded employee-only customer remark replacement.</summary>
public sealed class CustomerInternalRemarkUpdateRequest
{
    /// <summary>Gets or sets the internal remark; blank input clears the stored value.</summary>
    [StringLength(4000)]
    public string? InternalRemark { get; set; }
}
