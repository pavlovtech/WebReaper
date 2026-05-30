import type { NextConfig } from "next";
import withFlowbiteReact from "flowbite-react/plugin/nextjs";

// Build the Velite content layer in watch mode while the dev server runs so MDX
// edits hot-reload. Production builds run `velite` from the build script before
// `next build`, so we only trigger the watcher here for `next dev`.
if (process.argv.includes("dev") && !process.env.VELITE_STARTED) {
  process.env.VELITE_STARTED = "1";
  void import("velite").then((m) => m.build({ watch: true, clean: false }));
}

const nextConfig: NextConfig = {
  outputFileTracingRoot: __dirname,
};

export default withFlowbiteReact(nextConfig);
