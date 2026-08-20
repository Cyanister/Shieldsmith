namespace Shieldsmith.Core.Models;

public sealed class FormModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>The forms collection type from the export: main, quickCreate, card, quick, etc.</summary>
    public string FormType { get; set; } = string.Empty;
    public List<FormTabModel> Tabs { get; } = new();
    public List<string> Libraries { get; } = new();
}

public sealed class FormTabModel
{
    public string Label { get; set; } = string.Empty;
    public List<FormSectionModel> Sections { get; } = new();
}

public sealed class FormSectionModel
{
    public string Label { get; set; } = string.Empty;
    public List<string> Fields { get; } = new();
}

public sealed class ViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int QueryType { get; set; }
    public bool IsDefault { get; set; }
    public bool IsQuickFind { get; set; }
    public List<string> Columns { get; } = new();
}

public sealed class OptionSetModel
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsGlobal { get; set; }
    public List<OptionSetValue> Options { get; } = new();
}

public sealed class OptionSetValue
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public sealed class SecurityRoleModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<RolePrivilege> Privileges { get; } = new();

    public Dictionary<string, int> PrivilegeCountsByLevel =>
        Privileges.GroupBy(p => p.Level).ToDictionary(g => g.Key, g => g.Count());
}

public sealed class RolePrivilege
{
    public string Name { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
}

public sealed class AppModuleModel
{
    public string UniqueName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ClientType { get; set; } = string.Empty;
    public string NavigationType { get; set; } = string.Empty;
    public int ComponentCount { get; set; }
}

public sealed class SiteMapModel
{
    public string UniqueName { get; set; } = string.Empty;
    public List<SiteMapArea> Areas { get; } = new();
}

public sealed class SiteMapArea
{
    public string Title { get; set; } = string.Empty;
    public List<SiteMapGroup> Groups { get; } = new();
}

public sealed class SiteMapGroup
{
    public string Title { get; set; } = string.Empty;
    public List<SiteMapSubArea> SubAreas { get; } = new();
}

public sealed class SiteMapSubArea
{
    public string Title { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}
