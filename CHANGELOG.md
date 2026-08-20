# Changelog and project record

What Shieldsmith is, what was decided, what shipped, and what is honestly still missing.
Architecture and export-format facts live in `CLAUDE.md`; the product description lives in
`README.md`. This file is the history and the state.

## What it is

Shieldsmith documents Power Platform solution exports offline, reading the solution definition
from the zip on the machine it runs on. No environment connection, no admin rights, no upload.
It is Cameron's personal product, not Valto work.

It started from a LinkedIn post describing a documenter that drew a lot of interest and a lot of
requests for the tool. The post described features the prototype did not actually have: Graphviz
was built into a command string but never invoked, the "Visio XML" was a bespoke schema Visio
cannot open, and there was no AI integration anywhere. Making those claims true was Phase 1.

**Names, in order:** PowerPlatformDocumenter, then DocCam, then Shieldsmith (renamed
2026-08-20). Anything written before that date uses the older names. The repo folder is still
`powerplatform-documenter`, left deliberately.

## Decisions that stand

Made once, recorded here so they are not relitigated.

- **Personal, not Valto.** Cameron's own branding and commercials.
- **Evolve the prototype rather than start again.** .NET 10, because the machine has no .NET 8
  SDK. Self-contained publish, because a runtime prerequisite is a live complaint against
  PowerDocu.
- **AI in four shapes, always on the user's own access**: an Anthropic key, an OpenAI key, an
  installed Claude Code CLI driven headless, an export docpack for the user's own terminal, and
  an MCP server. Shieldsmith never holds a key of its own and never bills anyone.
- **AI output is labelled interpretation and never mixed with extracted fact.** It renders only
  in visually distinct captioned blocks, never in a fact table, and never to fill a parser gap.
  The document is complete with AI switched off.
- **Evidence over invention.** Anything the export does not settle becomes a diagnostic note in
  the output, not a plausible value. Inferred relationships are modelled separately from
  declared ones and always labelled inferred.
- **A missing engine is a supported state, not an error.** Graphviz, WebView2 and Claude Code
  are each optional, detected at runtime, and reported to the user before they are needed.
- **Apache 2.0, and open source.** Chosen over MIT for the explicit patent grant and the
  requirement that modified files be marked, and over AGPL because an employer ban on copyleft
  would cost more adoption than a closed fork would cost in revenue. `IEditionGate` in
  `Shieldsmith.Core` remains the only licensing seam if a paid edition is ever added; the code
  stays open either way.
- **Installing must be trivial.** A signed installer and a portable zip, both self-contained,
  per-user so there is no administrator prompt on a locked-down work machine. Needing a .NET
  runtime first, or needing to build from source, is the barrier this project set out to remove.

## Version history

### 0.9.0, 2026-08-20

Everything below was built on 2026-08-20, in phase order. It is one commit: the history was
squashed on 2026-08-20 because an early version of `CLAUDE.md` named a client and gave the path
to their solution export, and that name was present in every commit from Phase 5 onwards. The
data itself was never committed. Squashing was the cheapest fix while the repo had no remote,
and it is why the phases below carry no commit hashes.

**Baseline.** The prototype imported unchanged so the restructure has something to diff against.

**Phase 1: make the claimed features real.** Restructured into projects and
namespaces. Parser corrections against real export XML: entity scoping (the unscoped
`Descendants` call produced phantom entities), `RequiredLevel` as a token rather than the
always-false `== "1"` check, many-to-many as its own undirected type, publisher read properly,
environment variable definitions joined to values, the full component-type table. Unpacker
hardened: zip-slip guard, share-read open, disposable temp directories, startup sweep of
orphans. Graphviz actually invoked, with a built-in fallback layout so raw DOT is never the
answer. A real `.vsdx` written as a hand-built OPC package. Word gained a cover page, a table of
contents, the embedded diagram and per-table detail. The client zip sitting in `data/` was
removed and a synthetic fixture, ContosoTravel, authored to replace it.

**Phase 2: depth.** Cloud flows parsed from their clientdata JSON, with the trigger
decoded and the action tree ordered by `runAfter` rather than document order. Classic workflows,
business rules and business process flows. Forms down to tab, section and field. Views with
their columns. Option sets, security roles with a privilege matrix, model-driven apps, sitemap
navigation, web resources. A GUID-to-name resolver so root components show names. Markdown
output and the `generate` CLI verb.

**Phases 3 and 4: AI, the docpack and MCP.** `IAiProvider` with Anthropic, OpenAI and
Claude Code CLI implementations. DPAPI key storage. A consent dialogue that shows the exact
payload before anything leaves the machine, with environment variable values redacted by
default. A response cache keyed on the prompt version. The Claude Code docpack, and the stdio
MCP server on the official SDK.

**Phase 5: packaging.** Self-contained single-file publish for all three executables,
an Inno Setup installer, and a README that compares against PowerDocu honestly rather than
favourably.

**Phase 6: diagrams that actually render.** A built-in layered layout engine
(Sugiyama) with SVG and PNG renderers and no dependencies, so a good diagram never requires an
install. Real Mermaid rendered through a bundled `mermaid.min.js` in headless WebView2. A
flowchart per cloud flow. Three engines in preference order: Mermaid, built-in, Graphviz.

