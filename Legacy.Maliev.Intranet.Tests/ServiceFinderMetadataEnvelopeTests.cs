using System.Text.Json;
using Legacy.Maliev.Intranet.Client.Features.Quotations.Models;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class ServiceFinderMetadataEnvelopeTests
{
    [Theory]
    [MemberData(nameof(QuotationRequestFinderEnvelopeHttpTests.InvalidEnvelopes), MemberType = typeof(QuotationRequestFinderEnvelopeHttpTests))]
    public void InvalidOrAmbiguousInput_IsPlainNote(string original)
    {
        Assert.False(ServiceFinderMetadataEnvelope.TryRead(original, out var metadata));
        Assert.Null(metadata);
        Assert.Equal("staff note", ServiceFinderMetadataEnvelope.MergeOperatorComment(original, "  staff note  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ClearingNote_PreservesEntireContextWithoutOperatorComment(string? note)
    {
        var original = QuotationRequestFinderEnvelopeHttpTests.Envelope;
        var result = ServiceFinderMetadataEnvelope.MergeOperatorComment(original, note);
        Assert.True(ServiceFinderMetadataEnvelope.TryRead(result, out var metadata));
        Assert.Equal(string.Empty, metadata!.OperatorComment);
        using var before = JsonDocument.Parse(original);
        using var after = JsonDocument.Parse(result!);
        Assert.False(after.RootElement.TryGetProperty("operator_comment", out _));
        foreach (var property in before.RootElement.EnumerateObject().Where(property => property.Name != "operator_comment"))
            Assert.True(JsonElement.DeepEquals(property.Value, after.RootElement.GetProperty(property.Name)));
    }

    [Fact]
    public void AbsentOrEmptyStringOperatorNote_RemainsValidContext()
    {
        var original = QuotationRequestFinderEnvelopeHttpTests.Envelope;
        foreach (var value in new[]
        {
            original.Replace(",\"operator_comment\":\"original staff note\"", string.Empty, StringComparison.Ordinal),
            original.Replace("original staff note", string.Empty, StringComparison.Ordinal),
        })
        {
            Assert.True(ServiceFinderMetadataEnvelope.TryRead(value, out var metadata));
            Assert.Equal(string.Empty, metadata!.OperatorComment);
        }
    }

    [Fact]
    public void TypedContext_ExposesStableIdsNotExtensions()
    {
        Assert.True(ServiceFinderMetadataEnvelope.TryRead(QuotationRequestFinderEnvelopeHttpTests.Envelope, out var metadata));
        Assert.Equal(7, metadata!.Answers.Count);
        Assert.Equal("files-real-part", metadata.Answers["files"]);
        Assert.Equal("performance-strength", metadata.Answers["performance"]);
        Assert.Equal("environment-outdoor", metadata.Answers["environment"]);
        Assert.Equal(["scanning", "design"], metadata.RecommendedServiceIds);
        Assert.Equal(["scanning", "design"], metadata.FinderPath);
        Assert.Equal("original staff note", metadata.OperatorComment);
    }
}
