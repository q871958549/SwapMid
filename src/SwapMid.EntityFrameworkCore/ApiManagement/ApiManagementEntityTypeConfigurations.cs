using SwapMid.ApiManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace SwapMid.EntityFrameworkCore.ApiManagement;

public static class ApiManagementEntityTypeConfigurations
{
    public static void ConfigureApiManagement(this ModelBuilder builder)
    {
        builder.Entity<ApiGroup>(b =>
        {
            b.ToTable(SwapMidConsts.DbTablePrefix + "ApiGroups",
                SwapMidConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(ApiGroupConsts.MaxNameLength);
            b.Property(x => x.Description).HasMaxLength(ApiGroupConsts.MaxDescriptionLength);
            b.HasIndex(x => x.Name).IsUnique();
        });

        builder.Entity<ApiRoute>(b =>
        {
            b.ToTable(SwapMidConsts.DbTablePrefix + "ApiRoutes",
                SwapMidConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(ApiRouteConsts.MaxNameLength);
            b.Property(x => x.Path).IsRequired().HasMaxLength(ApiRouteConsts.MaxPathLength);
            b.Property(x => x.UpstreamUrl).IsRequired().HasMaxLength(ApiRouteConsts.MaxUpstreamUrlLength);
            b.Property(x => x.Description).HasMaxLength(ApiRouteConsts.MaxDescriptionLength);
            b.Property(x => x.HttpMethod).HasConversion<int>();

            b.HasIndex(x => new { x.Path, x.HttpMethod }).IsUnique();

            b.HasOne<ApiGroup>()
                .WithMany()
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
        });
    }
}
