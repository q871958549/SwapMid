using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SwapMid.Layout;

public class TableSavedSortItemDto
{
    public string Id { get; set; } = string.Empty;
    public bool Desc { get; set; }
}

public class TableSavedColumnFilterDto
{
    public string Id { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class TableSavedPresetDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string>? Order { get; set; }
    public Dictionary<string, bool>? Visibility { get; set; }
    public Dictionary<string, int>? Sizing { get; set; }
    public List<string>? Pinned { get; set; }
    public string? Search { get; set; }
    public List<TableSavedSortItemDto>? SortItems { get; set; }
    public List<TableSavedColumnFilterDto>? ColumnFilters { get; set; }
    public Dictionary<string, string>? UrlFilters { get; set; }
}

public class TableViewPreferenceDto
{
    public List<string>? Order { get; set; }
    public Dictionary<string, bool>? Visibility { get; set; }
    public Dictionary<string, int>? Sizing { get; set; }
    public List<string>? Pinned { get; set; }
    public int? PageSize { get; set; }
    public List<int>? CustomPageSizes { get; set; }
    public List<TableSavedPresetDto>? Presets { get; set; }
}

public class TablePreferenceSnapshotDto
{
    public string Density { get; set; } = "standard";
    public Dictionary<string, TableViewPreferenceDto> Tables { get; set; } = new();
}

public class SaveTableViewPreferenceInput
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string TableKey { get; set; } = string.Empty;

    public TableViewPreferenceDto? View { get; set; }
}

public class SaveTableDensityInput
{
    [Required]
    [StringLength(32, MinimumLength = 1)]
    public string Density { get; set; } = "standard";
}

public class NavUsageInputDto
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Domain { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Group { get; set; } = string.Empty;

    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Href { get; set; } = string.Empty;
}
