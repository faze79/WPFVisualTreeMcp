# Promotion / distribution checklist

Where this project should be listed so people can actually find it, and the exact
text to submit. Keep the entries in sync when the tool count or tagline changes.

## Distribution status (checked 2026-10-19)

| Channel | Status |
| --- | --- |
| [GitHub Releases](https://github.com/faze79/WPFVisualTreeMcp/releases/tag/v0.16.0) and [NuGet](https://www.nuget.org/packages/WpfVisualTreeMcp/0.16.0) | v0.16.0 published. |
| Official MCP Registry | v0.16.0 published as `io.github.faze79/wpf-visual-tree`. |
| [Glama](https://glama.ai/mcp/servers/faze79/WPFVisualTreeMcp) | Description and README updated; v0.16.0 published as **Latest**. The [public schema](https://glama.ai/mcp/servers/faze79/WPFVisualTreeMcp/schema) lists all 36 tools, including drag, touch, diagnostics and workflow recording/replay/export. Build and release publication were verified separately. |
| [awesome-mcp-servers](https://github.com/punkpeye/awesome-mcp-servers#os-automation) | Original [PR #9934](https://github.com/punkpeye/awesome-mcp-servers/pull/9934) merged on 2026-07-22. [PR #17013](https://github.com/punkpeye/awesome-mcp-servers/pull/17013) updates the description for workflows, touch and full-content screenshots. |
| [HVTracker](https://hvtracker.net/agents/wpfvisualtreemcp/) | Profile exists; [correction #372](https://github.com/YugantM/hvtracker/issues/372) requests the missing NuGet package association and refreshed metadata. Awaiting its maintainer. |
| [PulseMCP](https://www.pulsemcp.com/servers/faze79-wpf-visual-tree) | Existing listing found through its internal search. Its [cached manifest](https://www.pulsemcp.com/servers/faze79-wpf-visual-tree/serverjson) still shows v0.12.0. The directory has [paused submissions and listing changes](https://www.pulsemcp.com/submit); it says it will ingest the Official MCP Registry when processing resumes. v0.16.0 is already published there. |
| mcp.so, Smithery | No existing project listing found through internal searches (project name on mcp.so; `namespace:faze79` on Smithery). Do not treat these as published or automatically updated by a release. |

## Canonical pitch

> **Let AI agents see, debug and drive running WPF apps.** Snoop + Playwright for AI,
> exposed over 36 MCP tools and matching CLI commands: visual trees, bindings,
> diagnostic reports, full-content screenshots, mouse dragging, native touch,
> and workflow recording/replay with runnable xUnit export. Supports WPF apps on
> .NET Framework 4.7.2/4.8 and .NET 8+ through runtime injection or self-hosting.
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
It authenticates with GitHub OIDC — no secrets, no tokens: publishing under
`io.github.faze79/*` is authorized simply by the workflow running in a repo owned by faze79.

## 2. NuGet.org (trusted publishing / OIDC — no API key)

The release workflow publishes via **NuGet trusted publishing**: GitHub Actions gets a
short-lived token over OIDC, so there is no long-lived API key to store or rotate. Works
for a brand-new package (the first push creates `WpfVisualTreeMcp` on nuget.org), and the
repo is public so there is no 7-day activation window.

One-time setup (already configured for this repository; retained for reference):

1. **Create the trusted-publishing policy** at
   <https://www.nuget.org/account/trustedpublishing> → **Add**:
   - Package owner: your nuget.org account
   - Package ID: `WpfVisualTreeMcp` (an exact ID that doesn't exist yet is fine — the "glob"
     field can be left as the exact ID)
   - Repository owner: `faze79`
   - Repository: `WPFVisualTreeMcp`
   - Workflow file: `release.yml`
   - Environment: *(leave blank — the workflow uses no `environment:`)*
2. **Add the repo secret** `NUGET_USER` = your nuget.org **profile name** (not email),
   at Settings → Secrets and variables → Actions.
3. Push a `v*` tag — the workflow packs, does `NuGet/login@v1` (OIDC), pushes to nuget.org
   and attaches the `.nupkg` to the GitHub release. Until `NUGET_USER` is set the push step
   is skipped with a notice, so tagging still produces the GitHub release.

Users then install with `dotnet tool install -g WpfVisualTreeMcp` (gives the `wpfinspect`
command) or run the MCP server with `dnx WpfVisualTreeMcp`.

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

- **mcp.so** — <https://mcp.so/submit>
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
     its MCP schema. A successful metadata sync alone does not update the tools.
  4. Wait for release publication after the successful build: the UI can briefly
     show the previous version/schema while background processing completes.
     For a build that still needs manual publication, use **Make Release** /
     **Create Release**, enter the package version and release notes, then
     **Create & Publish Release**. See Glama's
     [release guide](https://glama.ai/blog/2026-03-15-how-to-make-a-release).
  5. Verify the expected version appears under **Releases**,
     and the public schema lists all tools (36 in v0.16.0). Auto-release is enabled,
     but its completion must still be checked after each GitHub release.
- **PulseMCP** — [existing listing](https://www.pulsemcp.com/servers/faze79-wpf-visual-tree).
  Keep the Official MCP Registry manifest current. As of 2026-10-19, its
  [submission page](https://www.pulsemcp.com/submit) explicitly pauses both new
  submissions and changes to existing listings; check again when intake resumes.
- **Smithery** — <https://smithery.ai/servers/new>. Treat this server as local-only:
  WPF interaction needs Windows and the local desktop. Recheck the publishing
  procedure before submission: Smithery [announces](https://smithery.ai/future)
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
