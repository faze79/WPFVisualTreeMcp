"""Generate bundle metadata from the packaged server's actual MCP tools."""
import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET

from test_injection import McpClient


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--bundle", required=True, type=Path)
    args = parser.parse_args()
    project = args.source / "src/WpfVisualTreeMcp.Server/WpfVisualTreeMcp.Server.csproj"
    version = ET.parse(project).findtext(".//Version")
    with (args.bundle.parent / "mcpb-schema.log").open("w", encoding="utf-8") as log:
        client = McpClient(args.bundle / "server/WpfVisualTreeMcp.Server.exe", log)
        try:
            tools = client.call("tools/list", {})["tools"]
        finally:
            client.close()
    if not tools or len({tool["name"] for tool in tools}) != len(tools):
        raise ValueError("The packaged MCP server must expose distinct tools")
    repo = "https://github.com/faze79/WPFVisualTreeMcp"
    manifest = {
        "manifest_version": "0.3",
        "name": "wpf-visual-tree",
        "display_name": "WPFVisualTreeMcp",
        "version": version,
        "description": "Inspect and automate local WPF apps: visual trees, binding diagnostics, screenshots, mouse hover/wheel/dragging, touch gestures and workflow tests.",
        "long_description": (
            f"{len(tools)} MCP tools for inspecting and automating running WPF apps on Windows x64. "
            "Read visual trees, dependency properties, bindings and DataContext; capture full-content screenshots; "
            "move/click/scroll/drag with the mouse, send native touch gestures, record/replay workflows and export runnable xUnit tests.\n\n"
            "Supports .NET Framework 4.7.2/4.8/4.8.1 and .NET 8/9/10 WPF targets, including x86 targets. "
            "The server and x86 helper include their .NET runtimes. The target app still needs its own runtime. "
            "Run in the same local Windows desktop session as the target application. "
            "Touch behavior depends on application support.\n\n"
            "Install an updated MCPB through your client to update this bundle. CLI self-update is intended "
            "for the separate NuGet and portable installations, not client-managed MCPB files."
        ),
        "author": {"name": "faze79", "url": "https://github.com/faze79"},
        "repository": {"type": "git", "url": repo + ".git"},
        "homepage": repo,
        "documentation": repo + "/blob/main/docs/MCPB.md",
        "support": repo + "/issues",
        "license": "MIT",
        "keywords": ["wpf", "windows", "dotnet", "debugging", "automation", "touch", "testing"],
        "compatibility": {"platforms": ["win32"]},
        "server": {
            "type": "binary",
            "entry_point": "server/WpfVisualTreeMcp.Server.exe",
            "mcp_config": {"command": "${__dirname}/server/WpfVisualTreeMcp.Server.exe", "args": []},
        },
        "tools": [{"name": tool["name"], "description": tool["description"]} for tool in tools],
        "tools_generated": False,
    }
    (args.bundle / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    # MCPB's tool entries allow only name/description. Smithery's release API
    # needs full MCP Tool objects in a separate serverCard instead.
    card = {"serverInfo": {"name": manifest["name"], "version": version}, "tools": tools}
    (args.bundle.parent / "mcp-server-card.json").write_text(json.dumps(card, indent=2) + "\n", encoding="utf-8")
    print(f"MCPB manifest: v{version}, {len(tools)} tools")


if __name__ == "__main__":
    main()
