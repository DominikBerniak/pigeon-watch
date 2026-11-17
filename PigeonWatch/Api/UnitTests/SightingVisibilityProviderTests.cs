using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;

namespace PigeonWatch.UnitTests;

public class SightingVisibilityProviderTests
{
    private static readonly DateTime removedAt = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);

    private readonly SightingVisibilityProvider provider = new();
    private readonly Guid reporterId = Guid.NewGuid();

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, true)]
    public void A_removed_sighting_is_visible_only_to_its_reporter_and_an_admin(bool removed, bool viewerIsReporter, bool viewerIsAdmin, bool expected)
    {
        SightingAccess access = new(reporterId, removed ? removedAt : null);
        Guid viewerId = viewerIsReporter ? reporterId : Guid.NewGuid();

        Assert.Equal(expected, provider.CanView(access, viewerId, viewerIsAdmin));
    }

    [Fact]
    public void A_sighting_is_removed_only_while_it_has_a_removal_time()
    {
        Assert.True(provider.IsRemoved(new SightingAccess(reporterId, removedAt)));
        Assert.False(provider.IsRemoved(new SightingAccess(reporterId, null)));
    }
}
