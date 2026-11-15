import { Activity, Database, GitBranch, History } from 'lucide-react'
import type { NavGroup } from '../nav-types'

export const dataGroup: NavGroup = {
  key: 'data',
  title: '数据集成',
  titleKey: 'groups.data',
  icon: Database,
  items: [
    {
      title: '数据源',
      titleKey: 'items.data.sources',
      href: '/data-integration/sources',
      icon: Database,
      permission: 'SwapMid.DataIntegration.DataSources',
    },
    {
      title: '数据管道',
      titleKey: 'items.data.pipelines',
      href: '/data-integration/pipelines',
      icon: GitBranch,
      permission: 'SwapMid.DataIntegration.DataPipelines',
    },
    {
      title: '执行记录',
      titleKey: 'items.data.executions',
      href: '/data-integration/executions',
      icon: History,
      permission: 'SwapMid.DataIntegration.PipelineExecutions',
    },
    {
      title: 'PostgreSQL CDC',
      titleKey: 'items.data.cdc',
      href: '/data-integration/cdc',
      icon: Activity,
      permission: 'SwapMid.DataIntegration.CdcStreams',
    },
  ],
}
