# Promotion / distribution checklist

Where this project should be listed so people can actually find it, and the exact
text to submit. Keep the entries in sync when the tool count or tagline changes.

## Distribution status (release channels checked 2026-10-25; external requests checked 2026-10-24)

| Channel | Status |
| --- | --- |
| [GitHub Releases](https://github.com/faze79/WPFVisualTreeMcp/releases/tag/v0.19.0) and [MCP/CLI NuGet](https://www.nuget.org/packages/WpfVisualTreeMcp/0.19.0) | Stable v0.19.0 published with portable, full/compact Windows x64 MCPB and Codex plugin assets. [Release CI](https://github.com/faze79/WPFVisualTreeMcp/actions/runs/40171888590) and [Build CI](https://github.com/faze79/WPFVisualTreeMcp/actions/runs/40171888598) passed, including installed SDK consumers, the framework/architecture matrix and workflow/xUnit round trips. Public NuGet tool installation and CLI startup verified. All 79 executable/assembly payload files match the tested GitHub release. |
| [C# automation SDK on NuGet](https://www.nuget.org/packages/WpfVisualTreeMcp.Automation/0.19.0) | `WpfVisualTreeMcp.Automation` 0.19.0 published and installed from nuget.org into a fresh .NET 8 consumer. All 34 executable/assembly payload files match the tested release. Live package tests passed against .NET 8 x64 and .NET Framework 4.7.2 x86: virtualized grids/trees, validation, automatic waits, state operations, push notifications, modal windows, recording/replay and process lifecycle. Local tests: 262 unit + 38 WPF tests passed; the opt-in matrix passed in CI. |
| Official MCP Registry | v0.19.0 published as `io.github.faze79/wpf-visual-tree`; [publication workflow](https://github.com/faze79/WPFVisualTreeMcp/actions/runs/40172529673) succeeded. Public API confirms `active` and `isLatest: true`. The workflow waits for NuGet indexing before publication. |
| [Glama](https://glama.ai/mcp/servers/faze79/WPFVisualTreeMcp) | Description and repository README synchronized for the advanced C# SDK. Catalog release **0.19.0** is **Latest**; the [successful build](https://glama.ai/mcp/servers/faze79/WPFVisualTreeMcp/admin/dockerfile/tests/01a171f8-cad0-7620-99ad-61d06b140434) installs NuGet server **0.19.0** and passes MCP initialization/tools introspection. The automatic build started before NuGet indexing; retrying after public installation succeeded. Published catalog version 0.19.0 from the verified build. |
| [awesome-mcp-servers](https://github.com/punkpeye/awesome-mcp-servers#os-automation) | Original [PR #9934](https://github.com/punkpeye/awesome-mcp-servers/pull/9934) merged on 2026-07-22. [PR #17013](https://github.com/punkpeye/awesome-mcp-servers/pull/17013) remains open for the description update. [Follow-up posted as faze79 on 2026-10-24](https://github.com/punkpeye/awesome-mcp-servers/pull/17013#issuecomment-6388117753) addresses the automated duplicate warning (one existing line replaced) and confirms the existing Glama badge and live 38-tool schema. Awaiting maintainer review. |
| [HVTracker](https://hvtracker.net/agents/wpfvisualtreemcp/) | [Correction #372](https://github.com/YugantM/hvtracker/issues/372) remains open. The profile still says no registry package. [Follow-up posted as faze79 on 2026-10-24](https://github.com/YugantM/hvtracker/issues/372#issuecomment-6388333638) provides verified NuGet v0.17.0 and Official MCP Registry evidence (`active`, `isLatest: true`) and requests the package association. No maintainer response yet. |
| [PulseMCP](https://www.pulsemcp.com/servers/faze79-wpf-visual-tree) | On 2026-10-24, the [submission page](https://www.pulsemcp.com/submit) still explicitly pauses new submissions and changes to existing listings; it says it will ingest the Official MCP Registry when processing resumes. v0.19.0 is now active/latest in that registry. The directory's [cached manifest](https://www.pulsemcp.com/servers/faze79-wpf-visual-tree/serverjson) last checked on 2026-10-19 showed v0.12.0. |
| mcp.so | On 2026-10-24 the prepared form still requires a **39 USD one-time fee**. No payment authorized or listing published. |
| Smithery | No listing published. The last API attempt (2026-10-19) returned HTTP 403, `Updating servers has been removed`, and lookup/release submission returned HTTP 404. On 2026-10-24, sign-in as faze79 succeeded, but the web publishing form still requires an HTTP MCP URL; it cannot accept this local stdio server. The [service announcement](https://smithery.ai/future) confirms accounts, API, CLI and proxy shutdown on 2026-10-31, with a curated public directory remaining. The compact [MCPB bundle](MCPB.md) remains available through GitHub Releases. |

For request follow-ups, check these external threads as well as this repository's
issues and PRs. Use the `faze79` GitHub account and verify the active login before
posting. An empty local issue/PR queue does not mean distribution requests are closed.

## Canonical pitch

> **Let AI agents see, debug and drive running WPF apps.** Snoop + Playwright for AI,
> exposed over 38 MCP tools, matching CLI commands and a C# automation SDK: visual trees, bindings,
> diagnostic reports, full-content screenshots, mouse hover/wheel/dragging, native touch,
> and workflow recording/replay with runnable xUnit export. Supports WPF apps on
> .NET Framework 4.7.2/4.8 and .NET 8+ through runtime injection or self-hosting.
> Target actions by AutomationId and await exact text/property values from C# tests.
> Includes CLI self-update and a Codex companion skill. Native touch behavior
> depends on the application's input support.

Category positioning: **OS Automation** / **Developer Tools**. The differentiator is
that UIA-based tools (FlaUI, WinAppDriver) and computer-use agents only see the
accessibility view or raw pixels; this exposes the *WPF-internal* view — visual tree,
dependency properties, bindings, DataContext — the way Snoop does, plus the hands to act.

## 1. Official MCP registry — registry.modelcontextprotocol.io

Manifest lives at `src/WpfVisualTreeMcp.Server/.mcp/server.json`
(namespace `io.github.faze79/wpf-visual-tree`).

**Prerequisites:**
1. The NuGet package version referenced in the manifest must already be **live on
   nuget.org** — the registry validates that the package exists.
2. The published package's README must contain the ownership token
   `<!-- mcp-name: io.github.faze79/wpf-visual-tree -->` (already in `README.md`, which
   ships in the package via `PackageReadmeFile`). The registry reads it to confirm the
   NuGet package belongs to this server name. Keep the token's server name in sync with
   `server.json` `name`.

Then run the **"Publish to MCP Registry"** workflow (Actions tab → Run workflow).
It waits for the exact NuGet version to become publicly available before publishing.
It authenticates with GitHub OIDC — no secrets, no tokens: publishing under
`io.github.faze79/*` is authorized simply by the workflow running in a repo owned by faze79.

## 2. NuGet.org (trusted publishing / OIDC — no API key)

The release workflow publishes both `WpfVisualTreeMcp` (MCP/CLI tool) and
`WpfVisualTreeMcp.Automation` (C# SDK) via **NuGet trusted publishing**: GitHub Actions gets a
short-lived token over OIDC, so there is no long-lived API key to store or rotate. Works
for a brand-new package (the first push creates `WpfVisualTreeMcp` on nuget.org), and the
repo is public so there is no 7-day activation window.

One-time setup (already configured for this repository; retained for reference):

1. **Create the trusted-publishing policy** at
   <https://www.nuget.org/account/trustedpublishing> → **Add**:
   - Package owner: your nuget.org account
   - Package scope: authorize both `WpfVisualTreeMcp` and `WpfVisualTreeMcp.Automation`.
     If configuring a new policy, ensure its package scope covers the SDK as well as
     the MCP tool; both were successfully published using the existing setup in v0.18.0.
   - Repository owner: `faze79`
   - Repository: `WPFVisualTreeMcp`
   - Workflow file: `release.yml`
   - Environment: *(leave blank — the workflow uses no `environment:`)*
2. **Add the repo secret** `NUGET_USER` = your nuget.org **profile name** (not email),
   at Settings → Secrets and variables → Actions.
3. Push a `v*` tag — the workflow packs and tests both packages, does `NuGet/login@v1`
   (OIDC), pushes to nuget.org and attaches both `.nupkg` files to the GitHub release.
   Until `NUGET_USER` is set the push step
   is skipped with a notice, so tagging still produces the GitHub release.

Users then install with `dotnet tool install -g WpfVisualTreeMcp` (gives the `wpfinspect`
command) or run the MCP server with `dnx WpfVisualTreeMcp`. Test projects add the SDK
with `dotnet add package WpfVisualTreeMcp.Automation --version 0.19.0`; no MCP server
process is needed for the C# API.

## 3. awesome-mcp-servers (punkpeye/awesome-mcp-servers)

The project is already listed under **OS Automation**. Update its existing line
through a PR from the `faze79/awesome-mcp-servers` fork; do not add a duplicate.

Legend used by that list: `#️⃣` = C# codebase, `🏠` = local service, `🪟` = Windows.

Current proposed entry (PR #17013):

```markdown
- [faze79/WPFVisualTreeMcp](https://github.com/faze79/WPFVisualTreeMcp) [![faze79/WPFVisualTreeMcp MCP server](https://glama.ai/mcp/servers/faze79/WPFVisualTreeMcp/badges/score.svg)](https://glama.ai/mcp/servers/faze79/WPFVisualTreeMcp) #️⃣ 🏠 🪟 - Inspect and automate running WPF apps without source changes: visual trees, binding diagnostics, full-content screenshots, mouse and touch input, plus workflow recording, replay and xUnit test export.
```

Keep the existing Glama badge and platform markers. Follow the upstream
`CONTRIBUTING.md` when opening subsequent updates.

## 4. Other directories

- **mcp.so** — <https://mcp.so/submit>. As of 2026-10-24 the form requires a
  39 USD one-time payment. Do not treat a prepared form as a published listing.
- **Glama** — [existing listing](https://glama.ai/mcp/servers/faze79/WPFVisualTreeMcp).
  Sign in with the repository owner's GitHub account (`faze79`) to manage it.
  The check builds a Docker image and verifies the server starts and answers `tools/list`
  — it does NOT need the app to actually inject (that's Windows-only). This repo ships a
  [`Dockerfile`](../Dockerfile) that installs the published NuGet tool and runs it as the
  stdio MCP server, which is all the check needs.
  1. [Listing administration](https://glama.ai/mcp/servers/faze79/WPFVisualTreeMcp/admin/profile):
     update the short description when capabilities change.
  2. **Repository → Sync Server**: refresh README, repository metadata and the head commit.
  3. **Dockerfile → Build & Release**: build the published NuGet tool and inspect
     its MCP schema. Wait until the exact package is available in NuGet's public
     feed. Glama's build step now reads `version` from
     `src/WpfVisualTreeMcp.Server/.mcp/server.json` and passes it to
     `dotnet tool install --version`, so publication delays cannot silently
     install an older server. A successful metadata sync alone does not update
     the tools; verify the actual server version in the build/handshake logs.
  4. Wait for release publication after the successful build: the UI can briefly
     show the previous version/schema while background processing completes.
     **Build & Release** may increment Glama's previous catalog version instead
     of using the NuGet version. Use **Build**, then the manual release form when
     the catalog version needs to match the package version exactly.
     For a build that still needs manual publication, use **Make Release** /
     **Create Release**, enter the package version and release notes, then
     **Create & Publish Release**. See Glama's
     [release guide](https://glama.ai/blog/2026-03-15-how-to-make-a-release).
  5. Verify the expected version appears under **Releases**,
     and the public schema lists all tools (38 in v0.18.0). Auto-release is enabled,
     but its completion must still be checked after each GitHub release.
- **PulseMCP** — [existing listing](https://www.pulsemcp.com/servers/faze79-wpf-visual-tree).
  Keep the Official MCP Registry manifest current. As of 2026-10-24, its
  [submission page](https://www.pulsemcp.com/submit) explicitly pauses both new
  submissions and changes to existing listings; check again when intake resumes.
- **Smithery** — <https://smithery.ai/servers/new>. Treat this server as local-only:
  WPF interaction needs Windows and the local desktop. The web form accepts HTTP
  endpoints. The [MCPB availability notes](MCPB.md#smithery-availability) record
  the current API restriction and CLI schema incompatibility. Recheck availability
  before submission: Smithery [announces](https://smithery.ai/future)
  that its proxy, managed identity and accounts stop working on 2026-10-31,
  while the public registry remains available.
- **HVTracker** — [profile](https://hvtracker.net/agents/wpfvisualtreemcp/).
  Repository metadata is collected by the directory. Report factual gaps using
  its [correction process](https://hvtracker.net/correct/), with public package,
  manifest or release links as evidence. A submitted correction is pending until
  the directory incorporates it.

## 5. Where the WPF audience actually is

This is an enterprise .NET crowd, not Hacker News:

- **r/dotnet**, **r/csharp** — a demo GIF plus the story of what the agent found
- **LinkedIn** — the same, aimed at teams maintaining legacy WPF line-of-business apps
- **dev.to / Medium** — the long-form write-up (see `docs/blog/`)
- **Awesome lists**: awesome-dotnet, awesome-wpf (dev-tools sections)

Lead with the *outcome* ("Claude found a binding typo in our WPF app by reading its
runtime binding errors"), not the mechanism ("MCP server for visual tree inspection").
