export type FactListExportKind = 'server' | 'disabled' | 'capped'

export interface FactListPageEntry {
  file: string
  growth: string
  export: FactListExportKind
}

export const FACT_LIST_PAGES: readonly FactListPageEntry[] = []
