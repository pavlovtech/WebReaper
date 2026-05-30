import * as runtime from "react/jsx-runtime";
import type { ComponentType } from "react";
import { mdxComponents } from "@/components/mdx-components";

// MDX component maps are heterogeneous (each tag has its own prop shape).
// eslint-disable-next-line @typescript-eslint/no-explicit-any
type MDXComponentMap = Record<string, ComponentType<any>>;

const useMDXComponent = (code: string) => {
  // eslint-disable-next-line @typescript-eslint/no-implied-eval, no-new-func
  const fn = new Function(code);
  return fn({ ...runtime }).default as ComponentType<{
    components?: MDXComponentMap;
  }>;
};

export function MDXContent({
  code,
  components,
}: {
  code: string;
  components?: MDXComponentMap;
}) {
  const Component = useMDXComponent(code);
  return <Component components={{ ...mdxComponents, ...components }} />;
}
