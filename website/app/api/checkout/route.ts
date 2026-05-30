import { createCheckout } from "@/lib/billing";

export async function POST(req: Request) {
  const body = await req.json().catch(() => ({}) as Record<string, unknown>);
  const plan = typeof body.plan === "string" ? body.plan : "cloud";
  const email = typeof body.email === "string" ? body.email : undefined;

  if (!email || !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) {
    return Response.json(
      { ok: false, message: "Please enter a valid email address." },
      { status: 400 },
    );
  }

  const result = await createCheckout(plan, email);

  if (result.mode === "stripe" && result.url) {
    return Response.json({ ok: true, redirect: result.url });
  }
  return Response.json({ ok: result.ok, message: result.message });
}
