using Giim.Domain.People;

namespace Giim.Domain.Tests;

public class LegacyOwnerMatcherTests
{
    private static readonly OwnerCandidate GraceA = new(Guid.NewGuid(), "Grace Brown", "grace.brown@giim-test.local");
    private static readonly OwnerCandidate GraceB = new(Guid.NewGuid(), "Grace Brown", "grace.brown2@giim-test.local");
    private static readonly OwnerCandidate Liam = new(Guid.NewGuid(), "Liam O'Brien", "liam.obrien@giim-test.local");
    private static readonly OwnerCandidate[] People = [GraceA, GraceB, Liam];

    private static OwnerMatch One(string owner, string? intuneUpn = null) =>
        Assert.Single(LegacyOwnerMatcher.Match([new LegacyOwnedAsset(Guid.NewGuid(), owner, intuneUpn)], People));

    [Theory]
    [InlineData("Liam O'Brien")]
    [InlineData("liam obrien")]
    [InlineData("O'Brien, Liam")]
    [InlineData("  LIAM   O'BRIEN ")]
    public void Unique_name_matches_in_any_format(string owner)
    {
        var match = One(owner);

        Assert.Equal(OwnerMatchOutcome.Matched, match.Outcome);
        Assert.Equal(Liam.PersonId, match.PersonId);
        Assert.Equal("name", match.MatchedBy);
    }

    [Fact]
    public void Shared_name_is_never_guessed()
    {
        var match = One("Grace Brown");

        Assert.Equal(OwnerMatchOutcome.Ambiguous, match.Outcome);
        Assert.Null(match.PersonId);
        Assert.Equal([GraceA.PersonId, GraceB.PersonId], match.CandidateIds);
    }

    [Fact]
    public void Intune_user_breaks_a_tie_between_shared_names()
    {
        var match = One("Grace Brown", intuneUpn: "GRACE.BROWN2@giim-test.local");

        Assert.Equal(OwnerMatchOutcome.Matched, match.Outcome);
        Assert.Equal(GraceB.PersonId, match.PersonId);
        Assert.Equal("name and Intune user", match.MatchedBy);
    }

    [Theory]
    [InlineData("FIN")]
    [InlineData("Finance")]
    [InlineData(" finance ")]
    public void Department_breaks_a_tie_when_intune_cannot(string department)
    {
        var finance = new OwnerCandidate(Guid.NewGuid(), "Aisha Anderson", "aisha.anderson@x", "FIN", "Finance");
        var logistics = new OwnerCandidate(Guid.NewGuid(), "Aisha Anderson", "aisha.anderson2@x", "LOG", "Logistics");

        var match = Assert.Single(LegacyOwnerMatcher.Match(
            [new LegacyOwnedAsset(Guid.NewGuid(), "Aisha Anderson", null, department)], [finance, logistics]));

        Assert.Equal(finance.PersonId, match.PersonId);
        Assert.Equal("name and department", match.MatchedBy);
    }

    [Fact]
    public void Same_name_in_the_same_department_stays_ambiguous()
    {
        var a = new OwnerCandidate(Guid.NewGuid(), "Jack Lee", "jack.lee@x", "IT", "Information Technology");
        var b = new OwnerCandidate(Guid.NewGuid(), "Jack Lee", "jack.lee2@x", "IT", "Information Technology");

        var match = Assert.Single(LegacyOwnerMatcher.Match([new LegacyOwnedAsset(Guid.NewGuid(), "Jack Lee", null, "IT")], [a, b]));

        Assert.Equal(OwnerMatchOutcome.Ambiguous, match.Outcome);
    }

    [Fact]
    public void Intune_user_wins_over_department()
    {
        var a = new OwnerCandidate(Guid.NewGuid(), "Jack Lee", "jack.lee@x", "IT", "Information Technology");
        var b = new OwnerCandidate(Guid.NewGuid(), "Jack Lee", "jack.lee2@x", "FIN", "Finance");

        // The register says IT, but Intune shows the Finance Jack Lee using it: Intune reflects reality.
        var match = Assert.Single(LegacyOwnerMatcher.Match([new LegacyOwnedAsset(Guid.NewGuid(), "Jack Lee", "jack.lee2@x", "IT")], [a, b]));

        Assert.Equal(b.PersonId, match.PersonId);
    }

    [Fact]
    public void Intune_user_does_not_override_a_different_name()
    {
        var match = One("Jack Smith", intuneUpn: "liam.obrien@giim-test.local");

        Assert.Equal(OwnerMatchOutcome.NotFound, match.Outcome);
        Assert.Null(match.PersonId);
        Assert.Equal([Liam.PersonId], match.CandidateIds);  // offered as a suggestion only
    }

    [Fact]
    public void Email_address_in_the_register_matches_the_upn()
    {
        var match = One("grace.brown2@giim-test.local");

        Assert.Equal(GraceB.PersonId, match.PersonId);
    }
}
