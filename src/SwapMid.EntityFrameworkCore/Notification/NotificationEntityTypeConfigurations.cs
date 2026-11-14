using SwapMid.Notification;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace SwapMid.EntityFrameworkCore.Notification;

public static class NotificationEntityTypeConfigurations
{
    public static void ConfigureNotification(this ModelBuilder builder)
    {
        builder.Entity<NotificationLog>(b =>
        {
            b.ToTable(SwapMidConsts.DbTablePrefix + "NotificationLogs", SwapMidConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Channel).HasConversion<int>();
            b.Property(x => x.Level).HasConversion<int>();
            b.Property(x => x.Title).IsRequired().HasMaxLength(256);
            b.Property(x => x.ToUser).HasMaxLength(512);
            b.Property(x => x.Source).HasMaxLength(64);
            b.Property(x => x.Error).HasMaxLength(2048);
            b.HasIndex(x => x.CreationTime);
        });

        builder.Entity<NotificationRecipient>(b =>
        {
            b.ToTable(SwapMidConsts.DbTablePrefix + "NotificationRecipients", SwapMidConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(64);
            b.Property(x => x.Email).HasMaxLength(256);
            b.Property(x => x.WebhookTarget).HasMaxLength(128);
            b.Property(x => x.MinLevel).HasConversion<int>();
            b.Property(x => x.Sources).HasMaxLength(256);
            b.HasIndex(x => x.IsEnabled);
        });
    }
}
