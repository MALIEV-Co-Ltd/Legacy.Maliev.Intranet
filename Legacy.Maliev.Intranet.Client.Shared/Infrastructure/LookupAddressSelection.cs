using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Client.Shared.Infrastructure;

/// <summary>Administrative selections are suggestions; they never save or overwrite street detail.</summary>
public sealed record LookupAddressSelection(LookupArea? Province = null, LookupArea? District = null,
    LookupArea? Subdistrict = null, string? Postcode = null)
{
    public LookupAddressConstraints Constraints => new(Province?.Code, District?.Code, Subdistrict?.Code, Postcode);
    public LookupAddressSelection WithProvince(LookupArea? value) => Province?.Code == value?.Code ? this : new(value);
    public LookupAddressSelection WithDistrict(LookupArea? value)
    {
        if (value is not null && value.ParentCode != Province?.Code) throw new ArgumentException("Incompatible district.");
        return District?.Code == value?.Code ? this : this with { District = value, Subdistrict = null, Postcode = null };
    }
    public LookupAddressSelection WithSubdistrict(LookupArea? value)
    {
        if (value is not null && value.ParentCode != District?.Code) throw new ArgumentException("Incompatible subdistrict.");
        return Subdistrict?.Code == value?.Code ? this : this with { Subdistrict = value, Postcode = null };
    }
    public LookupAddressSelection WithPostcode(string? value) => Postcode == value ? this : new(Postcode: value);
    public static LookupAddressSelection From(LookupAddressCombination value) =>
        new(value.Province, value.District, value.Subdistrict, value.Postcode);
}
