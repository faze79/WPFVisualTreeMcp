"""Exercise the published MCP server against a WPF app without a hosted Inspector.

Uses only the Python standard library. Every process and package copy is owned
by this test; no existing applications or source build outputs are changed.
"""
import argparse
import json
from pathlib import Path
import queue
import shutil
import subprocess
import tempfile
import threading
import time


def start(args, **kwargs):
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = 0
    return subprocess.Popen(args, startupinfo=startup, **kwargs)


class McpClient:
    def __init__(self, executable, log):
        self.process = start([str(executable)], stdin=subprocess.PIPE,
                             stdout=subprocess.PIPE, stderr=log,
                             text=True, encoding="utf-8")
        self.responses = queue.Queue()
        self.request_id = 0
        threading.Thread(target=self._read, daemon=True).start()
        self.call("initialize", {"protocolVersion": "2024-11-05",
                  "capabilities": {}, "clientInfo": {"name": "injection-smoke", "version": "1"}})
        self.send({"jsonrpc": "2.0", "method": "notifications/initialized"})

    def _read(self):
        for line in self.process.stdout:
            self.responses.put(json.loads(line))

    def send(self, message):
        self.process.stdin.write(json.dumps(message) + "\n")
        self.process.stdin.flush()

    def call(self, method, params):
        self.request_id += 1
        self.send({"jsonrpc": "2.0", "id": self.request_id, "method": method, "params": params})
        deadline = time.monotonic() + 45
        while True:
            response = self.responses.get(timeout=max(0.1, deadline - time.monotonic()))
            if response.get("id") == self.request_id:
                if "error" in response:
                    raise AssertionError(response)
                return response["result"]
            if time.monotonic() >= deadline:
                raise TimeoutError(method)

    def tool(self, name, **arguments):
        return self.call("tools/call", {"name": name, "arguments": arguments})

    def close(self):
        self.process.terminate()
        self.process.wait(timeout=10)


def value(result):
    assert not result.get("isError"), result
    return json.loads(result["content"][0]["text"])


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--server", required=True, type=Path)
    parser.add_argument("--sample", required=True, type=Path)
    parser.add_argument("--architecture", required=True, choices=["x86", "x64"])
    args = parser.parse_args()
    assert not (args.sample.parent / "WpfVisualTreeMcp.Inspector.dll").exists(), "Sample must not self-host"
    for arch in ("x86", "x64"):
        for relative in ("WpfInspectorBootstrapper.dll", "WpfVisualTreeMcp.Inspector.dll",
                         "WpfVisualTreeMcp.Shared.dll", "System.Text.Json.dll",
                         "coreclr/WpfVisualTreeMcp.Inspector.dll",
                         "coreclr/WpfVisualTreeMcp.Shared.dll",
                         "coreclr/WpfVisualTreeMcp.Inspector.coreclr.runtimeconfig.json"):
            assert (args.server / "native" / arch / relative).is_file(), relative

    with tempfile.TemporaryDirectory(prefix="wpf-injection-") as temp:
        package = Path(temp) / "server"
        shutil.copytree(args.server, package)
        bootstrapper = package / "native" / args.architecture / "WpfInspectorBootstrapper.dll"
        disabled = bootstrapper.with_suffix(".disabled")
        bootstrapper.rename(disabled)
        sample = start([str(args.sample.resolve())])
        client = None
        try:
            time.sleep(2)
            assert sample.poll() is None, "Sample exited during startup"
            with (Path(temp) / "server.log").open("w", encoding="utf-8") as log:
                client = McpClient(package / "WpfVisualTreeMcp.Server.exe", log)
                try:
                    no_injection = value(client.tool("wpf_attach", process_id=sample.pid, auto_inject=False))
                    assert no_injection["success"] is False, no_injection
                    assert no_injection["inspector_ready"] is False, no_injection

                    failed = value(client.tool("wpf_attach", process_id=sample.pid, auto_inject=True))
                    assert failed["success"] is False, failed
                    assert failed["inspector_ready"] is False, failed
                    assert "Bootstrapper" in failed["inspector_status"], failed
                    downstream = client.tool("wpf_find_elements", text="Name")
                    assert downstream.get("isError") is True, downstream
                    assert str(sample.pid) in downstream["content"][0]["text"], downstream
                    assert "Bootstrapper" in downstream["content"][0]["text"], downstream

                    cli = subprocess.run([str(package / "WpfVisualTreeMcp.Server.exe"), "attach",
                                          "--pid", str(sample.pid), "--auto-inject"],
                                         capture_output=True, text=True, timeout=30)
                    assert cli.returncode == 1, cli.stdout
                    assert json.loads(cli.stdout)["success"] is False, cli.stdout

                    disabled.rename(bootstrapper)
                    attached = value(client.tool("wpf_attach", process_id=sample.pid, auto_inject=True))
                    assert attached["success"] is True, attached
                    assert attached["inspector_ready"] is True, attached
                    tree = value(client.tool("wpf_get_visual_tree", max_depth=5))
                    assert tree["totalElements"] > 0, tree

                    report = value(client.tool("wpf_report", max_elements=2))
                    assert report["processId"] == sample.pid, report
                    assert report["scannedElements"] == 2, report
                    assert "max_elements" in report["truncationReasons"], report
                    assert report["bindingErrorScope"] == "process_since_inspector_attach", report
                    cli_report = subprocess.run([str(package / "WpfVisualTreeMcp.Server.exe"), "report",
                                                 "--pid", str(sample.pid), "--max", "2"],
                                                capture_output=True, text=True, timeout=30)
                    assert cli_report.returncode == 0, cli_report.stdout + cli_report.stderr
                    assert json.loads(cli_report.stdout)["scannedElements"] == 2, cli_report.stdout

                    existing = value(client.tool("wpf_attach", process_id=sample.pid, auto_inject=False))
                    assert existing["success"] is True, existing
                    print(f"PASS {args.architecture}: missing-DLL diagnostics, CLI failure exit, real injection, "
                          f"{tree['totalElements']} visual elements, bounded MCP/CLI reports, existing-inspector reconnect")
                finally:
                    client.close()
                    client = None
        except Exception:
            server_log = Path(temp) / "server.log"
            if server_log.exists():
                print(server_log.read_text(encoding="utf-8")[-12000:])
            raise
        finally:
            if client:
                client.close()
            if sample.poll() is None:
                sample.terminate()
            sample.wait(timeout=10)


if __name__ == "__main__":
    main()
