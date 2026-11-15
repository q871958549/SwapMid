using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Uow;

namespace SwapMid.MessageBus;

public class MessageBusDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IMessageTopicRepository _topicRepository;
    private readonly IGuidGenerator _guidGenerator;

    public MessageBusDataSeedContributor(
        IMessageTopicRepository topicRepository,
        IGuidGenerator guidGenerator)
    {
        _topicRepository = topicRepository;
        _guidGenerator = guidGenerator;
    }

    [UnitOfWork]
    public async Task SeedAsync(DataSeedContext context)
    {
        await EnsureTopicAsync(
            MessageBusTopics.IntegrationPipelineEvents,
            TopicType.Broadcast,
            "数据管道生命周期事件：ETL 管道触发、完成、失败与对账恢复事件流，可订阅转发至下游系统或机器人 Webhook。");

        await EnsureTopicAsync(
            MessageBusTopics.IntegrationCdcEvents,
            TopicType.Broadcast,
            "PostgreSQL 逻辑复制（pgoutput）CDC 变更事件流：实时捕获表级 INSERT/UPDATE/DELETE 变更与 LSN 检查点，推送至 CloudEvents 下游订阅。");

        await EnsureTopicAsync(
            MessageBusTopics.SystemAlerts,
            TopicType.Broadcast,
            "平台运行告警事件：定时任务异常、投递耗尽与外部探针告警统一事件主题。");
    }

    private async Task EnsureTopicAsync(string name, TopicType type, string description)
    {
        if (await _topicRepository.NameExistsAsync(name))
        {
            return;
        }

        await _topicRepository.InsertAsync(
            new MessageTopic(
                _guidGenerator.Create(),
                name,
                type,
                description: description),
            autoSave: true);
    }
}
