using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Client.Shared.Infrastructure;

/// <summary>Maps reviewed suggestions to the existing supplier-owned address contract.</summary>
public static class LookupSupplierSuggestions
{
    public static void ApplyAddress(SupplierCreateRequest target, LookupAddressSelection value)
    {
        // City carries the subdistrict and district because the legacy address has no separate fields.
        target.State = value.Province?.NameTh;
        target.City = string.Join(", ", new[] { value.Subdistrict?.NameTh, value.District?.NameTh }
            .Where(name => !string.IsNullOrWhiteSpace(name)));
        target.PostalCode = value.Postcode;
        // Building, Address1, Address2 and CountryId remain the owner's editable detail.
    }

    public static void ApplyCompany(SupplierCreateRequest target, LookupCompany value, bool thai)
    {
        var name = thai ? value.NameTh ?? value.NameEn : value.NameEn ?? value.NameTh;
        if (!string.IsNullOrWhiteSpace(name)) target.Name = name;
        if (!string.IsNullOrWhiteSpace(value.TaxId)) target.TaxNumber = value.TaxId;
        // A suggestion provides no verified website, contact, status, or registered address.
    }
}
