import { FileText, Radio, Rss } from 'lucide-react'
import type { NavGroup } from '../nav-types'

export const messageGroup: NavGroup = {
  key: 'message',
  title: '消息总线',
  titleKey: 'groups.message',
  icon: Radio,
  items: [
    {
      title: '消息主题',
      titleKey: 'items.message.topics',
      href: '/message-bus/topics',
      icon: Radio,
      permission: 'SwapMid.MessageBus.Topics',
    },
    {
      title: '消息订阅',
      titleKey: 'items.message.subscriptions',
      href: '/message-bus/subscriptions',
      icon: Rss,
      permission: 'SwapMid.MessageBus.Subscriptions',
    },
    {
      title: '消息记录',
      titleKey: 'items.message.records',
      href: '/message-bus/records',
      icon: FileText,
      permission: 'SwapMid.MessageBus.Records',
    },
  ],
}
