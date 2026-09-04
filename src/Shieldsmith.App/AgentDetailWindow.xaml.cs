using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shieldsmith.Core.Models;

namespace Shieldsmith.App;

/// <summary>
/// Drill-down for one Copilot Studio agent: every topic's trigger phrases,
/// message variations and actions, the tools, and the knowledge sources, all
/// exactly as configured in the export. Facts only; nothing here is inferred.
/// </summary>
public partial class AgentDetailWindow : Window
{
    public AgentDetailWindow(AgentModel agent)
    {
        InitializeComponent();

        var name = agent.Name.Length > 0 ? agent.Name : agent.SchemaName;
        Title = $"Agent: {name}";
        txtAgentName.Text = name;
        txtAgentAuth.Text = agent.AuthenticationModeDisplay;
        txtAgentMeta.Text = string.Join("  ·  ", new[]
        {
            agent.SchemaName,
            agent.Template.Length > 0 ? $"template {agent.Template}" : null,
            agent.GenerativeActionsEnabled ? "generative actions enabled" : "generative actions off",
        }.Where(part => part is not null));

        if (agent.Instructions.Length > 0) txtInstructions.Text = agent.Instructions;
        else cardInstructions.Visibility = Visibility.Collapsed;

        txtTopicsTitle.Text = $"Topics ({agent.Topics.Count})";
        txtToolsTitle.Text = $"Tools ({agent.Tools.Count})";
        txtKnowledgeTitle.Text = $"Knowledge sources ({agent.KnowledgeSources.Count})";

        PopulateTopics(agent);
        PopulateTools(agent);
        PopulateKnowledge(agent);
    }

    private void PopulateTopics(AgentModel agent)
    {
        if (agent.Topics.Count == 0)
        {
            txtNoTopics.Visibility = Visibility.Visible;
            return;
        }

        foreach (var topic in agent.Topics)
        {
            var body = new StackPanel();

            if (topic.TriggerQueries.Count > 0)
            {
                body.Children.Add(Label("Trigger phrases"));
                var wrap = new ItemsControl
                {
                    ItemsSource = topic.TriggerQueries,
                    ItemTemplate = (DataTemplate)FindResource("PhrasePill"),
                };
                wrap.ItemsPanel = new ItemsPanelTemplate(
                    new System.Windows.FrameworkElementFactory(typeof(WrapPanel)));
                body.Children.Add(wrap);
            }

            if (topic.Messages.Count > 0)
            {
                body.Children.Add(Label("Messages", topMargin: topic.TriggerQueries.Count > 0 ? 8 : 0));
                foreach (var message in topic.Messages)
                    body.Children.Add(Bullet(message));
            }

            if (topic.ActionKinds.Count > 0)
            {
                body.Children.Add(Label("Actions", topMargin: 8));
                foreach (var kind in topic.ActionKinds.GroupBy(k => k))
                    body.Children.Add(Bullet(kind.Count() > 1 ? $"{kind.Key} ×{kind.Count()}" : kind.Key));
            }

            if (body.Children.Count == 0)
                body.Children.Add(Muted("This topic declares no trigger phrases, messages or actions."));

            var header = new StackPanel();
            header.Children.Add(new TextBlock
            {
                Text = topic.DisplayName.Length > 0 ? topic.DisplayName : topic.SchemaName,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("Text"),
            });
            header.Children.Add(new TextBlock
            {
                Text = Summary(topic),
                FontSize = 11,
                Foreground = (Brush)FindResource("Subtle"),
                Margin = new Thickness(0, 1, 0, 0),
            });

            listTopics.Items.Add(new Expander
            {
                Style = (Style)FindResource("TopicExpander"),
                Header = header,
                Content = body,
            });
        }
    }

    private static string Summary(AgentTopicModel topic)
    {
        var parts = new List<string>();
        if (topic.TriggerQueries.Count > 0) parts.Add($"{topic.TriggerQueries.Count} trigger phrase{(topic.TriggerQueries.Count == 1 ? "" : "s")}");
        if (topic.Messages.Count > 0) parts.Add($"{topic.Messages.Count} message{(topic.Messages.Count == 1 ? "" : "s")}");
        if (topic.ActionKinds.Count > 0) parts.Add($"{topic.ActionKinds.Count} action{(topic.ActionKinds.Count == 1 ? "" : "s")}");
        return parts.Count > 0 ? string.Join(", ", parts) : "no configuration read from the export";
    }

    private void PopulateTools(AgentModel agent)
    {
        if (agent.Tools.Count == 0)
        {
            txtNoTools.Visibility = Visibility.Visible;
            return;
        }

        foreach (var tool in agent.Tools)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(new TextBlock
            {
                Text = tool.Name.Length > 0 ? tool.Name : tool.SchemaName,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("Text"),
            });
            panel.Children.Add(Muted($"Kind: {tool.Kind}" +
                (tool.ParentComponent.Length > 0 ? $"  ·  part of {tool.ParentComponent}" : string.Empty)));
            if (tool.Description.Length > 0) panel.Children.Add(Body(tool.Description));
            listTools.Items.Add(panel);
        }
    }

    private void PopulateKnowledge(AgentModel agent)
    {
        if (agent.KnowledgeSources.Count == 0)
        {
            txtNoKnowledge.Visibility = Visibility.Visible;
            return;
        }

        foreach (var source in agent.KnowledgeSources)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(new TextBlock
            {
                Text = source.Name.Length > 0 ? source.Name : source.SchemaName,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("Text"),
            });
            panel.Children.Add(Muted($"Kind: {source.SourceKind}"));
            if (source.Site.Length > 0) panel.Children.Add(Body(source.Site));
            listKnowledge.Items.Add(panel);
        }
    }

    /// <summary>The first topic's expander, for the screenshot harness.</summary>
    public Expander? FirstTopicExpander =>
        listTopics.Items.Count > 0 ? listTopics.Items[0] as Expander : null;

    private TextBlock Label(string text, double topMargin = 0) => new()
    {
        Text = text,
        FontSize = 10.5,
        FontWeight = FontWeights.SemiBold,
        Foreground = (Brush)FindResource("PrimaryDark"),
        Margin = new Thickness(0, topMargin, 0, 5),
    };

    private TextBlock Body(string text) => new()
    {
        Text = text,
        FontSize = 12.5,
        Foreground = (Brush)FindResource("Text"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 0),
    };

    private TextBlock Muted(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = (Brush)FindResource("Muted"),
        TextWrapping = TextWrapping.Wrap,
    };

    private TextBlock Bullet(string text) => new()
    {
        Text = "•  " + text,
        FontSize = 12.5,
        Foreground = (Brush)FindResource("Text"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(4, 0, 0, 3),
    };
}
