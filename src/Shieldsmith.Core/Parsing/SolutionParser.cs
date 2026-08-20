using System.Xml.Linq;
using Shieldsmith.Core.Models;

namespace Shieldsmith.Core.Parsing;

/// <summary>
/// Reads solution.xml, customizations.xml and the environment variable definition
/// folder into a SolutionModel. Parsing is tolerant: a missing optional element is
/// never an error, and anything that cannot be fully read becomes a diagnostic
/// rather than an exception or a silently invented value.
/// </summary>
public static class SolutionParser
{
    public static SolutionModel Parse(UnpackedSolution unpacked, IProgress<string>? progress = null)
    {
        var model = new SolutionModel();

        progress?.Report("Reading solution manifest...");
        ParseSolutionXml(unpacked.SolutionXmlPath, model);

        progress?.Report("Reading customisations...");
        var customizations = XDocument.Load(unpacked.CustomizationsXmlPath);
        ParseEntities(customizations, model);
        ParseRelationships(customizations, model);

        progress?.Report("Reading processes and flows...");
        FlowParser.Parse(customizations, unpacked.RootPath, model);
        ParseWebResources(customizations, model);

        progress?.Report("Reading forms, views, roles and apps...");
        UxParser.Parse(customizations, model);

        progress?.Report("Reading canvas apps...");
        CanvasAppParser.Parse(customizations, unpacked.RootPath, model);

        progress?.Report("Reading agents...");
        AgentParser.Parse(unpacked.RootPath, model);

        progress?.Report("Reading plugins...");
        PluginParser.Parse(customizations, model);

        progress?.Report("Reading environment variables...");
        ParseEnvironmentVariables(unpacked.RootPath, customizations, model);

        progress?.Report("Deriving implicit relationships...");
        InferLookupRelationships(model);
        ResolveRootComponentNames(model);

        return model;
    }

    private static void ParseSolutionXml(string path, SolutionModel model)
    {
        var doc = XDocument.Load(path);
        var manifest = doc.Root?.Element("SolutionManifest");
        if (manifest is null)
            throw new SolutionFormatException("solution.xml has no SolutionManifest element.");

        model.UniqueName = manifest.Element("UniqueName")?.Value ?? string.Empty;
        model.DisplayName = LocalizedText(manifest.Element("LocalizedNames")) ?? model.UniqueName;
        model.Version = manifest.Element("Version")?.Value ?? string.Empty;
        model.IsManaged = manifest.Element("Managed")?.Value.Trim() == "1";

        var publisher = manifest.Element("Publisher");
        if (publisher is not null)
        {
            model.PublisherUniqueName = publisher.Element("UniqueName")?.Value ?? string.Empty;
            model.PublisherDisplayName = LocalizedText(publisher.Element("LocalizedNames")) ?? model.PublisherUniqueName;
            model.CustomizationPrefix = publisher.Element("CustomizationPrefix")?.Value ?? string.Empty;
        }

        foreach (var rc in manifest.Descendants("RootComponent"))
        {
            if (!int.TryParse(rc.Attribute("type")?.Value, out var typeCode))
            {
                model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning,
                    $"Root component with unreadable type attribute: {rc}"));
                continue;
            }

