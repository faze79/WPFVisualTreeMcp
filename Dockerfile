# Dockerfile for the Glama (glama.ai/mcp/servers) health check.
#
# WpfVisualTreeMcp does its real work on Windows: it injects an inspector into
# running WPF (.NET desktop) processes and talks to it over named pipes. None of
# that runs in a Linux container. But the MCP server itself is plain .NET 8 — it
# starts, performs the MCP handshake, and answers `tools/list` on any platform,
# because the tool list is built from attributes via reflection, not by touching
# any Windows API. That introspection is exactly what Glama's check exercises.
#
# Rather than compile from source (the solution also targets net48 /
# net8.0-windows and bundles a win-x86 helper, which don't build on Linux), this
# image installs the published, cross-platform NuGet tool and runs it as the
# stdio MCP server. Running it locally against a real WPF app still requires
# Windows — see the README.

FROM mcr.microsoft.com/dotnet/sdk:8.0

# Install the published MCP server as a global .NET tool (cross-platform payload).
RUN dotnet tool install --global WpfVisualTreeMcp
ENV PATH="${PATH}:/root/.dotnet/tools"

# `wpfinspect` with no arguments runs the MCP stdio server (a subcommand would run
# the one-shot CLI instead — see Program.cs / CliRunner).
ENTRYPOINT ["wpfinspect"]
