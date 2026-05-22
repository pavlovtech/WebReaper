using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Core.LinkTracker.Concrete;
using WebReaper.Core.Scheduler.Concrete;
using WebReaper.Domain;
using WebReaper.Domain.Selectors;

namespace WebReaper.UnitTests;

// ADR-0011 (#57): the three remaining file-backed adapters delegate directory
// creation, cleanup-on-start and the missing-file policy to
// FilePersistencePrep. These pin the migration through each adapter's PUBLIC
// interface — a path in a not-yet-existing directory now works, where the old
// per-adapter directory handling was conditional/divergent. Each adapter's own
// essence (the in-memory mirror, the resumable cursor/poll loop, the buffered
// drain) is untouched and covered by its existing tests / the kept FileSink
// drain tests; GetAllAsync's poll loop is deliberately not driven here.
public class FilePersistenceAdaptersTests
{
    private static string MissingNestedDir() =>
        Path.Combine(Path.GetTempPath(), $"wr-fpa-{Guid.NewGuid():N}", "nested");

    [Fact]
    public async Task FileVisitedLinkedTracker_in_a_missing_directory_initializes_and_round_trips()
    {
        var dir = MissingNestedDir();
        try
        {
            var tracker = new FileVisitedLinkedTracker(Path.Combine(dir, "visited.txt"));
            await tracker.InitializeAsync();

            await tracker.AddVisitedLinkAsync("http://x/1");

            Assert.Contains("http://x/1", await tracker.GetVisitedLinksAsync());
            Assert.Equal(1, await tracker.GetVisitedLinksCount());
        }
        finally
        {
            var root = Directory.GetParent(dir)!.FullName;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FileScheduler_in_a_missing_directory_initializes_and_AddAsync_succeeds()
    {
        var dir = MissingNestedDir();
        try
        {
            var scheduler = new FileScheduler(
                Path.Combine(dir, "jobs.txt"),
                Path.Combine(dir, "pos.txt"),
                NullLogger.Instance);
            await scheduler.InitializeAsync();

            await scheduler.AddAsync(new Job(
                "http://x/1",
                ImmutableQueue<LinkPathSelector>.Empty,
                ImmutableQueue<string>.Empty));

            Assert.True(File.Exists(Path.Combine(dir, "jobs.txt")));
        }
        finally
        {
            var root = Directory.GetParent(dir)!.FullName;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
