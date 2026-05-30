using System.Collections.Generic;
using WebReaper.Core.Blocking.Abstract;
using WebReaper.Core.Blocking.Concrete;
using WebReaper.Core.Loaders.Abstract;

namespace WebReaper.UnitTests;

/// <summary>
/// ADR-0083 slice 1 core classifier. The default <see cref="BlockDetector"/> is
/// a pure function over a <see cref="PageLoadResult"/> (status, headers, body).
/// High = challenge-class status or a challenge header; Weak = a body marker;
/// None = a clean page. Record count is deliberately NOT an input. Prior art:
/// <c>BotCheckDetectorTests</c> (the ADR-0056 CLI ancestor this ports).
/// </summary>
public class BlockDetectorTests
{
    private static readonly BlockDetector Sut = new();

    private static PageLoadResult Page(
        string html = "<html><body>ok</body></html>",
        int? status = 200,
        params (string Key, string Value)[] headers)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in headers) dict[k] = v;
        return new PageLoadResult { Html = html, HttpStatus = status, Headers = dict };
    }

    // ---- High: challenge-class HTTP status ----

    [Theory]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(503)]
    public void Challenge_class_status_is_High(int status)
    {
        var v = Sut.Detect(Page(status: status));

        Assert.Equal(BlockConfidence.High, v.Confidence);
        Assert.True(v.IsBlocked);
    }

    // ---- High: challenge-signalling response header ----

    [Fact]
    public void Challenge_header_on_a_clean_200_is_High()
    {
        // cf-mitigated is the curated challenge header; 200 status + clean body
        // would otherwise be None, so the verdict must come from the header.
        var v = Sut.Detect(Page(status: 200, headers: ("cf-mitigated", "challenge")));

        Assert.Equal(BlockConfidence.High, v.Confidence);
        Assert.True(v.IsBlocked);
    }

    // ---- Weak: body marker ----

    public static IEnumerable<object[]> WeakBodyMarkers =>
        BlockDetector.WeakBodyMarkers.Select(m => new object[] { m });

    [Theory]
    [MemberData(nameof(WeakBodyMarkers))]
    public void Body_marker_on_a_clean_200_is_Weak(string marker)
    {
        var html = $"<html><body><div>{marker}</div></body></html>";
        var v = Sut.Detect(Page(html: html, status: 200));

        Assert.Equal(BlockConfidence.Weak, v.Confidence);
        Assert.True(v.IsBlocked);
    }

    // ---- None: clean page ----

    [Fact]
    public void Clean_200_with_no_markers_or_headers_is_None()
    {
        var v = Sut.Detect(Page(html: "<html><body><h1>Hello</h1></body></html>", status: 200));

        Assert.Equal(BlockConfidence.None, v.Confidence);
        Assert.False(v.IsBlocked);
    }

    // ---- None: bare vendor names were deliberately dropped (ADR-0083) ----

    [Theory]
    [InlineData("DataDome")]
    [InlineData("Akamai")]
    public void Bare_vendor_name_without_a_structural_marker_is_None(string vendor)
    {
        // "DataDome" / "Akamai" appear in legitimate content; the marker list
        // was tightened to drop them. A page that merely mentions the vendor
        // (no structural marker like dd-rd / ak_bmsc / /akam/) is not a block.
        var html = $"<html><body><p>Protected by {vendor}.</p></body></html>";
        var v = Sut.Detect(Page(html: html, status: 200));

        Assert.Equal(BlockConfidence.None, v.Confidence);
        Assert.False(v.IsBlocked);
    }

    // ---- None: WAF-presence header is not a block ----

    [Fact]
    public void WAF_presence_header_on_a_clean_200_is_None()
    {
        // cf-ray is on ALL Cloudflare traffic, blocked or not, so it is
        // deliberately excluded from the challenge-header set; it must not fire.
        var v = Sut.Detect(Page(status: 200, headers: ("cf-ray", "7a1b2c3d4e5f6789")));

        Assert.Equal(BlockConfidence.None, v.Confidence);
        Assert.False(v.IsBlocked);
    }
}
