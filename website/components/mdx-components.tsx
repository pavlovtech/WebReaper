import Link from "next/link";
import type { ComponentProps } from "react";

/** Shared components injected into every rendered MDX document. */
export const mdxComponents = {
  a: ({ href = "#", className, ...props }: ComponentProps<"a">) => {
    const isExternal = /^https?:\/\//.test(href);
    if (isExternal) {
      return (
        <a
          href={href}
          target="_blank"
          rel="noreferrer noopener"
          className={className}
          {...props}
        />
      );
    }
    return <Link href={href} className={className} {...props} />;
  },
};
