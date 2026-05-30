import type { NextConfig } from "next";
import withFlowbiteReact from "flowbite-react/plugin/nextjs";

// Build the Velite content layer in watch mode while the dev server runs so MDX
// edits hot-reload. Production builds run `velite` from the build script before
// `next build`, so we only trigger the watcher here for `next dev`.
if (process.argv.includes("dev") && !process.env.VELITE_STARTED) {
  process.env.VELITE_STARTED = "1";
  void import("velite").then((m) => m.build({ watch: true, clean: false }));
}

// Do NOT set `outputFileTracingRoot` to the website dir. The Vercel project's
// Root Directory is "website"; a subdir tracing root makes Vercel's build resolve
// `.next` one level too high and fail with ENOENT on
// routes-manifest-deterministic.json. Let Next/Vercel infer the root instead.
const nextConfig: NextConfig = {};

export default withFlowbiteReact(nextConfig);