            model.RootComponents.Add(new RootComponent
            {
                TypeCode = typeCode,
                TypeName = ComponentTypes.NameOf(typeCode),
                SchemaName = rc.Attribute("schemaName")?.Value ?? string.Empty,
                Id = rc.Attribute("id")?.Value.Trim('{', '}') ?? string.Empty,
                Behavior = rc.Attribute("behavior")?.Value ?? string.Empty,
            });
        }
    }

    private static void ParseEntities(XDocument customizations, SolutionModel model)
    {
        // Scoped deliberately: Entities/Entity only. Descendants("Entity") also
        // matches unrelated nested elements and produces phantom entities.
        var entities = customizations.Root?.Element("Entities")?.Elements("Entity") ?? [];

        foreach (var entityElement in entities)
        {
            var nameElement = entityElement.Element("Name");
            if (nameElement is null)
            {
                model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning,
                    "An Entity element had no Name and was skipped."));
                continue;
            }

            var info = entityElement.Element("EntityInfo")?.Element("entity");
            if (info is null)
            {
                // Entities included only for a ribbon or form change carry no
                // EntityInfo. Record them by name rather than losing them.
                model.Entities.Add(new EntityModel
                {
                    SchemaName = nameElement.Value.Trim(),
                    LogicalName = nameElement.Value.Trim().ToLowerInvariant(),
                    DisplayName = nameElement.Attribute("LocalizedName")?.Value ?? string.Empty,
                });
                model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Info,
                    $"Entity '{nameElement.Value.Trim()}' is included without table metadata " +
                    "(a ribbon or form-only change); its columns are not part of this solution."));
                continue;
            }

            var entity = new EntityModel
            {
                SchemaName = nameElement.Value.Trim(),
                LogicalName = (info.Attribute("Name")?.Value ?? nameElement.Value).Trim().ToLowerInvariant(),
                DisplayName = nameElement.Attribute("LocalizedName")?.Value ?? string.Empty,
                Description = LocalizedText(info.Element("Descriptions"), "Description", "description") ?? string.Empty,
                EntitySetName = info.Element("EntitySetName")?.Value ?? string.Empty,
                OwnershipType = info.Element("OwnershipTypeMask")?.Value ?? string.Empty,
                IsAuditEnabled = info.Element("IsAuditEnabled")?.Value.Trim() == "1",
                IsActivity = info.Element("IsActivity")?.Value.Trim() == "1",
            };

            foreach (var attr in info.Element("attributes")?.Elements("attribute") ?? [])
            {
                var displayMask = attr.Element("DisplayMask")?.Value ?? string.Empty;
                var type = attr.Element("Type")?.Value ?? string.Empty;
                entity.Attributes.Add(new AttributeModel
                {
                    PhysicalName = attr.Attribute("PhysicalName")?.Value ?? string.Empty,
                    LogicalName = attr.Element("LogicalName")?.Value ?? attr.Element("Name")?.Value ?? string.Empty,
                    DisplayName = LocalizedText(attr.Element("displaynames"), "displayname", "description") ?? string.Empty,
                    Description = LocalizedText(attr.Element("Descriptions"), "Description", "description") ?? string.Empty,
                    Type = type,
                    RequiredLevel = RequiredLevelParser.Parse(attr.Element("RequiredLevel")?.Value),
                    IsPrimaryId = string.Equals(type, "primarykey", StringComparison.OrdinalIgnoreCase),
                    IsPrimaryName = displayMask.Contains("PrimaryName", StringComparison.OrdinalIgnoreCase),
                    IsCustomField = attr.Element("IsCustomField")?.Value.Trim() == "1",
                });
            }

            foreach (var key in info.Element("EntityKeys")?.Elements("EntityKey") ?? [])
            {
                var keyModel = new EntityKeyModel
                {
                    LogicalName = key.Element("LogicalName")?.Value ?? string.Empty,
                    DisplayName = LocalizedText(key.Element("displaynames"), "displayname", "description") ?? string.Empty,
                };
                foreach (var keyAttr in key.Element("EntityKeyAttributes")?.Elements("AttributeName") ?? [])
                    keyModel.KeyAttributes.Add(keyAttr.Value);
                entity.Keys.Add(keyModel);
            }

            model.Entities.Add(entity);
        }
    }

    private static void ParseRelationships(XDocument customizations, SolutionModel model)
    {
        // Relationships live at solution level under EntityRelationships, not
        // nested per entity. (The distinction matters: per-entity nesting is how
        // an earlier version of this parser managed to find zero relationships.)
        var relationships = customizations.Root?.Element("EntityRelationships")?.Elements("EntityRelationship") ?? [];

        foreach (var rel in relationships)
        {
            var schemaName = rel.Attribute("Name")?.Value ?? string.Empty;
            var relType = rel.Element("EntityRelationshipType")?.Value.Trim() ?? string.Empty;

            if (string.Equals(relType, "OneToMany", StringComparison.OrdinalIgnoreCase))
            {
                model.OneToManyRelationships.Add(new OneToManyRelationship
                {
                    SchemaName = schemaName,
                    ReferencedEntity = rel.Element("ReferencedEntityName")?.Value ?? string.Empty,
                    ReferencingEntity = rel.Element("ReferencingEntityName")?.Value ?? string.Empty,
                    ReferencingAttribute = rel.Element("ReferencingAttributeName")?.Value ?? string.Empty,
                    CascadeDelete = rel.Element("CascadeDelete")?.Value ?? string.Empty,
                    IsHierarchical = rel.Element("IsHierarchical")?.Value.Trim() == "1",
                });
            }
            else if (string.Equals(relType, "ManyToMany", StringComparison.OrdinalIgnoreCase))
            {
                model.ManyToManyRelationships.Add(new ManyToManyRelationship
                {
                    SchemaName = schemaName,
                    Entity1 = rel.Element("FirstEntityName")?.Value ?? string.Empty,
                    Entity2 = rel.Element("SecondEntityName")?.Value ?? string.Empty,
                    IntersectEntity = rel.Element("IntersectEntityName")?.Value ?? string.Empty,
                });
            }
            else
            {
                model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Info,
                    $"Relationship '{schemaName}' has unrecognised type '{relType}' and was recorded as a diagnostic only."));
            }
        }
    }

    private static void ParseEnvironmentVariables(string rootPath, XDocument customizations, SolutionModel model)
    {
        // Primary source: the environmentvariabledefinitions folder in the export.
        var definitionsDir = Path.Combine(rootPath, "environmentvariabledefinitions");
        if (Directory.Exists(definitionsDir))
        {
            foreach (var dir in Directory.EnumerateDirectories(definitionsDir).OrderBy(d => d))
            {
                var definitionFile = Path.Combine(dir, "environmentvariabledefinition.xml");
                if (!File.Exists(definitionFile)) continue;

                try
                {
                    var def = XDocument.Load(definitionFile).Root;
                    if (def is null) continue;
                    var variable = new EnvironmentVariableModel
                    {
                        SchemaName = def.Attribute("schemaname")?.Value ?? Path.GetFileName(dir),
                        DisplayName = def.Element("displayname")?.Attribute("default")?.Value
                                      ?? LocalizedText(def.Element("displayname"), "label", "description")
                                      ?? string.Empty,
                        DefaultValue = def.Element("defaultvalue")?.Value ?? string.Empty,
                        IsRequired = def.Element("isrequired")?.Value.Trim() == "1",
                        IsSecret = def.Element("secretstore")?.Value.Trim() == "1",
                    };
                    if (int.TryParse(def.Element("type")?.Value, out var typeCode))
                        variable.TypeCode = typeCode;
                    model.EnvironmentVariables.Add(variable);
                }
                catch (Exception ex)
                {
                    model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning,
                        $"Environment variable definition '{Path.GetFileName(dir)}' could not be read: {ex.Message}"));
                }
            }
        }

        // Values, when the export carries them, appear as environmentvariablevalues.xml
        // either in a root-level folder or inside each definition folder. The file
        // holds a list of environmentvariablevalue elements keyed by schema name.
        foreach (var valueFile in Directory.EnumerateFiles(rootPath, "environmentvariablevalue*.xml", SearchOption.AllDirectories))
        {
            try
            {
                var root = XDocument.Load(valueFile).Root;
                if (root is null) continue;
                var valueElements = root.Name.LocalName == "environmentvariablevalue"
                    ? new[] { root }
                    : root.Descendants("environmentvariablevalue").ToArray();
                foreach (var valueElement in valueElements)
                {
                    ApplyValue(model,
                        valueElement.Attribute("schemaname")?.Value ?? string.Empty,
                        valueElement.Element("value")?.Value ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning,
                    $"Environment variable value file '{Path.GetFileName(valueFile)}' could not be read: {ex.Message}"));
            }
        }

        // Fallback for older export shapes that put definitions in customizations.xml.
        if (model.EnvironmentVariables.Count == 0)
        {
            foreach (var def in customizations.Descendants("EnvironmentVariableDefinition"))
            {
                model.EnvironmentVariables.Add(new EnvironmentVariableModel
                {
                    SchemaName = def.Element("SchemaName")?.Value ?? def.Attribute("schemaname")?.Value ?? string.Empty,
                    DisplayName = LocalizedText(def.Element("DisplayNames")) ?? string.Empty,
                    DefaultValue = def.Element("DefaultValue")?.Value ?? string.Empty,
                });
            }
        }
    }

    private static void ApplyValue(SolutionModel model, string schemaName, string value)
    {
        if (string.IsNullOrEmpty(schemaName)) return;
        var variable = model.EnvironmentVariables.FirstOrDefault(v =>
            string.Equals(v.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase));
        if (variable is null)
        {
            variable = new EnvironmentVariableModel { SchemaName = schemaName };
            model.EnvironmentVariables.Add(variable);
        }
        variable.CurrentValue = value;
        variable.HasCurrentValue = true;
    }

    private static void InferLookupRelationships(SolutionModel model)
    {
        // A lookup-shaped attribute with no declared 1:N in this solution is still
        // a relationship in practice. Recorded separately and always labelled inferred.
        var declaredLookups = new HashSet<string>(
            model.OneToManyRelationships.Select(r =>
                $"{r.ReferencingEntity.ToLowerInvariant()}|{r.ReferencingAttribute.ToLowerInvariant()}"));

        foreach (var entity in model.Entities)
        {
            foreach (var attribute in entity.Attributes.Where(a => a.IsLookupStyle))
            {
                var key = $"{entity.LogicalName}|{attribute.LogicalName.ToLowerInvariant()}";
                var keySchema = $"{entity.SchemaName.ToLowerInvariant()}|{attribute.LogicalName.ToLowerInvariant()}";
                if (declaredLookups.Contains(key) || declaredLookups.Contains(keySchema))
                    continue;

                model.InferredLookups.Add(new InferredLookupRelationship
                {
                    Entity = entity.SchemaName,
                    AttributeLogicalName = attribute.LogicalName,
                    AttributeDisplayName = attribute.DisplayName,
                    AttributeType = attribute.Type,
                });
            }
        }
    }

    private static void ParseWebResources(XDocument customizations, SolutionModel model)
    {
        foreach (var resource in customizations.Root?.Element("WebResources")?.Elements("WebResource") ?? [])
        {
            var webResource = new WebResourceModel
            {
                Id = resource.Element("WebResourceId")?.Value.Trim('{', '}').ToLowerInvariant() ?? string.Empty,
                Name = resource.Element("Name")?.Value ?? string.Empty,
                DisplayName = resource.Element("DisplayName")?.Value ?? string.Empty,
                FileName = resource.Element("FileName")?.Value ?? string.Empty,
            };
            if (int.TryParse(resource.Element("WebResourceType")?.Value, out var typeCode))
                webResource.TypeCode = typeCode;
            model.WebResources.Add(webResource);
        }
    }

    /// <summary>
    /// Fills in names for GUID-identified root components from the artefacts parsed
    /// elsewhere in the export (processes, web resources).
    /// </summary>
    private static void ResolveRootComponentNames(SolutionModel model)
    {
        var namesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in model.Processes)
            if (!string.IsNullOrEmpty(process.Id))
                namesById[process.Id] = process.Name;
        foreach (var webResource in model.WebResources)
            if (!string.IsNullOrEmpty(webResource.Id))
                namesById[webResource.Id] = webResource.Name;
        foreach (var role in model.SecurityRoles)
            if (!string.IsNullOrEmpty(role.Id))
                namesById[role.Id] = role.Name;
        foreach (var entity in model.Entities)
        {
            foreach (var form in entity.Forms)
                if (!string.IsNullOrEmpty(form.Id) && !string.IsNullOrEmpty(form.Name))
                    namesById[form.Id] = form.Name;
            foreach (var view in entity.Views)
                if (!string.IsNullOrEmpty(view.Id) && !string.IsNullOrEmpty(view.Name))
                    namesById[view.Id] = view.Name;
        }

        var unresolved = 0;
        foreach (var component in model.RootComponents.Where(c =>
                     string.IsNullOrEmpty(c.SchemaName) && !string.IsNullOrEmpty(c.Id)))
        {
            if (namesById.TryGetValue(component.Id, out var name))
                component.ResolvedName = name;
            else
                unresolved++;
        }

        if (unresolved > 0)
            model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Info,
                $"{unresolved} root component(s) are identified only by GUID; their names are not carried in this export."));
    }

    /// <summary>
    /// Reads the common localised-text shape: a container whose child elements each
    /// carry the text in an attribute, e.g. LocalizedNames/LocalizedName@description
    /// or displaynames/displayname@description.
    /// </summary>
    private static string? LocalizedText(XElement? container,
        string childName = "LocalizedName", string attributeName = "description")
    {
        var text = container?.Elements(childName)
            .Select(e => e.Attribute(attributeName)?.Value)
            .FirstOrDefault(v => !string.IsNullOrEmpty(v));
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
