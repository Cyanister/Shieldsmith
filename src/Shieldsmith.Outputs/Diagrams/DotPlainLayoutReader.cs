using System.Globalization;

namespace Shieldsmith.Outputs.Diagrams;

/// <summary>
/// Parses "dot -Tplain" output into an ErdLayout. The plain format is line
/// based: graph, node, edge, stop. All coordinates are in inches with the
/// origin at the bottom left.
/// </summary>
public static class DotPlainLayoutReader
{
    public static ErdLayout Parse(string plainOutput)
    {
        var layout = new ErdLayout();

        foreach (var line in plainOutput.Split('\n'))
        {
            var tokens = Tokenize(line.TrimEnd('\r'));
            if (tokens.Count == 0) continue;

            switch (tokens[0])
            {
                case "graph" when tokens.Count >= 4:
                    layout.Width = ParseDouble(tokens[2]);
                    layout.Height = ParseDouble(tokens[3]);
                    break;

                case "node" when tokens.Count >= 6:
                    layout.Nodes.Add(new ErdNode
                    {
                        Id = tokens[1],
                        X = ParseDouble(tokens[2]),
                        Y = ParseDouble(tokens[3]),
                        Width = ParseDouble(tokens[4]),
                        Height = ParseDouble(tokens[5]),
                    });
                    break;

                case "edge" when tokens.Count >= 3:
                    layout.Edges.Add(new ErdEdge
                    {
                        FromId = tokens[1],
                        ToId = tokens[2],
                    });
                    break;
            }
        }

        return layout;
    }

    private static double ParseDouble(string token) =>
        double.Parse(token, CultureInfo.InvariantCulture);

    /// <summary>Splits a plain-format line into tokens, honouring double-quoted strings.</summary>
    private static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var index = 0;
        while (index < line.Length)
        {
            while (index < line.Length && line[index] == ' ') index++;
            if (index >= line.Length) break;

            if (line[index] == '"')
            {
                index++;
                var start = index;
                var buffer = new System.Text.StringBuilder();
                while (index < line.Length && line[index] != '"')
                {
                    if (line[index] == '\\' && index + 1 < line.Length) index++;
                    buffer.Append(line[index]);
                    index++;
                }
                index++; // closing quote
                tokens.Add(buffer.ToString());
                _ = start;
            }
            else
            {
                var start = index;
                while (index < line.Length && line[index] != ' ') index++;
                tokens.Add(line[start..index]);
            }
        }
        return tokens;
    }
}
