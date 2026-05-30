using WebReaper.Core.Blocking.Abstract;
using WebReaper.Core.Loaders.Concrete;
using Xunit;

namespace WebReaper.UnitTests;

// ADR-0083 slice 4: the per-host starting-tier memory. Only a high-confidence
// block lifts the floor; it never lowers; hosts are independent.
public class HostTierFloorTests
{
    [Fact]
    public void An_unseen_host_starts_at_tier_zero()
    {
        var floor = new HostTierFloor();
        Assert.Equal(0, floor.FloorFor("example.com"));
    }

    [Fact]
    public void A_high_confidence_block_lifts_the_floor()
    {
        var floor = new HostTierFloor();
        floor.Lift("example.com", 1, BlockConfidence.High);
        Assert.Equal(1, floor.FloorFor("example.com"));
    }

    [Theory]
    [InlineData(BlockConfidence.Weak)]
    [InlineData(BlockConfidence.None)]
    public void A_non_high_block_never_lifts_the_floor(BlockConfidence confidence)
    {
        var floor = new HostTierFloor();
        floor.Lift("example.com", 2, confidence);
        Assert.Equal(0, floor.FloorFor("example.com"));
    }

    [Fact]
    public void The_floor_never_lowers()
    {
        var floor = new HostTierFloor();
        floor.Lift("example.com", 2, BlockConfidence.High);
        floor.Lift("example.com", 1, BlockConfidence.High); // a lower lift is a no-op
        Assert.Equal(2, floor.FloorFor("example.com"));
    }

    [Fact]
    public void Hosts_are_independent()
    {
        var floor = new HostTierFloor();
        floor.Lift("blocked.com", 1, BlockConfidence.High);
        Assert.Equal(1, floor.FloorFor("blocked.com"));
        Assert.Equal(0, floor.FloorFor("clean.com"));
    }

    [Fact]
    public void Host_matching_is_case_insensitive()
    {
        var floor = new HostTierFloor();
        floor.Lift("Example.COM", 1, BlockConfidence.High);
        Assert.Equal(1, floor.FloorFor("example.com"));
    }
}
