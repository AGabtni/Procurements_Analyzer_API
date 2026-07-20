namespace ProcurePortal.API.DTOs;

public class IndustryElementsDto
{
    public string[] Examples { get; set; } = [];
    public string[] Inclusions { get; set; } = [];
    public string[] Exclusions { get; set; } = [];
    public string[] ExamplesFr { get; set; } = [];
    public string[] InclusionsFr { get; set; } = [];
    public string[] ExclusionsFr { get; set; } = [];
}

public class IndustryDto
{
    public string Code { get; set; } = string.Empty;
    public string TitleEn { get; set; } = string.Empty;
    public string? TitleFr { get; set; }
    public short Level { get; set; }
    public string? ParentCode { get; set; }
    public bool HasChildren { get; set; }
    public IndustryElementsDto? Elements { get; set; }
}

public class IndustrySearchResultDto : IndustryDto
{
    // Ancestor titles from root down to the direct parent (e.g. ["Construction", "Construction of Buildings"])
    public string[] AncestorTitles { get; set; } = [];
}

public class IndustryLabelDto
{
    public string Code { get; set; } = string.Empty;
    public string TitleEn { get; set; } = string.Empty;
}
