// Categorical slots (validated reference palette). Color follows the entity for the whole session, never its rank.
const slots = ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300"] as const;
const assigned = new Map<string, string>();
export const otherColor = "#a3a3a3";

export function seriesColor(entity: string): string {
  const existing = assigned.get(entity);
  if (existing) return existing;
  // Never cycle: once the six slots are taken, later entities fold into the neutral "other" color.
  const next = slots[assigned.size];
  if (!next) return otherColor;
  assigned.set(entity, next);
  return next;
}

