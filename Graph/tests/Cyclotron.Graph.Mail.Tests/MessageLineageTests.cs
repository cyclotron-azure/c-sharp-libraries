using Cyclotron.Graph.Mail.Models;

namespace Cyclotron.Graph.Mail.Tests;

public class MessageLineageTests
{
    [Theory]
    [InlineData("Re: Contract Review", "Contract Review")]
    [InlineData("RE:Contract Review", "Contract Review")]
    [InlineData("  Fwd:   Contract Review  ", "Contract Review")]
    [InlineData("Fw: Contract Review", "Contract Review")]
    [InlineData("Contract Review", "Contract Review")]
    [InlineData(null, "")]
    [InlineData("Fwd: RE: Renewal", "RE: Renewal")]
    [InlineData("Update: Re: x", "Update: Re: x")]
    public void NormalizeSubject_RawSubject_ReturnsExpectedNormalizedForm(string? subject, string expected)
    {
        var actual = MessageLineage.NormalizeSubject(subject);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Golden-value assertion. The expected bytes are SHA-256("Contract Review") truncated to its
    /// first 16 bytes, derived independently of this library and verifiable with
    /// <c>printf 'Contract Review' | sha256sum</c> → <c>895e197589703247baafca0e81d54573…</c>.
    /// Hard-coded deliberately: EmailTriage persists these hashes, so computing the expectation by
    /// calling the library would pin whatever the code does, bugs included, and a silent change
    /// would break lookups against existing rows.
    /// </summary>
    [Fact]
    public void ComputeSubjectHash_KnownInput_MatchesIndependentlyDerivedGoldenValue()
    {
        byte[] expected =
        [
            0x89, 0x5E, 0x19, 0x75, 0x89, 0x70, 0x32, 0x47,
            0xBA, 0xAF, 0xCA, 0x0E, 0x81, 0xD5, 0x45, 0x73,
        ];

        var actual = MessageLineage.ComputeSubjectHash("Contract Review");

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ComputeSubjectHash_AnyInput_ReturnsSixteenBytes()
    {
        var actual = MessageLineage.ComputeSubjectHash("Contract Review");

        Assert.Equal(16, actual.Length);
    }

    [Fact]
    public void ComputeSubjectHash_SameInputTwice_ReturnsEqualHashes()
    {
        var first = MessageLineage.ComputeSubjectHash("Quarterly Review");
        var second = MessageLineage.ComputeSubjectHash("Quarterly Review");

        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeSubjectHash_DifferentInputs_ReturnsDifferentHashes()
    {
        var first = MessageLineage.ComputeSubjectHash("Quarterly Review");
        var second = MessageLineage.ComputeSubjectHash("Contract Review");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ParseReferenceIds_MultipleIds_ReturnsMostRecentAncestorFirst()
    {
        var actual = MessageLineage.ParseReferenceIds("<a> <b> <c>");

        Assert.Equal(["<c>", "<b>", "<a>"], actual);
    }

    [Fact]
    public void ParseReferenceIds_SingleId_ReturnsThatIdAlone()
    {
        var actual = MessageLineage.ParseReferenceIds("<a>");

        Assert.Equal(["<a>"], actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void ParseReferenceIds_NullOrWhitespace_ReturnsEmptySequence(string? references)
    {
        var actual = MessageLineage.ParseReferenceIds(references);

        Assert.Empty(actual);
    }
}