**Phase 7: the last three parity gaps.** Canvas app internals from the `.msapp`:
screens, control tree, authored formulas, data sources, derived variables. Copilot Studio agents
from their `bots/` folder and YAML payloads. Desktop flows with the Robin script parsed into
subflows and ordered steps. Verified against two real solutions, not only the fixture.

**Phase 8: the interface.** Rebuilt around a brand theme taken from
cameronshields.co.uk, with a control template for everything the app uses. Cards, a real empty
state, drag and drop, engine-availability pills, a live diagram preview over all three engines,
and tabs for the Phase 7 component types. MCP gained `get_canvas_app` and `get_agent`, desktop
flow detail inside `get_flow`, and the new types in `search`.

**Shieldsmith.** Renamed throughout: projects, namespaces, both executables, the MCP
server name and the docs. Brand assets applied. An About window added, stating the version, the
build date, the runtime, which engines this machine has, which solution is loaded, what the tool
produces, how to use it, and where the data does and does not go.

**Packaging.** Apache 2.0 licence and a NOTICE listing every third-party component, with the
licences read from the packages' own metadata rather than assumed. All three executables now
publish into one folder sharing a single copy of the .NET and WPF runtime: 174 MB rather than
the 439 MB that single-file-per-executable produced. Release builds ship no debug symbols and
no IntelliSense XML, so no local source paths leak into a public download. The installer version
is generated from `Directory.Build.props`; it had sat at 0.4.0 against a 0.9.0 application since
Phase 5. The installer was compiled and tested for the first time: silent install, run the
installed app and CLI end to end, add and remove the PATH entry, and uninstall.

**Business process flows, plugins, and diagrams that survive a real solution.** Both diagram
engines turned out to be broken on a hundred-table solution, because they had only ever been
tested against the four-table fixture. Mermaid did not fail: past its default limits it draws a
picture reading "Maximum text size in diagram exceeded" and returns it as a valid SVG, which was
being written out as a solution's entity relationship diagram. The built-in engine crashed
outright, because GDI+ throws from `new Bitmap()` long before it runs out of memory. Mermaid is
now opt-in and its error diagrams are detected as failures; the rasteriser scales down to fit a
pixel budget, falls back to the exact SVG, and one diagram failing no longer takes the rest with
it. Above 25 tables the ERD drops to table names and says so.

Business process flow stages and steps are now read from the workflow XAML, and plugin
assemblies, types and SDK message processing steps from `customizations.xml`. PowerDocu
documents neither. Both were written against real exports: a 16-stage flow, and an assembly with
three types and four registrations.

## What is verified, and how

- **65 unit tests** across Core, Outputs, Ai and Diagrams, all against the synthetic fixture.
- **Word output validates clean** under `OpenXmlValidator`, checked by the `validate-docx` CLI
  verb rather than by eye.
- **Diagrams are checked by opening the rendered image.** Every Mermaid bug and every layout bug
  found so far returned success and produced a wrong picture. Trusting the return value would
  have shipped all six.
- **The interface is checked by rendering it.** `Shieldsmith.exe <zip> --shot <path.png>` writes
  a PNG per tab, per diagram, and of the About and consent windows. Four defects were found this
  way and none of them failed the build.
- **MCP is driven end to end over stdio**, sequentially, as a real client does. Firing the
  requests concurrently produces answers in the wrong order and proves nothing.
- **The installer is installed, not just compiled.** Silent install to a scratch directory, the
  installed app and CLI both run and produce real documents, the PATH entry is added when the
  task is chosen and the user's PATH is left untouched when it is not, and uninstall removes the
  directory and restores PATH byte for byte. The PATH was backed up before that test, because
  the uninstall code that edits it was newly written and untested.
- **Canvas app parsing was verified against two real solutions**, not just the fixture: a
  four-screen app reporting 321 controls, seven tables and four variables, and a second
  correctly identified as a component library.

Not verified, and needing a person:

- **Visio.** The `.vsdx` passes structural tests but Visio is not installed on the dev machine.
  Open a generated file in real Visio once before shipping any change to the writer.
- **The Anthropic and OpenAI key paths.** Unit-tested with a fake provider only. The Claude Code
  CLI path has been exercised against a real install. Run one real request against each before a
  release.

## Still open

- **AI Builder models.** The one place PowerDocu is still ahead.
- **Code signing.** Needed from the first public release. SmartScreen blocking unsigned builds
  is a live complaint against PowerDocu and the first thing a new user hits.
- **Licence and distribution model.** Undecided by choice.
- **The repo folder name**, still `powerplatform-documenter`.

## Notes for whoever picks this up

Renaming to Shieldsmith moved the key store to `%APPDATA%\Shieldsmith` and changed the DPAPI
entropy string, so API keys saved under the old name cannot be read and must be entered again.
This degrades to the normal first-run state rather than an error. An MCP registration pointing
at the old executable needs replacing; the app's "Copy MCP registration" button gives the
current command.
