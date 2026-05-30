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

  // Waitlist fallback: in production this would persist `email` + `plan`.
  void email;
  return {
    ok: true,
    mode: "waitlist",
    message: "You're on the list. We'll email you when Cloud opens up.",
  };
}
