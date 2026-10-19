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

The script publishes the x64 server and a single-file x86 helper with their
runtimes, starts the packaged server to obtain its real tool list, and validates
and packs the manifest using `@anthropic-ai/mcpb@2.1.2`. Keep the helper's runtime
inside its executable: copying its runtime DLLs into `native/x86` would overwrite
the .NET Framework Inspector dependencies.

Before distributing the archive, extract it and verify the extracted payload:

```powershell
./scripts/Test-Injection.ps1 -ServerDirectory <extracted-bundle>/server
./tests/run-integration-tests.ps1 -ServerDirectory <extracted-bundle>/server
```

These tests exercise real WPF targets; the MCPB schema check alone does not prove
that injection works. The bundle is unsigned; a successful schema validation is
not a code-signing verification.

## Smithery publishing

Smithery's [local publishing flow](https://smithery.ai/docs/build/publish) accepts
MCPB bundles through its CLI:

```powershell
npx --yes smithery@1.2.0 auth login
npx --yes smithery@1.2.0 mcp publish ./WpfVisualTreeMcp-v0.16.0-win-x64.mcpb -n faze79/wpf-visual-tree
```

Browser login and CLI authorization are separate steps. Do not substitute a
repository URL for an MCP HTTP endpoint in the web publishing form. Verify the
public listing and downloadable artifact after publication. See
[distribution status](https://github.com/faze79/WPFVisualTreeMcp/blob/main/docs/PROMOTION.md)
for current catalog availability and the
announced Smithery service changes.
