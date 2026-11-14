import {
  Activity,
  Bell,
  BookMarked,
  Clock,
  FileSearch,
  Hash,
  MailCheck,
  Settings,
  Users,
} from 'lucide-react'
import type { NavGroup } from '../nav-types'

export const systemGroup: NavGroup = {
  key: 'system',
  title: '系统运维',
  titleKey: 'groups.system',
  icon: Settings,
  items: [
    {
      title: '编码规则',
      titleKey: 'items.system.codeRules',
      href: '/system/code-rules',
      icon: Hash,
      permission: 'SwapMid.SystemManagement.CodeRules',
    },
    {
      title: '数据字典',
      titleKey: 'items.system.dataDictionaries',
      href: '/system/data-dictionaries',
      icon: BookMarked,
      permission: 'SwapMid.SystemManagement.DataDictionaries',
    },
    {
      title: '定时任务',
      titleKey: 'items.system.scheduledJobs',
      href: '/system/scheduled-jobs',
      icon: Clock,
      permission: 'SwapMid.SystemManagement.ScheduledJobs',
    },
    {
      title: '通知设置',
      titleKey: 'items.system.notification',
      href: '/system/notification',
      icon: Bell,
      permission: 'SwapMid.NotificationCenter.Settings',
    },
    {
      title: '通知接收人',
      titleKey: 'items.system.notificationRecipients',
      href: '/system/notification-recipients',
      icon: Users,
      permission: 'SwapMid.NotificationCenter.Settings',
    },
    {
      title: '通知日志',
      titleKey: 'items.system.notificationLogs',
      href: '/system/notification-logs',
      icon: MailCheck,
      permission: 'SwapMid.NotificationCenter.Settings',
    },
    {
      title: '审计日志',
      titleKey: 'items.system.auditLogs',
      href: '/system/audit-logs',
      icon: FileSearch,
      permission: 'SwapMid.SystemManagement.AuditLogs',
    },
    {
      title: '系统体检',
      titleKey: 'items.system.health',
      href: '/system/health',
      icon: Activity,
      permission: 'SwapMid.SystemManagement.SystemHealth',
    },
  ],
}
