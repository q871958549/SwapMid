using Microsoft.EntityFrameworkCore;
using SwapMid.ApiManagement;
using SwapMid.DataIntegration;
using SwapMid.EnterpriseManagement;
using SwapMid.EntityFrameworkCore.ApiManagement;
using SwapMid.EntityFrameworkCore.DataIntegration;
using SwapMid.EntityFrameworkCore.EnterpriseManagement;
using SwapMid.EntityFrameworkCore.MessageBus;
using SwapMid.EntityFrameworkCore.Notification;
using SwapMid.EntityFrameworkCore.Scheduling;
using SwapMid.EntityFrameworkCore.SystemManagement;
using SwapMid.MessageBus;
using SwapMid.Notification;
using SwapMid.Scheduling;
using SwapMid.SystemManagement;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.BlobStoring.Database.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.TenantManagement;
using Volo.Abp.TenantManagement.EntityFrameworkCore;

namespace SwapMid.EntityFrameworkCore;

[ReplaceDbContext(typeof(IIdentityDbContext))]
[ReplaceDbContext(typeof(ITenantManagementDbContext))]
[ConnectionStringName("Default")]
public class SwapMidDbContext :
    AbpDbContext<SwapMidDbContext>,
    ITenantManagementDbContext,
    IIdentityDbContext
{
    // ==================== Core Integration Engines ====================
    public DbSet<ApiGroup> ApiGroups { get; set; } = null!;
    public DbSet<ApiRoute> ApiRoutes { get; set; } = null!;

    public DbSet<DataSource> DataSources { get; set; } = null!;
    public DbSet<DataPipeline> DataPipelines { get; set; } = null!;
    public DbSet<PipelineExecution> PipelineExecutions { get; set; } = null!;
    public DbSet<CdcStreamDefinition> CdcStreams { get; set; } = null!;

    public DbSet<MessageTopic> MessageTopics { get; set; } = null!;
    public DbSet<MessageSubscription> MessageSubscriptions { get; set; } = null!;
    public DbSet<MessageRecord> MessageRecords { get; set; } = null!;
    public DbSet<MessageDeliveryAttempt> MessageDeliveryAttempts { get; set; } = null!;

    // ==================== Organization & Master Data ====================
    public DbSet<Enterprise> Enterprises { get; set; } = null!;
    public DbSet<CompanyDepartment> CompanyDepartments { get; set; } = null!;
    public DbSet<Position> Positions { get; set; } = null!;
    public DbSet<Employee> Employees { get; set; } = null!;

    // ==================== System, Notification & Scheduling ====================
    public DbSet<CodeRule> CodeRules { get; set; } = null!;
    public DbSet<DataDictionary> DataDictionaries { get; set; } = null!;
    public DbSet<DataDictionaryItem> DataDictionaryItems { get; set; } = null!;
    public DbSet<NotificationLog> NotificationLogs { get; set; } = null!;
    public DbSet<NotificationRecipient> NotificationRecipients { get; set; } = null!;
    public DbSet<ScheduledJobExecution> ScheduledJobExecutions { get; set; } = null!;

    // ==================== ABP Identity & Tenant Management ====================
    public DbSet<IdentityUser> Users { get; set; } = null!;
    public DbSet<IdentityRole> Roles { get; set; } = null!;
    public DbSet<IdentityClaimType> ClaimTypes { get; set; } = null!;
    public DbSet<OrganizationUnit> OrganizationUnits { get; set; } = null!;
    public DbSet<IdentitySecurityLog> SecurityLogs { get; set; } = null!;
    public DbSet<IdentityLinkUser> LinkUsers { get; set; } = null!;
    public DbSet<IdentityUserDelegation> UserDelegations { get; set; } = null!;
    public DbSet<IdentitySession> Sessions { get; set; } = null!;
    public DbSet<Tenant> Tenants { get; set; } = null!;
    public DbSet<TenantConnectionString> TenantConnectionStrings { get; set; } = null!;

    public SwapMidDbContext(DbContextOptions<SwapMidDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ConfigurePermissionManagement();
        builder.ConfigureSettingManagement();
        builder.ConfigureBackgroundJobs();
        builder.ConfigureAuditLogging();
        builder.ConfigureFeatureManagement();
        builder.ConfigureIdentity();
        builder.ConfigureOpenIddict();
        builder.ConfigureTenantManagement();
        builder.ConfigureBlobStoring();

        builder.ConfigureApiManagement();
        builder.ConfigureDataIntegration();
        builder.ConfigureMessageBus();
        builder.ConfigureEnterpriseManagement();
        builder.ConfigureSystemManagement();
        builder.ConfigureNotification();
        builder.ConfigureScheduling();
    }
}
