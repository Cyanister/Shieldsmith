# Shieldsmith

Documentation for Power Platform solutions, straight from the export zip.

Shieldsmith reads `solution.xml`, `customizations.xml` and the definition folders inside a solution
export and produces the documentation nobody was otherwise going to write: every component,
every table with its columns and requirement levels, every relationship with its cardinality,
an entity relationship diagram, a Word document, and a real Visio `.vsdx` file. No environment
connection, no admin rights. Just the file that got emailed to you.

Then, optionally, it hands the same extraction to AI: your own Claude or ChatGPT API key, your
installed Claude Code, a folder you open in your own Claude Code terminal, or an MCP server you
query live.

The principle underneath it: **read the definition, not the documentation.** The solution file
is what is actually true.

Apache 2.0 licensed. Windows x64.

## Install

Two ways in, both self-contained. There is no .NET runtime to install first, which is the
single most common complaint against the alternatives.

**Installer.** Download `Shieldsmith-<version>-setup.exe` and run it. About 53 MB to download,
178 MB installed. It installs for you alone, so there is **no administrator prompt** and it
works on a locked-down work machine. You get a Start Menu entry, an optional desktop shortcut,
and an optional PATH entry for the command line tool. Uninstall from Add or Remove Programs
removes everything it added, including the PATH entry.

**Portable zip.** Download `Shieldsmith-<version>-win-x64-portable.zip`, unzip it anywhere, and
run `Shieldsmith.exe`. Nothing is written to the registry and nothing is installed. Use this if
you cannot install software at all, or want to run it from a USB stick.

