using SwapMid.SystemManagement;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace SwapMid.EntityFrameworkCore.SystemManagement;

public static class SystemManagementEntityTypeConfigurations
{
    public static void ConfigureSystemManagement(this ModelBuilder builder)
    {
        builder.Entity<CodeRule>(b =>
        {
            b.ToTable(SwapMidConsts.DbTablePrefix + "CodeRules",
                SwapMidConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.EntityType).IsRequired().HasMaxLength(CodeRuleConsts.MaxEntityTypeLength);
            b.Property(x => x.DisplayName).IsRequired().HasMaxLength(CodeRuleConsts.MaxEntityTypeLength);
            b.Property(x => x.Prefix).HasMaxLength(CodeRuleConsts.MaxPrefixLength);
            b.Property(x => x.Suffix).HasMaxLength(CodeRuleConsts.MaxSuffixLength);
            b.Property(x => x.DateFormat).HasMaxLength(CodeRuleConsts.MaxDateFormatLength);
            b.Property(x => x.Description).HasMaxLength(CodeRuleConsts.MaxDescriptionLength);
            b.Property(x => x.ResetCycle).HasConversion<int>();

            b.HasIndex(x => x.EntityType).IsUnique();
        });

        builder.Entity<DataDictionary>(b =>
        {
            b.ToTable(SwapMidConsts.DbTablePrefix + "DataDictionaries",
                SwapMidConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(DataDictionaryConsts.MaxCodeLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(DataDictionaryConsts.MaxNameLength);
            b.Property(x => x.Description).HasMaxLength(DataDictionaryConsts.MaxDescriptionLength);

            b.HasIndex(x => x.Code).IsUnique();
        });

        builder.Entity<DataDictionaryItem>(b =>
        {
            b.ToTable(SwapMidConsts.DbTablePrefix + "DataDictionaryItems",
                SwapMidConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Label).IsRequired().HasMaxLength(DataDictionaryConsts.MaxItemLabelLength);
            b.Property(x => x.Value).IsRequired().HasMaxLength(DataDictionaryConsts.MaxItemValueLength);
            b.Property(x => x.Description).HasMaxLength(DataDictionaryConsts.MaxDescriptionLength);

            b.HasIndex(x => x.DictionaryId);
        });
    }
}
