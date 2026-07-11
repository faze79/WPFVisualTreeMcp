# Promotion / distribution checklist

Where this project should be listed so people can actually find it, and the exact
text to submit. Keep the entries in sync when the tool count or tagline changes.

## Canonical pitch

> **Let AI agents see, debug and drive running WPF apps.** Snoop + Playwright for AI,
> exposed over MCP — visual tree, data bindings and binding errors, screenshots
> (popups included), clicks, item selection, text input and keyboard shortcuts,
> against any running WPF process, no source changes needed.

Category positioning: **OS Automation** / **Developer Tools**. The differentiator is
that UIA-based tools (FlaUI, WinAppDriver) and computer-use agents only see the
accessibility view or raw pixels; this exposes the *WPF-internal* view — visual tree,
dependency properties, bindings, DataContext — the way Snoop does, plus the hands to act.

## 1. Official MCP registry — registry.modelcontextprotocol.io

Manifest lives at `src/WpfVisualTreeMcp.Server/.mcp/server.json`
(namespace `io.github.faze79/wpf-visual-tree`).

**Prerequisite:** the NuGet package version referenced in the manifest must already be
live on nuget.org — the registry validates that the package exists and embeds a matching
`.mcp/server.json`.

Then run the **"Publish to MCP Registry"** workflow (Actions tab → Run workflow).
It authenticates with GitHub OIDC — no secrets, no tokens: publishing under
`io.github.faze79/*` is authorized simply by the workflow running in a repo owned by faze79.

## 2. NuGet.org

The release workflow packs and pushes automatically, but needs a repo secret:

1. Create an API key at <https://www.nuget.org/account/apikeys> (scope: push new packages
   and package versions, glob `WpfVisualTreeMcp*`).
2. Add it as the repo secret **`NUGET_API_KEY`** (Settings → Secrets and variables → Actions).
3. Push a `v*` tag — the workflow packs, pushes to nuget.org and attaches the `.nupkg`
   to the GitHub release. Without the secret the push step is skipped with a notice.

Users then install with `dotnet tool install -g WpfVisualTreeMcp` (gives the `wpfinspect`
command) or run the MCP server with `dnx WpfVisualTreeMcp`.

## 3. awesome-mcp-servers (punkpeye/awesome-mcp-servers)

The most-linked MCP list. Fork, add the entry below under **OS Automation**
(alphabetical order by repo name), then open a PR.

Legend used by that list: `#️⃣` = C# codebase, `🏠` = local service, `🪟` = Windows.

Entry to add:

```markdown
- [faze79/WPFVisualTreeMcp](https://github.com/faze79/WPFVisualTreeMcp) #️⃣ 🏠 🪟 - Inspect, debug and drive running WPF (.NET desktop) apps: visual tree, dependency properties, data bindings and binding errors, DataContext, screenshots (popups/menus included), plus clicking, item selection, text input and keyboard shortcuts. Auto-injects into any WPF process — no source changes.
```

PR title: `Add WPFVisualTreeMcp (WPF desktop app inspection & automation)`

## 4. Other directories (5-minute submission forms each)

- **mcp.so** — <https://mcp.so/submit>
- **Glama** — <https://glama.ai/mcp/servers> (auto-indexes public GitHub repos with an MCP
  server; a score badge can then be added to the README)
- **PulseMCP** — <https://www.pulsemcp.com/submit>
- **Smithery** — <https://smithery.ai/new> (needs a `smithery.yaml`; note this server is
  Windows-only and stdio-based, so hosted deployment doesn't apply — list as local-only)

## 5. Where the WPF audience actually is

This is an enterprise .NET crowd, not Hacker News:

- **r/dotnet**, **r/csharp** — a demo GIF plus the story of what the agent found
- **LinkedIn** — the same, aimed at teams maintaining legacy WPF line-of-business apps
- **dev.to / Medium** — the long-form write-up (see `docs/blog/`)
- **Awesome lists**: awesome-dotnet, awesome-wpf (dev-tools sections)

Lead with the *outcome* ("Claude found a binding typo in our WPF app by reading its
runtime binding errors"), not the mechanism ("MCP server for visual tree inspection").
