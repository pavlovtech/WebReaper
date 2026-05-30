import { ImageResponse } from "next/og";

export const alt = "WebReaper — AI-native web scraping for .NET";
export const size = { width: 1200, height: 630 };
export const contentType = "image/png";

export default function OpenGraphImage() {
  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          flexDirection: "column",
          justifyContent: "space-between",
          background:
            "radial-gradient(900px 500px at 50% -10%, rgba(52,211,153,0.22), transparent 70%), #08090a",
          padding: 80,
          fontFamily: "sans-serif",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 20 }}>
          <div
            style={{
              width: 56,
              height: 56,
              borderRadius: 14,
              display: "flex",
              background: "linear-gradient(135deg, #34d399, #059669)",
            }}
          />
          <div style={{ display: "flex", fontSize: 34, fontWeight: 600, color: "#ededed" }}>
            WebReaper
          </div>
        </div>

        <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <div
            style={{
              display: "flex",
              fontSize: 80,
              fontWeight: 700,
              color: "#ededed",
              letterSpacing: -2,
            }}
          >
            Scrape any site.
          </div>
          <div
            style={{
              display: "flex",
              fontSize: 80,
              fontWeight: 700,
              color: "#34d399",
              letterSpacing: -2,
            }}
          >
            Feed your AI.
          </div>
          <div
            style={{
              display: "flex",
              marginTop: 10,
              fontSize: 30,
              color: "#9aa3ad",
            }}
          >
            AI-native web scraping for .NET. One binary, MIT licensed.
          </div>
        </div>

        <div style={{ display: "flex", fontSize: 24, color: "#9aa3ad" }}>
          github.com/pavlovtech/WebReaper
        </div>
      </div>
    ),
    { ...size },
  );
}
