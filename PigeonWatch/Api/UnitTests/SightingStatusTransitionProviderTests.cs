using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;

namespace PigeonWatch.UnitTests;

public class SightingStatusTransitionProviderTests
{
    private readonly ISightingStatusTransitionProvider provider = new SightingStatusTransitionProvider();

    public static TheoryData<SightingStatus, SightingStatus[]> PrdTransitions => new()
    {
        { SightingStatus.Spotted, [SightingStatus.Caught, SightingStatus.NotFound] },
        { SightingStatus.Caught, [SightingStatus.AtTheVet, SightingStatus.InHomeCare, SightingStatus.Released] },
        { SightingStatus.AtTheVet, [SightingStatus.InHomeCare, SightingStatus.AwaitingAdoption, SightingStatus.Released, SightingStatus.PassedAway] },
        { SightingStatus.InHomeCare, [SightingStatus.AtTheVet, SightingStatus.AwaitingAdoption, SightingStatus.Released] },
        { SightingStatus.AwaitingAdoption, [SightingStatus.Adopted, SightingStatus.AtTheVet] },
        { SightingStatus.Adopted, [] },
        { SightingStatus.Released, [] },
        { SightingStatus.PassedAway, [] },
        { SightingStatus.NotFound, [] }
    };

    [Theory]
    [MemberData(nameof(PrdTransitions))]
    public void Allowed_next_statuses_match_the_prd_table(SightingStatus from, SightingStatus[] expected)
    {
        IReadOnlyList<SightingStatus> allowed = provider.GetAllowedNextStatuses(from);

        Assert.Equal(expected.Order(), allowed.Order());
    }

    [Theory]
    [MemberData(nameof(PrdTransitions))]
    public void Every_status_pair_is_allowed_only_when_the_prd_table_lists_it(SightingStatus from, SightingStatus[] expected)
    {
        foreach (SightingStatus to in Enum.GetValues<SightingStatus>())
            Assert.Equal(expected.Contains(to), provider.IsTransitionAllowed(from, to));
    }

    [Theory]
    [InlineData(SightingStatus.Spotted, SightingStatus.AtTheVet)]
    [InlineData(SightingStatus.Caught, SightingStatus.Spotted)]
    [InlineData(SightingStatus.AtTheVet, SightingStatus.Adopted)]
    [InlineData(SightingStatus.InHomeCare, SightingStatus.PassedAway)]
    [InlineData(SightingStatus.AwaitingAdoption, SightingStatus.Released)]
    [InlineData(SightingStatus.Adopted, SightingStatus.AwaitingAdoption)]
    [InlineData(SightingStatus.Released, SightingStatus.Caught)]
    [InlineData(SightingStatus.PassedAway, SightingStatus.AtTheVet)]
    [InlineData(SightingStatus.NotFound, SightingStatus.Spotted)]
    public void Transitions_outside_the_prd_table_are_not_allowed(SightingStatus from, SightingStatus to)
    {
        Assert.False(provider.IsTransitionAllowed(from, to));
    }

    [Theory]
    [MemberData(nameof(PrdTransitions))]
    public void A_status_cannot_transition_to_itself(SightingStatus status, SightingStatus[] expected)
    {
        Assert.DoesNotContain(status, expected);
        Assert.False(provider.IsTransitionAllowed(status, status));
    }

    [Fact]
    public void Exactly_the_four_end_states_are_resolved()
    {
        List<SightingStatus> resolved = Enum.GetValues<SightingStatus>().Where(provider.IsResolved).ToList();

        Assert.Equal(
            [SightingStatus.Adopted, SightingStatus.Released, SightingStatus.PassedAway, SightingStatus.NotFound],
            resolved);
    }

    [Fact]
    public void Resolved_statuses_have_no_next_status()
    {
        foreach (SightingStatus status in Enum.GetValues<SightingStatus>().Where(provider.IsResolved))
            Assert.Empty(provider.GetAllowedNextStatuses(status));
    }

    [Fact]
    public void Only_spotted_and_caught_are_valid_initial_statuses()
    {
        List<SightingStatus> initial = Enum.GetValues<SightingStatus>().Where(provider.IsValidInitialStatus).ToList();

        Assert.Equal([SightingStatus.Spotted, SightingStatus.Caught], initial);
    }

    [Fact]
    public void Undefined_status_values_are_neither_initial_nor_resolved_and_have_no_next_status()
    {
        SightingStatus undefined = (SightingStatus)0;

        Assert.False(provider.IsValidInitialStatus(undefined));
        Assert.False(provider.IsResolved(undefined));
        Assert.Empty(provider.GetAllowedNextStatuses(undefined));
    }

    [Theory]
    [InlineData(SightingCriticality.Monitor, 1)]
    [InlineData(SightingCriticality.NeedsHelpSoon, 2)]
    [InlineData(SightingCriticality.Urgent, 3)]
    public void Criticality_rank_orders_levels_from_least_to_most_urgent(SightingCriticality criticality, int expectedRank)
    {
        Assert.Equal(expectedRank, provider.GetCriticalityRank(criticality));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Criticality_rank_rejects_undefined_levels(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => provider.GetCriticalityRank((SightingCriticality)value));
    }
}
