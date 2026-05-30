import { useCases } from "@/.velite";

export type UseCase = (typeof useCases)[number];

export function getUseCases(): UseCase[] {
  return useCases.slice().sort((a, b) => a.order - b.order);
}

export function getUseCaseBySlug(slug: string): UseCase | undefined {
  return useCases.find((u) => u.slug === slug);
}
