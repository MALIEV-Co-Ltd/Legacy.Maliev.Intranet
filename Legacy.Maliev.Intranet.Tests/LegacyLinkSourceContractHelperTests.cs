namespace Legacy.Maliev.Intranet.Tests;

public sealed class LegacyLinkSourceContractHelperTests
{
    [Fact]
    public void SpecializedOwnerAllowlist_RejectsClassSpoofingAndArbitraryFilledButtons()
    {
        Assert.True(LegacyLinkSourceContracts.IsSpecializedOwner(
            "Legacy.Maliev.Intranet.Client/Layout/MainLayout.razor",
            "RawAnchor",
            "<a class=\"legacy-skip-link\" href=\"#main-content\">@Text[\"Skip to content\"]</a>"));
        Assert.False(LegacyLinkSourceContracts.IsSpecializedOwner(
            "Legacy.Maliev.Intranet.Client.Features.Customers/Pages/Customers.razor",
            "RawAnchor",
            "<a class=\"legacy-skip-link\" href=\"/Customers/Fake\">Fake</a>"));
        Assert.False(LegacyLinkSourceContracts.IsSpecializedOwner(
            "Legacy.Maliev.Intranet.Client.Features.Customers/Pages/Customers.razor",
            "MudButton",
            "<MudButton Href=\"/Customers/Fake\" Variant=\"Variant.Filled\">Fake</MudButton>"));
    }

    [Fact]
    public void RecordAccessibleNameValidation_RequiresTheSameLinksRecordContext()
    {
        const string associatedRecord = """
            <LegacyLink Href="@($"/Customers/View?id={context.Id}")"
                        Role="LegacyLinkRole.Record"
                        AriaLabel="@($"{Text["Customer"]} {context.Id}")">@Text["View"]</LegacyLink>
            """;
        const string genericRecord = """
            <LegacyLink Href="@($"/Customers/View?id={context.Id}")"
                        Role="LegacyLinkRole.Record"
                        AriaLabel="@Text["Id"]">@Text["View"]</LegacyLink>
            """;
        const string hardCodedIdBearingRecord = """
            <LegacyLink Href="@($"/Customers/View?id={context.Id}")"
                        Role="LegacyLinkRole.Record"
                        AriaLabel="@($"Customer {context.Id}")">@context.Id</LegacyLink>
            """;
        const string detachedBuilderLabel = """
            builder.OpenComponent<LegacyLink>(1);
            builder.AddAttribute(2, nameof(LegacyLink.Href), $"/Customers/View?id={id}");
            builder.AddAttribute(3, nameof(LegacyLink.Role), LegacyLinkRole.Record);
            builder.CloseComponent();
            builder.AddAttribute(4, nameof(LegacyLink.AriaLabel), $"Customer {id}");
            """;

        Assert.Empty(LegacyLinkSourceContracts.FindRecordAccessibleNameViolations("Customers.razor", associatedRecord));
        Assert.NotEmpty(LegacyLinkSourceContracts.FindRecordAccessibleNameViolations("Customers.razor", genericRecord));
        Assert.NotEmpty(LegacyLinkSourceContracts.FindRecordAccessibleNameViolations("Customers.razor", hardCodedIdBearingRecord));
        Assert.NotEmpty(LegacyLinkSourceContracts.FindRecordAccessibleNameViolations("Quotations.razor", detachedBuilderLabel));
    }

    [Fact]
    public void StructuredLinkMatching_RejectsAttributesScatteredAcrossDifferentOwners()
    {
        const string source = """
            <LegacyLink Href="/Customers/Index" Role="LegacyLinkRole.Inline">Customers</LegacyLink>
            <LegacyLink Href="/Elsewhere" Role="LegacyLinkRole.Navigation" Disabled="submitting">Elsewhere</LegacyLink>
            """;

        Assert.True(LegacyLinkSourceContracts.MatchesExpectedLink(
            source,
            "Href=\"/Customers/Index\"",
            "Role=\"LegacyLinkRole.Inline\""));
        Assert.False(LegacyLinkSourceContracts.MatchesExpectedLink(
            source,
            "Href=\"/Customers/Index\"",
            "Role=\"LegacyLinkRole.Navigation\"",
            "Disabled=\"submitting\""));
    }

    [Fact]
    public void StructuredLinkMatching_RejectsAnIdBearingLocalizationMutation()
    {
        const string localized = """
            <LegacyLink Href="@quotation.NavigateTo"
                        Role="LegacyLinkRole.Record"
                        AriaLabel="@($"{Text["Quote"]} #{quotation.Id}")">#@quotation.Id</LegacyLink>
            """;
        var hardCoded = localized.Replace(
            "@($\"{Text[\"Quote\"]} #{quotation.Id}\")",
            "@($\"Quotation #{quotation.Id}\")",
            StringComparison.Ordinal);
        string[] frozenLocalization =
        [
            "AriaLabel=\"@($\"{Text[\"Quote\"]} #{quotation.Id}\")\"",
            "#@quotation.Id</LegacyLink>",
        ];

        Assert.True(LegacyLinkSourceContracts.MatchesExpectedLink(
            localized,
            "Href=\"@quotation.NavigateTo\"",
            frozenLocalization));
        Assert.False(LegacyLinkSourceContracts.MatchesExpectedLink(
            hardCoded,
            "Href=\"@quotation.NavigateTo\"",
            frozenLocalization));
    }
}
