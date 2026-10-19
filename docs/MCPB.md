# Local MCPB bundle

`WpfVisualTreeMcp-vX.Y.Z-win-x64.mcpb` is a local desktop extension for MCP clients
that support [MCP bundles](https://github.com/modelcontextprotocol/mcpb).
Import it through the client's extension installation UI. The client starts
`server/WpfVisualTreeMcp.Server.exe` over stdio with no arguments.

The bundle requires Windows 10/11 x64 and a local desktop session. It includes
the .NET runtime for the server and a self-contained x86 injection helper; no
separate .NET installation is required to start these two executables. The WPF
application being inspected still needs its own runtime. Supported targets are
.NET Framework 4.7.2/4.8 and .NET 8+, on x64 or x86.

The 36 MCP tools include visual trees, bindings, diagnostics, screenshots, mouse
dragging, native touch gestures and workflow recording/replay/xUnit export. Touch
requires application support. This is a local Windows server, not a hosted HTTP
endpoint. The bundle does not install the separate Codex companion skill.

Update by importing a newer MCPB through your client. Use CLI `update` for the
separate NuGet or portable installations; replacing files inside a client-managed
bundle can leave its metadata and packaged runtimes out of sync.

## Building

Use a clean checkout of the intended release. The build requires Visual Studio
C++ build tools, the .NET SDK, Python 3 and Node.js/npm. From a developer shell:

```powershell
./scripts/Build-NativeBootstrapper.ps1
dotnet build WpfVisualTreeMcp.sln -c Release
./scripts/Build-McpBundle.ps1 -OutputDirectory ./TestResults/mcpb-build
```

The output directory must not exist. For packaging an older release with the
current packaging scripts, pass `-SourceDirectory <clean-release-checkout>`.
Only published binaries, generated metadata, this guide and license notices enter
the bundle. Personal files and source-checkout contents are not copied.

Add `-Compact` to produce
`WpfVisualTreeMcp-vX.Y.Z-win-x64-compact.mcpb` for clients or catalogs with upload
limits (Smithery CLI 1.2.0 enforces 25 MiB). This variant trims
unused runtime code while explicitly preserving the reflected MCP tools, IPC
models, protocol SDK, JSON serializer and logger. Trimming still reports analysis
warnings for dynamic serialization and tool discovery; the preservation rules
live in `scripts/McpBundleTrim.targets`. Validate the compact archive itself with
the integration tests below before publishing it. The full bundle remains an
alternative for clients without the upload limit.

The build also writes `mcp-server-card.json` next to the bundle directory, with
each tool's full MCP schema. Keep it separate: the MCPB manifest permits only tool
names/descriptions, whereas a full MCP server card includes `inputSchema`.
Smithery CLI 1.2.0 copies the MCPB entries directly into its server card and the
API rejects them. Adding `inputSchema` to the manifest is not a valid fix because
it violates the MCPB schema. See the current Smithery availability below.

The script publishes the x64 server and a single-file x86 helper with their
runtimes, starts the packaged server to obtain its real tool list, and validates
and packs the manifest using `@anthropic-ai/mcpb@2.1.2`. Keep the helper's runtime
inside its executable: copying its runtime DLLs into `native/x86` would overwrite
the .NET Framework Inspector dependencies.

Before distributing the archive, extract it and verify the extracted payload:

```powershell
./scripts/Test-Injection.ps1 -ServerDirectory <extracted-bundle>/server
./tests/run-integration-tests.ps1 -ServerDirectory <extracted-bundle>/server
python ./scripts/test_workflow.py --server <extracted-bundle>/server/WpfVisualTreeMcp.Server.exe --sample <built-WpfTestApp.exe> --gestures
```

These tests exercise real WPF targets; the MCPB schema check alone does not prove
that injection works. The bundle is unsigned; a successful schema validation is
not a code-signing verification.

## Smithery availability

**Publication is blocked as of 2026-10-19.** Browser and CLI authentication
succeeded, but the official API's create-server call returns HTTP 403:
`Updating servers has been removed. Contact support@smithery.ai.`
Both the server lookup and release submission return HTTP 404 (`Server not found`)
for `faze79/wpf-visual-tree`. No Smithery listing or release was created.

The [documented local publishing flow](https://smithery.ai/docs/build/publish)
still describes MCPB distribution. If the service restores publishing, submit
the standard compact bundle and the separate complete server card through its
[release API](https://smithery.ai/docs/api-reference/servers/publish-a-server).
Recheck server creation support first; neither a valid bundle nor successful
authentication resolves the current service restriction. Do not substitute a
repository URL for an MCP HTTP endpoint in the web form.

Both bundle variants remain available through GitHub Releases. See
[distribution status](https://github.com/faze79/WPFVisualTreeMcp/blob/main/docs/PROMOTION.md)
for current catalog availability and the
announced Smithery service changes.