Windows will warn you about an unrecognised publisher until the release is code signed. That is
being fixed; see [Still open](#still-open).

### Then

1. Export a solution from Power Platform, managed or unmanaged.
2. Drag the `.zip` onto the Shieldsmith window and press **Analyse solution**.
3. Read the results, then write a Word document, a Markdown set, a Visio diagram, or a docpack
   for Claude Code.

Nothing leaves your machine unless you deliberately turn AI on, and even then only after a
dialogue has shown you the exact payload.

## What it produces

**Extracted fact**

- **Component inventory**, typed, with GUIDs resolved to names wherever the export allows.
- **Tables**: display name, logical name, type, requirement level (parsed from the real export
  tokens), primary key and primary name markers, alternate keys, forms down to tab, section and
  field level with their script libraries, and views with their columns.
- **Relationships** from the solution-level `EntityRelationships` block: one-to-many with the
  lookup column and cascade behaviour, many-to-many with the intersect table named, plus
  implicit lookup relationships (owner, customer, lookups declared outside the solution) which
  are always labelled as inferred rather than presented as declared fact.
- **Automations**: cloud flows parsed from their clientdata JSON (trigger decoded, ordered
  action tree including Scope, If, Switch and Foreach nesting, connectors, connection
  references), plus classic workflows, business rules, actions and business process flows.
- **Choices, security roles, model-driven apps, sitemap navigation, web resources,
  environment variables** with definitions and values joined and secrets never printed.

**Outputs**

- **ERD** as Graphviz DOT, rendered to PNG and SVG when Graphviz is installed, or with the
  built-in layout engine when it is not. Raw DOT is never shown as the answer.
- **Word document**: cover page, table of contents that fills in on open, the ERD embedded,
  per-table detail, relationships, automations, roles, apps, and a parsing-notes section stating
  anything the export did not fully settle.
- **Markdown**: one file per table and per cloud flow plus an index, suitable for a GitHub or
  Azure DevOps wiki.
- **Visio** `.vsdx`: a genuine OPC package Visio opens, with entity shapes and relationship
  connectors positioned to match the ERD.
- **Canonical JSON**: the complete extraction, and the shared substrate for everything below.

**AI, four ways, all using your own access**

1. **Your Anthropic or OpenAI API key.** Pick a provider in the app, paste your key once (stored
   encrypted with Windows DPAPI), and Shieldsmith adds plain-English interpretation to the Word and
   Markdown output.
2. **Your installed Claude Code.** Shieldsmith detects the `claude` CLI and drives it headless, so
   there is no key to manage and no separate bill.
3. **A docpack for your own Claude Code terminal.** "Export for Claude Code" writes a folder
   containing the canonical JSON, the full Markdown documentation, a `CLAUDE.md` with grounding
   rules, and a question-answering skill. Open it and ask questions about the solution.
4. **An MCP server.** `Shieldsmith.Mcp.exe` speaks MCP over stdio and exposes `load_solution`,
   `get_entity`, `get_flow`, `get_canvas_app`, `get_agent`, `get_relationships`, `search` and
   more, so Claude Code queries a parsed solution live. `get_canvas_app` returns the app
   overview by default and a screen's full control tree only when you name a screen, so a
   321-control app cannot flood the context by accident.

**How AI content is treated.** It renders only inside visually distinct blocks captioned with
the provider, model and date, never inside fact tables, and never fills a parser gap. Nothing
leaves your machine without a consent dialogue that shows the exact payload first. Environment
variable values are redacted by default. The document is complete with AI switched off.

## Compared with PowerDocu

[PowerDocu](https://github.com/modery/PowerDocu) is the established free tool in this space and
is genuinely good, particularly on canvas apps. An honest comparison:

| | Shieldsmith | PowerDocu |
| --- | --- | --- |
| Runs offline from the export zip | Yes | Yes |
| Dataverse table detail | Columns, types, requirement levels, keys, forms, views | Internal column names |
| Entity relationship diagram | Yes, with implicit lookups labelled | No |
| Visio output | Real `.vsdx` | No |
| AI interpretation | Four options, all your own access | None |
| MCP server / Claude Code pack | Yes | No |
| Canvas app internals | Screens, control tree, authored formulas, data sources, variables | Screens, controls, properties |
| Flow diagrams as images | Yes, Mermaid or the built-in engine | Yes |
| Mermaid source you can paste anywhere | Yes | No |
| Copilot Studio agents | Topics, trigger phrases, tools, knowledge | Yes |
| Desktop flows | Robin script parsed into subflows and steps | Yes |
| AI models (AI Builder) | No | Yes |
| Runtime required | None (self-contained) | .NET runtime |

PowerDocu remains ahead on AI Builder models. Its canvas app coverage is also longer-established
than Shieldsmith's, which was written against two real exports and the synthetic fixture.

## Projects

```
src/Shieldsmith.Core        parsing, object model, canonical JSON; no UI, no network
src/Shieldsmith.Diagrams    the built-in layered layout engine (SVG and PNG, no dependencies)
src/Shieldsmith.Mermaid     Mermaid rendered for real through headless WebView2
src/Shieldsmith.Outputs     Word, Markdown, DOT/Graphviz, VSDX writers, the diagram suite
src/Shieldsmith.Ai          IAiProvider + Anthropic/OpenAI/ClaudeCodeCli, DPAPI key store, cache
src/Shieldsmith.ExportPack  the Claude Code docpack generator
src/Shieldsmith.Mcp         stdio MCP server
src/Shieldsmith.Cli         shieldsmith.exe
src/Shieldsmith.App         WPF desktop app
tests/                 xunit suites for Core, Outputs and Ai
data/                  ContosoTravel_sample.zip, the synthetic fixture (no client data)
build/                 publish script and Inno Setup installer script
```

.NET 10. Graphviz is optional and detected at runtime (`GRAPHVIZ_DOT`, then PATH, then Program
Files).

## Build, test, run

```
dotnet build Shieldsmith.slnx
dotnet test Shieldsmith.slnx
dotnet run --project src/Shieldsmith.Cli -- analyze data/ContosoTravel_sample.zip
```

Command line:

```
shieldsmith generate <solution.zip> -o <dir> [--word] [--markdown] [--erd] [--vsdx] [--json]
                                        [--ai anthropic|openai|claude-code] [--ai-model <model>]
shieldsmith export-pack <solution.zip> [outdir]     folder for your own Claude Code terminal
shieldsmith diagram <solution.zip> [--no-mermaid] [--no-flows]
shieldsmith analyze|json|erd|vsdx|word <solution.zip>
shieldsmith validate-docx <file.docx>               OpenXML validation, useful in CI
shieldsmith check                                   what this machine can do: Graphviz, WebView2,
                                               a live Mermaid render, Claude Code
```

API keys for the CLI come from `ANTHROPIC_API_KEY` or `OPENAI_API_KEY`. Passing `--ai` is your
consent to send the extracted structure to that provider.

Register the MCP server with Claude Code:

```
claude mcp add shieldsmith -- <install-dir>\Shieldsmith.Mcp.exe
```

## Building a release

```
powershell -File build/publish.ps1              # payload + portable zip + version.iss
build/sign.ps1 -CertificateThumbprint <hash>    # sign every exe (see the script for options)
iscc build/Shieldsmith.iss                      # installer, into artifacts/
```

`publish.ps1` puts all three executables in **one** folder so the .NET and WPF runtime ships
once rather than three times. Published as single-file executables instead, the same payload is
439 MB rather than 174 MB, which is a lot to ask someone to download for a documentation tool.

Because they share a folder and Windows file names are case-insensitive, the CLI is
`shieldsmith-cli.exe` and a `shieldsmith.cmd` shim sits beside it, so `shieldsmith` is still
what you type.

The installer version is read from `Directory.Build.props` via a generated `build/version.iss`.
It is not typed into the installer script, because when it was it sat at 0.4.0 against a 0.9.0
application for four phases.

Sign before any public release. Unsigned builds trigger SmartScreen, which is a live complaint
against PowerDocu and the first thing a new user hits.

## The desktop app

`Shieldsmith.exe` is the same pipeline with an interface: drop a solution zip anywhere on the window
(or pass one on the command line), analyse, then read the results as tables and diagrams and
write whichever outputs you want. The header states what the machine can actually do before you
ask it to: whether Mermaid can render, whether Graphviz is installed, whether Claude Code is on
the PATH. A missing engine is a supported state, never an error.

The diagram pane shows the whole diagram when that stays readable and fits the width and scrolls
when it would not, which is almost always the case for an entity relationship diagram. Every
diagram's Mermaid source can be copied straight to the clipboard.

**About** (top right) states the version, the date this copy was built, the runtime, which
diagram and AI engines this machine actually has, which solution is currently loaded, what
Shieldsmith produces, how to use it, and exactly where your data does and does not go.

`Shieldsmith.exe <solution.zip> --shot <path.png>` writes one PNG per results tab, per diagram,
and of the About and consent windows, then exits. That is how the interface is checked: by
looking at it, not by trusting that the XAML compiled.

## Still open

- **Code signing.** The release is not signed yet, so Windows shows an unrecognised publisher
  warning. `build/sign.ps1` is ready and documents both routes; it needs a certificate, and
  Azure Trusted Signing is the recommended one because reputation builds against Microsoft's
  root rather than a fresh certificate.
- **winget.** `winget install Shieldsmith` needs a signed installer at a public URL first, so it
  follows the first signed release rather than shipping with it.
- **AI Builder models.** The one component type PowerDocu documents and Shieldsmith does not.
- **Visio verification.** The `.vsdx` passes structural tests, but Visio is not installed on the
  development machine, so open one in real Visio before shipping a change to the writer.

Licensed under Apache 2.0. `IEditionGate` in `Shieldsmith.Core` remains the single seam if a
paid edition is ever added; the code stays open either way.
