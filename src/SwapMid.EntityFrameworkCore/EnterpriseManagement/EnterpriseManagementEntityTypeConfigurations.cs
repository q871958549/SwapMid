using SwapMid.EnterpriseManagement;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace SwapMid.EntityFrameworkCore.EnterpriseManagement;

public static class EnterpriseManagementEntityTypeConfigurations
{
    public static void ConfigureEnterpriseManagement(this ModelBuilder builder)
    {
        builder.Entity<Enterprise>(b =>
        {
            b.ToTable(SwapMidConsts.DbTablePrefix + "Enterprises", SwapMidConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(EnterpriseConsts.MaxCodeLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(EnterpriseConsts.MaxNameLength);
            b.Property(x => x.ShortName).HasMaxLength(EnterpriseConsts.MaxShortNameLength);
            b.Property(x => x.ContactPerson).HasMaxLength(EnterpriseConsts.MaxContactPersonLength);
            b.Property(x => x.ContactPhone).HasMaxLength(EnterpriseConsts.MaxContactPhoneLength);
            b.Property(x => x.Email).HasMaxLength(EnterpriseConsts.MaxEmailLength);
            b.Property(x => x.Address).HasMaxLength(EnterpriseConsts.MaxAddressLength);
            b.Property(x => x.Status).HasConversion<int>();

            b.HasIndex(x => x.Code).IsUnique();
            b.HasIndex(x => x.Name);
        });

        builder.Entity<CompanyDepartment>(b =>
        {
            b.ToTable(SwapMidConsts.DbTablePrefix + "CompanyDepartments", SwapMidConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(CompanyDepartmentConsts.MaxCodeLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(CompanyDepartmentConsts.MaxNameLength);
            b.Property(x => x.Level).HasDefaultValue(1);
            b.Property(x => x.FullPath).HasMaxLength(CompanyDepartmentConsts.MaxFullPathLength);

            b.HasIndex(x => new { x.EnterpriseId, x.Code }).IsUnique();
            b.HasIndex(x => x.ParentId);

            b.HasOne<Enterprise>()
                .WithMany()
                .HasForeignKey(x => x.EnterpriseId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            b.HasOne<CompanyDepartment>()
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            b.HasOne<Employee>()
                .WithMany()
                .HasForeignKey(x => x.LeaderEmployeeId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
        });

        builder.Entity<Employee>(b =>
        {
            b.ToTable(SwapMidConsts.DbTablePrefix + "Employees", SwapMidConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.EmployeeNo).IsRequired().HasMaxLength(EmployeeConsts.MaxEmployeeNoLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(EmployeeConsts.MaxNameLength);
            b.Property(x => x.Email).HasMaxLength(EmployeeConsts.MaxEmailLength);
            b.Property(x => x.Phone).HasMaxLength(EmployeeConsts.MaxPhoneLength);
            b.Property(x => x.Position).HasMaxLength(EmployeeConsts.MaxPositionLength);
            b.Property(x => x.Status).HasConversion<int>();

            b.HasIndex(x => new { x.EnterpriseId, x.EmployeeNo }).IsUnique();
            b.HasIndex(x => x.UserId).IsUnique();
            b.HasIndex(x => x.DepartmentId);
            b.HasIndex(x => x.PositionId);

            b.HasOne<Enterprise>()
                .WithMany()
                .HasForeignKey(x => x.EnterpriseId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            b.HasOne<CompanyDepartment>()
                .WithMany()
                .HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            b.HasOne<Position>()
                .WithMany()
                .HasForeignKey(x => x.PositionId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
        });

        builder.Entity<Position>(b =>
        {
            b.ToTable(SwapMidConsts.DbTablePrefix + "Positions", SwapMidConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(PositionConsts.MaxCodeLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(PositionConsts.MaxNameLength);
            b.Property(x => x.Level).HasMaxLength(PositionConsts.MaxLevelLength);
            b.Property(x => x.Description).HasMaxLength(PositionConsts.MaxDescriptionLength);

            b.HasIndex(x => new { x.DepartmentId, x.Code }).IsUnique();
            b.HasIndex(x => x.EnterpriseId);

            b.HasOne<Enterprise>()
                .WithMany()
                .HasForeignKey(x => x.EnterpriseId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            b.HasOne<CompanyDepartment>()
                .WithMany()
                .HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
        });
    }
}
