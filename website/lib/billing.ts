import { siteConfig } from "@/lib/site";

export type CheckoutResult = {
  ok: boolean;
  mode: "stripe" | "waitlist";
  url?: string;
  message?: string;
};

/**
 * Stripe-ready seam. When STRIPE_SECRET_KEY is configured, create a Checkout
 * Session and return its URL. Until then, every paid plan collects a waitlist
 * signup so the subscribe flow is wired end-to-end and dropping Stripe in later
 * is a one-function change.
 */
export async function createCheckout(
  plan: string,
  email?: string,
): Promise<CheckoutResult> {
  if (process.env.STRIPE_SECRET_KEY) {
    // TODO: create a Stripe Checkout Session for `plan` and return session.url.
    // const stripe = new Stripe(process.env.STRIPE_SECRET_KEY);
    // const session = await stripe.checkout.sessions.create({ ... });
    // return { ok: true, mode: "stripe", url: session.url ?? undefined };
  }

  // Waitlist: notify the site owner of the signup (best-effort). A failed or
  // unconfigured notification must never fail the signup, so the UI stays wired
  // end to end; set RESEND_API_KEY to start receiving leads. Drop in Stripe
  // later by setting STRIPE_SECRET_KEY above.
  await notifyWaitlistSignup(plan, email);
  return {
    ok: true,
    mode: "waitlist",
    message: "You're on the list. We'll email you when Cloud opens up.",
  };
}

/**
 * Email the site owner that someone joined the Cloud waitlist, via the Resend
 * REST API (no SDK dependency). Gated on RESEND_API_KEY: without it the signup
 * still succeeds and nothing is sent (the UI is wired end to end either way).
 * Best-effort, so a notification failure never fails the signup. See
 * .env.example for the env vars.
 *
 * The default sender `onboarding@resend.dev` needs no domain verification but
 * delivers only to the Resend account's own address, which is exactly the owner
 * notification here; verify a domain and set WAITLIST_NOTIFY_FROM to send
 * confirmations to the signer-up.
 */
async function notifyWaitlistSignup(plan: string, email?: string): Promise<void> {
  const apiKey = process.env.RESEND_API_KEY;
  if (!apiKey) return;

  const to = process.env.WAITLIST_NOTIFY_TO ?? siteConfig.contactEmail;
  const from =
    process.env.WAITLIST_NOTIFY_FROM ?? "WebReaper Waitlist <onboarding@resend.dev>";

  try {
    const res = await fetch("https://api.resend.com/emails", {
      method: "POST",
      headers: {
        Authorization: `Bearer ${apiKey}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        from,
        to: [to],
        reply_to: email,
        subject: `WebReaper Cloud waitlist: ${plan}`,
        text:
          `New WebReaper Cloud waitlist signup.\n\n` +
          `Plan:  ${plan}\n` +
          `Email: ${email ?? "(not provided)"}\n` +
          `Time:  ${new Date().toISOString()}\n`,
      }),
    });
    if (!res.ok) {
      console.error(
        "Waitlist notification failed:",
        res.status,
        await res.text().catch(() => ""),
      );
    }
  } catch (err) {
    console.error("Waitlist notification error:", err);
  }
}
