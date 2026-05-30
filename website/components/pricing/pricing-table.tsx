"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { Check, X } from "lucide-react";
import { plans, type Plan } from "@/lib/pricing";
import { siteConfig } from "@/lib/site";
import { cn } from "@/lib/utils";

function priceLabel(plan: Plan, annual: boolean) {
  if (plan.customPrice) return plan.customPrice;
  const value = annual ? plan.priceAnnual : plan.priceMonthly;
  if (value === 0) return "Free";
  if (value === null) return "Custom";
  return `$${value}`;
}

export function PricingTable() {
  const [annual, setAnnual] = useState(true);

  return (
    <div>
      <div className="flex items-center justify-center gap-3">
        <span
          className={cn(
            "text-sm",
            !annual ? "text-foreground" : "text-muted",
          )}
        >
          Monthly
        </span>
        <button
          type="button"
          role="switch"
          aria-checked={annual}
          aria-label="Toggle annual billing"
          onClick={() => setAnnual((v) => !v)}
          className={cn(
            "relative h-6 w-11 rounded-full border transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/70",
            annual ? "border-accent/50 bg-accent/20" : "border-border bg-surface-2",
          )}
        >
          <span
            className={cn(
              "absolute left-[2px] top-[2px] h-[18px] w-[18px] rounded-full bg-accent transition-transform duration-200",
              annual ? "translate-x-[20px]" : "translate-x-0",
            )}
          />
        </button>
        <span
          className={cn("text-sm", annual ? "text-foreground" : "text-muted")}
        >
          Annual
          <span className="ml-1.5 rounded-full bg-accent/15 px-2 py-0.5 text-xs font-medium text-accent">
            Save ~20%
          </span>
        </span>
      </div>

      <div className="mt-12 grid gap-5 lg:grid-cols-3">
        {plans.map((plan) => (
          <PlanCard key={plan.id} plan={plan} annual={annual} />
        ))}
      </div>

      <p className="mt-8 text-center text-sm text-muted-2">
        Cloud is in early access. Prices shown are indicative and finalize at
        launch. No credit card required to join the waitlist.
      </p>
    </div>
  );
}

