using WebReaper.Core.Blocking.Abstract;
using WebReaper.Core.Blocking.Concrete;
using Xunit;

namespace WebReaper.UnitTests;

// ADR-0083 slice 3: the pure confidence-split drop rule (part 8). The Crawl
// driver supplies the post-extraction record count; the policy never sees a
// page, only the verdict + count, so its behaviour is a closed truth table.
public class BlockDropPolicyTests
{
    [Theory]
    // High confidence drops regardless of record count — a confirmed challenge
    // (a challenge-class status or header) is not worth emitting.
    [InlineData(BlockConfidence.High, 0, true)]
    [InlineData(BlockConfidence.High, 1, true)]
    [InlineData(BlockConfidence.High, 25, true)]
    // Weak drops only when the page yielded nothing; a weak body-marker page
    // that still extracted real records was a false positive and is kept.
    [InlineData(BlockConfidence.Weak, 0, true)]
    [InlineData(BlockConfidence.Weak, 1, false)]
    [InlineData(BlockConfidence.Weak, 25, false)]
    // None is not a block, so never a drop, whatever the record count.
    [InlineData(BlockConfidence.None, 0, false)]
    [InlineData(BlockConfidence.None, 25, false)]
    public void ShouldDrop_follows_the_confidence_split(
        BlockConfidence confidence, int recordCount, bool expectedDrop)
    {
        var verdict = new BlockVerdict(
            confidence, confidence == BlockConfidence.None ? null : "test marker");

        Assert.Equal(expectedDrop, BlockDropPolicy.ShouldDrop(verdict, recordCount));
    }

    [Fact]
    public void ShouldDrop_throws_on_a_null_verdict()
    {
        Assert.Throws<ArgumentNullException>(() => BlockDropPolicy.ShouldDrop(null!, 0));
    }
}
