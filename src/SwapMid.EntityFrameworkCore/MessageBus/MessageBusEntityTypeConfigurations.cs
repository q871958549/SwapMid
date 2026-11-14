using SwapMid.MessageBus;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace SwapMid.EntityFrameworkCore.MessageBus;

public static class MessageBusEntityTypeConfigurations
{
    public static void ConfigureMessageBus(this ModelBuilder builder)
    {
        builder.Entity<MessageTopic>(b =>
        {
            b.ToTable(SwapMidConsts.DbTablePrefix + "MessageTopics", SwapMidConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(MessageTopicConsts.MaxNameLength);
            b.Property(x => x.Description).HasMaxLength(MessageTopicConsts.MaxDescriptionLength);
            b.Property(x => x.Type).HasConversion<int>();
            b.HasIndex(x => x.Name).IsUnique();
        });

        builder.Entity<MessageSubscription>(b =>
        {
            b.ToTable(SwapMidConsts.DbTablePrefix + "MessageSubscriptions", SwapMidConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(MessageSubscriptionConsts.MaxNameLength);
            b.Property(x => x.DeliveryType).HasConversion<int>();
            b.Property(x => x.EndpointUrl).HasMaxLength(MessageSubscriptionConsts.MaxEndpointUrlLength);
            b.Property(x => x.ExchangeName).HasMaxLength(MessageSubscriptionConsts.MaxExchangeNameLength);
            b.Property(x => x.RoutingKey).HasMaxLength(MessageSubscriptionConsts.MaxRoutingKeyLength);
            b.Property(x => x.FilterRule).HasMaxLength(MessageSubscriptionConsts.MaxFilterRuleLength);
            b.Property(x => x.Secret).HasMaxLength(MessageSubscriptionConsts.MaxSecretLength);
            b.HasOne<MessageTopic>().WithMany().HasForeignKey(x => x.TopicId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.TopicId);
        });

        builder.Entity<MessageRecord>(b =>
        {
            b.ToTable(SwapMidConsts.DbTablePrefix + "MessageRecords", SwapMidConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.TopicName).IsRequired().HasMaxLength(MessageTopicConsts.MaxNameLength);
            b.Property(x => x.Subject).IsRequired().HasMaxLength(MessageRecordConsts.MaxSubjectLength);
            b.Property(x => x.Payload).IsRequired().HasMaxLength(MessageRecordConsts.MaxPayloadLength);
            b.Property(x => x.ErrorMessage).HasMaxLength(MessageRecordConsts.MaxErrorMessageLength);
            b.Property(x => x.Status).HasConversion<int>();
            b.HasOne<MessageTopic>().WithMany().HasForeignKey(x => x.TopicId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.TopicId);
            b.HasIndex(x => x.Status);
        });

        builder.Entity<MessageDeliveryAttempt>(b =>
        {
            b.ToTable(SwapMidConsts.DbTablePrefix + "MessageDeliveryAttempts", SwapMidConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.SubscriptionName).IsRequired().HasMaxLength(MessageSubscriptionConsts.MaxNameLength);
            b.Property(x => x.LastError).HasMaxLength(MessageRecordConsts.MaxErrorMessageLength);
            b.Property(x => x.Status).HasConversion<int>();
            b.HasOne<MessageRecord>().WithMany().HasForeignKey(x => x.RecordId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.RecordId);
        });
    }
}
