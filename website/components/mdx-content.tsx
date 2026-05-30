import { useMemo } from "react";
import * as runtime from "react/jsx-runtime";
import type { ComponentType } from "react";
import { mdxComponents } from "@/components/mdx-components";

// MDX component maps are heterogeneous (each tag has its own prop shape).
// eslint-disable-next-line @typescript-eslint/no-explicit-any
type MDXComponentMap = Record<string, ComponentType<any>>;

// Velite compiles each MDX document to a function body string. Turning that
// back into a component necessarily happens at runtime; we memoize on `code`
// so the component identity is stable across re-renders (no remount/state loss).
function compileMDX(code: string) {
  const fn = new Function(code);
  return fn({ ...runtime }).default as ComponentType<{
    components?: MDXComponentMap;
  }>;
}

export function MDXContent({
  code,
  components,
}: {
  code: string;
  components?: MDXComponentMap;
}) {
  const Component = useMemo(() => compileMDX(code), [code]);
  // The component is built from trusted build-time content and memoized on
  // `code`, so its identity is stable; the static-components rule can't prove
  // that for a runtime-compiled component.
  // eslint-disable-next-line react-hooks/static-components
  return <Component components={{ ...mdxComponents, ...components }} />;
}
