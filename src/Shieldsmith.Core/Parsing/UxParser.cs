using System.Xml.Linq;
using Shieldsmith.Core.Models;

namespace Shieldsmith.Core.Parsing;

/// <summary>
/// Parses the user-experience artefacts: forms and views (inside each Entity
/// element), global option sets, security roles, model-driven apps and their
/// site maps. All tolerant; missing pieces become diagnostics, not exceptions.
/// </summary>
public static class UxParser
{
    public static void Parse(XDocument customizations, SolutionModel model)
    {
        ParseFormsAndViews(customizations, model);
        ParseOptionSets(customizations, model);
        ParseRoles(customizations, model);
        ParseAppModules(customizations, model);
        ParseSiteMaps(customizations, model);
    }

    private static void ParseFormsAndViews(XDocument customizations, SolutionModel model)
    {
        foreach (var entityElement in customizations.Root?.Element("Entities")?.Elements("Entity") ?? [])
        {
            var entityName = entityElement.Element("Name")?.Value.Trim();
            var entity = entityName is null ? null : model.FindEntity(entityName);
            if (entity is null) continue;

            foreach (var formsCollection in entityElement.Element("FormXml")?.Elements("forms") ?? [])
            {
                var formType = formsCollection.Attribute("type")?.Value ?? string.Empty;
                foreach (var systemForm in formsCollection.Elements("systemform"))
                {
                    var form = new FormModel
                    {
                        Id = systemForm.Element("formid")?.Value.Trim('{', '}').ToLowerInvariant() ?? string.Empty,
                        Name = LocalizedText(systemForm.Element("LocalizedNames")) ?? string.Empty,
                        FormType = formType,
                    };

                    var formDefinition = systemForm.Element("form");
                    if (formDefinition is not null)
                    {
                        foreach (var library in formDefinition.Element("formLibraries")?.Elements("Library") ?? [])
                        {
                            var libraryName = library.Attribute("name")?.Value;
                            if (!string.IsNullOrEmpty(libraryName)) form.Libraries.Add(libraryName);
                        }

                        foreach (var tabElement in formDefinition.Element("tabs")?.Elements("tab") ?? [])
                        {
                            var tab = new FormTabModel
                            {
                                Label = LabelText(tabElement) ?? tabElement.Attribute("name")?.Value ?? string.Empty,
                            };
                            foreach (var sectionElement in tabElement.Descendants("section"))
                            {
                                var section = new FormSectionModel
                                {
                                    Label = LabelText(sectionElement) ?? sectionElement.Attribute("name")?.Value ?? string.Empty,
                                };
                                foreach (var control in sectionElement.Descendants("control"))
                                {
                                    var field = control.Attribute("datafieldname")?.Value;
                                    if (!string.IsNullOrEmpty(field) && !section.Fields.Contains(field))
                                        section.Fields.Add(field);
                                }
                                tab.Sections.Add(section);
                            }
                            form.Tabs.Add(tab);
                        }
                    }

                    entity.Forms.Add(form);
                }
            }

            foreach (var savedQuery in entityElement.Element("SavedQueries")
                         ?.Element("savedqueries")?.Elements("savedquery") ?? [])
            {
                var view = new ViewModel
                {
                    Id = savedQuery.Element("savedqueryid")?.Value.Trim('{', '}').ToLowerInvariant() ?? string.Empty,
                    Name = LocalizedText(savedQuery.Element("LocalizedNames")) ?? string.Empty,
                    IsDefault = savedQuery.Element("isdefault")?.Value.Trim() == "1",
                    IsQuickFind = savedQuery.Element("isquickfindquery")?.Value.Trim() == "1",
                };
                if (int.TryParse(savedQuery.Element("querytype")?.Value, out var queryType))
                    view.QueryType = queryType;

                // Columns come from the grid layout, which is XML embedded as text.
                var layoutXml = savedQuery.Element("layoutxml")?.Value;
                if (!string.IsNullOrEmpty(layoutXml))
                {
                    try
                    {
                        var layout = XDocument.Parse(layoutXml);
                        foreach (var cell in layout.Descendants("cell"))
                        {
                            var column = cell.Attribute("name")?.Value;
                            if (!string.IsNullOrEmpty(column)) view.Columns.Add(column);
                        }
                    }
                    catch (Exception ex)
                    {
                        model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning,
                            $"View '{view.Name}' on {entity.SchemaName} has unreadable layout XML: {ex.Message}"));
                    }
                }

                entity.Views.Add(view);
            }
        }
    }

    private static void ParseOptionSets(XDocument customizations, SolutionModel model)
    {
        foreach (var optionSetElement in customizations.Root?.Element("optionsets")?.Elements("optionset") ?? [])
        {
            var optionSet = new OptionSetModel
            {
                Name = optionSetElement.Attribute("Name")?.Value ?? string.Empty,
                DisplayName = optionSetElement.Attribute("localizedName")?.Value
                              ?? LocalizedText(optionSetElement.Element("displaynames"), "displayname")
                              ?? string.Empty,
                IsGlobal = optionSetElement.Element("IsGlobal")?.Value.Trim() == "1",
            };

            foreach (var option in optionSetElement.Element("options")?.Elements("option") ?? [])
            {
                optionSet.Options.Add(new OptionSetValue
                {
                    Value = option.Attribute("value")?.Value ?? string.Empty,
                    Label = LocalizedText(option.Element("labels"), "label") ?? string.Empty,
                });
            }

            model.OptionSets.Add(optionSet);
        }
    }

    private static void ParseRoles(XDocument customizations, SolutionModel model)
    {
        foreach (var roleElement in customizations.Root?.Element("Roles")?.Elements("Role") ?? [])
        {
            var role = new SecurityRoleModel
            {
                Id = roleElement.Attribute("id")?.Value.Trim('{', '}').ToLowerInvariant() ?? string.Empty,
                Name = roleElement.Attribute("name")?.Value ?? string.Empty,
            };
            foreach (var privilege in roleElement.Element("RolePrivileges")?.Elements("RolePrivilege") ?? [])
            {
                role.Privileges.Add(new RolePrivilege
                {
                    Name = privilege.Attribute("name")?.Value ?? string.Empty,
                    Level = privilege.Attribute("level")?.Value ?? string.Empty,
                });
            }
            model.SecurityRoles.Add(role);
        }
    }

    private static void ParseAppModules(XDocument customizations, SolutionModel model)
    {
        foreach (var appElement in customizations.Root?.Element("AppModules")?.Elements("AppModule") ?? [])
        {
            model.AppModules.Add(new AppModuleModel
            {
                UniqueName = appElement.Element("UniqueName")?.Value ?? string.Empty,
                Name = LocalizedText(appElement.Element("LocalizedNames")) ?? string.Empty,
                ClientType = appElement.Element("ClientType")?.Value ?? string.Empty,
                NavigationType = appElement.Element("NavigationType")?.Value ?? string.Empty,
                ComponentCount = appElement.Element("AppModuleComponents")?.Elements().Count() ?? 0,
            });
        }
    }

    private static void ParseSiteMaps(XDocument customizations, SolutionModel model)
    {
        foreach (var siteMapContainer in customizations.Root?.Element("AppModuleSiteMaps")
                     ?.Elements("AppModuleSiteMap") ?? [])
        {
            var siteMap = new SiteMapModel
            {
                UniqueName = siteMapContainer.Element("SiteMapUniqueName")?.Value ?? string.Empty,
            };

            foreach (var areaElement in siteMapContainer.Element("SiteMap")?.Elements("Area") ?? [])
            {
                var area = new SiteMapArea { Title = SiteMapTitle(areaElement) };
                foreach (var groupElement in areaElement.Elements("Group"))
                {
                    var group = new SiteMapGroup { Title = SiteMapTitle(groupElement) };
                    foreach (var subAreaElement in groupElement.Elements("SubArea"))
                    {
                        group.SubAreas.Add(new SiteMapSubArea
                        {
                            Title = SiteMapTitle(subAreaElement),
                            Entity = subAreaElement.Attribute("Entity")?.Value ?? string.Empty,
                            Url = subAreaElement.Attribute("Url")?.Value ?? string.Empty,
                        });
                    }
                    area.Groups.Add(group);
                }
                siteMap.Areas.Add(area);
            }

            model.SiteMaps.Add(siteMap);
        }
    }

    private static string SiteMapTitle(XElement element)
    {
        var title = element.Element("Titles")?.Elements("Title")
            .Select(t => t.Attribute("Title")?.Value)
            .FirstOrDefault(v => !string.IsNullOrEmpty(v));
        return title
               ?? element.Attribute("Title")?.Value
               ?? element.Attribute("Entity")?.Value
               ?? element.Attribute("Id")?.Value
               ?? string.Empty;
    }

    private static string? LabelText(XElement element)
    {
        var label = element.Element("labels")?.Elements("label")
            .Select(l => l.Attribute("description")?.Value)
            .FirstOrDefault(v => !string.IsNullOrEmpty(v));
        return string.IsNullOrEmpty(label) ? null : label;
    }

    private static string? LocalizedText(XElement? container, string childName = "LocalizedName")
    {
        var text = container?.Elements(childName)
            .Select(e => e.Attribute("description")?.Value)
            .FirstOrDefault(v => !string.IsNullOrEmpty(v));
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
