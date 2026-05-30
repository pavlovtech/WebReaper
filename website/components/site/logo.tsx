import Image from "next/image";
import { cn } from "@/lib/utils";

/** Official WebReaper mark (the logo.png shipped on every NuGet package + README). */
export function Logo({
  className,
  showWordmark = true,
}: {
  className?: string;
  showWordmark?: boolean;
}) {
  return (
    <span className={cn("inline-flex items-center gap-2.5", className)}>
      <Image
        src="/webreaper-logo.png"
        alt="WebReaper"
        width={32}
        height={32}
        className="h-8 w-8 rounded-lg"
        priority
      />
      {showWordmark ? (
        <span className="text-[15px] font-semibold tracking-tight text-foreground">
          WebReaper
        </span>
      ) : null}
    </span>
  );
}