function PlanCard({ plan, annual }: { plan: Plan; annual: boolean }) {
  return (
    <div
      className={cn(
        "surface-card relative flex flex-col p-7",
        plan.featured && "ring-1 ring-accent/50",
      )}
    >
      {plan.badge ? (
        <span className="absolute -top-3 left-7 rounded-full bg-accent px-3 py-1 text-xs font-medium text-accent-foreground">
          {plan.badge}
        </span>
      ) : null}

      <h3 className="text-lg font-semibold">{plan.name}</h3>
      <div className="mt-3 flex items-baseline gap-1.5">
        <span className="text-4xl font-semibold tracking-tight">
          {priceLabel(plan, annual)}
        </span>
        {plan.priceMonthly && plan.priceMonthly > 0 ? (
          <span className="text-sm text-muted">/mo</span>
        ) : null}
      </div>
      <p className="mt-3 text-sm leading-relaxed text-muted">{plan.tagline}</p>

      <PlanCta plan={plan} />

      <ul className="mt-7 space-y-3">
        {plan.features.map((feature) => (
          <li key={feature} className="flex items-start gap-2.5 text-sm">
            <Check className="mt-0.5 h-4 w-4 shrink-0 text-accent" />
            <span className="text-muted">{feature}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function PlanCta({ plan }: { plan: Plan }) {
  const base =
    "mt-6 inline-flex h-11 w-full items-center justify-center gap-2 rounded-lg px-5 text-sm font-medium transition-all";
  const primary =
    "bg-accent text-accent-foreground hover:bg-accent-strong ring-1 ring-inset ring-white/10";
  const secondary =
    "border border-border-strong bg-surface text-foreground hover:border-accent/50 hover:bg-surface-2";

  if (plan.action === "install") {
    return (
      <a href={plan.href} className={cn(base, secondary)}>
        {plan.cta}
      </a>
    );
  }
  if (plan.action === "contact") {
    return (
      <a
        href={`mailto:${siteConfig.contactEmail}?subject=WebReaper%20Enterprise`}
        className={cn(base, secondary)}
      >
        {plan.cta}
      </a>
    );
  }
  return <SubscribeButton plan={plan} className={cn(base, primary)} />;
}

function SubscribeButton({
  plan,
  className,
}: {
  plan: Plan;
  className?: string;
}) {
  const [open, setOpen] = useState(false);
  const [email, setEmail] = useState("");
  const [status, setStatus] = useState<"idle" | "loading" | "done" | "error">(
    "idle",
  );
  const [message, setMessage] = useState("");
  const triggerRef = useRef<HTMLButtonElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);

  const closeModal = useCallback(() => {
    setOpen(false);
    triggerRef.current?.focus();
  }, []);

  // Escape to close, focus the email field on open, lock body scroll.
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") closeModal();
    };
    window.addEventListener("keydown", onKey);
    const t = setTimeout(() => inputRef.current?.focus(), 10);
    const prevOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      window.removeEventListener("keydown", onKey);
      clearTimeout(t);
      document.body.style.overflow = prevOverflow;
    };
  }, [open, closeModal]);

  function onPanelKeyDown(e: React.KeyboardEvent) {
    if (e.key !== "Tab") return;
    const focusables = panelRef.current?.querySelectorAll<HTMLElement>(
      'a[href], button:not([disabled]), input, [tabindex]:not([tabindex="-1"])',
    );
    if (!focusables || focusables.length === 0) return;
    const first = focusables[0];
    const last = focusables[focusables.length - 1];
    if (e.shiftKey && document.activeElement === first) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault();
      first.focus();
    }
  }

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setStatus("loading");
    try {
      const res = await fetch("/api/checkout", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ plan: plan.id, email }),
      });
      const data = await res.json();
      if (data.redirect) {
        window.location.href = data.redirect;
        return;
      }
      if (!res.ok) {
        setStatus("error");
        setMessage(data.message ?? "Something went wrong.");
        return;
      }
      setStatus("done");
      setMessage(data.message ?? "You're on the list.");
    } catch {
      setStatus("error");
      setMessage("Network error. Please try again.");
    }
  }

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        onClick={() => setOpen(true)}
        className={className}
      >
        {plan.cta}
      </button>

      {open ? (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div
            className="absolute inset-0 bg-black/70 backdrop-blur-sm"
            onClick={closeModal}
          />
          <div
            ref={panelRef}
            role="dialog"
            aria-modal="true"
            aria-labelledby="waitlist-title"
            onKeyDown={onPanelKeyDown}
            className="relative w-full max-w-md rounded-2xl border border-border bg-surface p-6 shadow-2xl"
          >
            <button
              type="button"
              aria-label="Close dialog"
              onClick={closeModal}
              className="absolute right-4 top-4 text-muted hover:text-foreground"
            >
              <X className="h-5 w-5" />
            </button>
            <h3 id="waitlist-title" className="text-lg font-semibold">
              Join the Cloud waitlist
            </h3>
            <p className="mt-2 text-sm text-muted">
              Be first to know when WebReaper Cloud opens. No spam, no card.
            </p>

            {status === "done" ? (
              <div className="mt-6 rounded-lg border border-accent/30 bg-accent/10 p-4 text-sm text-accent">
                {message}
              </div>
            ) : (
              <form onSubmit={submit} className="mt-5 space-y-3">
                <input
                  ref={inputRef}
                  type="email"
                  required
                  autoComplete="email"
                  aria-label="Email address"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="you@company.com"
                  className="w-full rounded-lg border border-border bg-background px-3.5 py-2.5 text-sm outline-none focus:border-accent focus:ring-2 focus:ring-ring/40"
                />
                {status === "error" ? (
                  <p className="text-sm text-red-600 dark:text-red-400">
                    {message}
                  </p>
                ) : null}
                <button
                  type="submit"
                  disabled={status === "loading"}
                  className="inline-flex h-11 w-full items-center justify-center rounded-lg bg-accent px-5 text-sm font-medium text-accent-foreground transition hover:bg-accent-strong disabled:opacity-60"
                >
                  {status === "loading" ? "Joining…" : "Join waitlist"}
                </button>
              </form>
            )}
          </div>
        </div>
      ) : null}
    </>
  );
}
