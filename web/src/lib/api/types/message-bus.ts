import type { Require } from './_require'

// ===== 消息总线 =====
export type MessageTopicDto = Require<import('../generated').SwapMidMessageBusMessageTopicDto, 'id' | 'name' | 'creationTime'>
export type MessageSubscriptionDto = Require<import('../generated').SwapMidMessageBusMessageSubscriptionDto, 'id' | 'topicId' | 'creationTime'>
export type MessageRecordDto = Require<import('../generated').SwapMidMessageBusMessageRecordDto, 'id' | 'topicId' | 'subject' | 'payload' | 'status' | 'retryCount' | 'creationTime'>
