export interface PermissionDraft {
  baseline: Record<string, boolean>
  granted: Record<string, boolean>
}

function isGrantMap(value: unknown): value is Record<string, boolean> {
  return !!value && typeof value === 'object' && !Array.isArray(value)
    && Object.entries(value).every(([name, granted]) => name.length > 0 && typeof granted === 'boolean')
}

export function isPermissionDraft(value: unknown): value is PermissionDraft {
  if (!value || typeof value !== 'object') return false
  const draft = value as Partial<PermissionDraft>
  return isGrantMap(draft.baseline) && isGrantMap(draft.granted)
    && samePermissionKeys(draft.baseline, draft.granted)
}

export function samePermissionKeys(a: Record<string, boolean>, b: Record<string, boolean>) {
  return Object.keys(a).length === Object.keys(b).length && Object.keys(a).every(name => Object.hasOwn(b, name))
}
