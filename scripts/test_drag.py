"""Interactive desktop smoke test: actual WPF slider, splitter, OLE drop and mouse capture.

Runs only its own sample application. Requires an unlocked interactive Windows
desktop; keep the mouse idle. Uses stdlib and the existing MCP stdio test client.
"""
import argparse
from concurrent.futures import ThreadPoolExecutor
import ctypes
import json
from pathlib import Path
import subprocess
import time

from test_injection import McpClient, value


def find(client, name=None, **filters):
    if name is not None:
        filters["element_name"] = name
    result = value(client.tool("wpf_find_elements", **filters))
    elements = result["elements"]
    if name is not None:
        elements = [item for item in elements if item.get("name") == name]
    assert elements, result
    return elements[0]


def wait_text(client, name, expected):
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        text = find(client, name).get("text", "")
        if expected in text:
            return text
        time.sleep(0.05)
    raise AssertionError(f"{name}: expected {expected!r}, got {text!r}")


def no_buttons_held():
    assert all(not (ctypes.windll.user32.GetAsyncKeyState(key) & 0x8000) for key in (1, 2, 4)), "Mouse button left held"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--server", type=Path, required=True)
    parser.add_argument("--sample", type=Path, required=True)
    parser.add_argument("--configuration", required=True)
    args = parser.parse_args()
    sample = subprocess.Popen([str(args.sample.resolve())])
    clients = []
    log_path = args.sample.parent.parent / "drag-server.log"
    try:
        time.sleep(1.5)
        assert sample.poll() is None, "Sample exited during startup"
        with log_path.open("w", encoding="utf-8") as log:
            client = McpClient(args.server.resolve(), log)
            clients.append(client)
            assert value(client.tool("wpf_attach", process_id=sample.pid))["success"]
            schema = client.call("tools/list", {})
            tool = next(tool for tool in schema["tools"] if tool["name"] == "wpf_drag")
            assert not tool["inputSchema"].get("required"), tool
            assert {"element_handle", "automation_id", "target_automation_id"} <= set(tool["inputSchema"]["properties"]), tool

            value(client.tool("wpf_click_element", element_handle=find(client, "OpenDragDemoButton")["handle"]))
            wait_text(client, "DropStatus", "Drop here")
            source = find(client, "DragSource")["handle"]
            target = find(client, "DropTarget")["handle"]
            drop = value(client.tool("wpf_drag", element_handle=source, target_handle=target,
                                     start_x=15, start_y=20, target_x=20, target_y=20, duration_ms=600))
            assert drop["success"] and drop["coordinate_space"] == "screen_pixels", drop
            wait_text(client, "DropStatus", "Dropped 1")
            no_buttons_held()

            slider = find(client, "DragSlider")["handle"]
            thumb = find(client, type_name="Thumb", root_handle=slider)["handle"]
            before = float(find(client, "SliderValue")["text"])
            value(client.tool("wpf_drag", element_handle=thumb, delta_x=100))
            after = float(find(client, "SliderValue")["text"])
            assert after > before + 5, (before, after)

            splitter = find(client, "DragSplitter")
            value(client.tool("wpf_drag", element_handle=splitter["handle"], delta_x=60))
            moved_splitter = find(client, "DragSplitter")
            assert moved_splitter["screenBounds"]["x"] > splitter["screenBounds"]["x"] + 20

            box = find(client, "DragBox")["handle"]
            for button in ("left", "right", "middle"):
                result = value(client.tool("wpf_drag", element_handle=box, delta_x=60, delta_y=0,
                                           start_x=15, start_y=20, button=button, duration_ms=250))
                assert result["end"]["x"] > result["start"]["x"], result
                status = wait_text(client, "DragStatus", "Released " + button.title())
                assert int(status.split("moves=")[1]) > 2, status
                no_buttons_held()

            # CLI parses negative values as option values, not flags.
            cli = subprocess.run([str(args.server.resolve()), "drag", "--pid", str(sample.pid),
                                  "--handle", box, "--delta-x", "-40.5", "--duration", "200"],
                                 capture_output=True, text=True, timeout=30)
            assert cli.returncode == 0, (cli.stdout, cli.stderr)
            assert json.loads(cli.stdout)["success"], cli.stdout
            wait_text(client, "DragStatus", "Released Left")

            observer = McpClient(args.server.resolve(), log)
            clients.append(observer)
            assert value(observer.tool("wpf_attach", process_id=sample.pid))["success"]
            with ThreadPoolExecutor() as pool:
                dragging = pool.submit(client.tool, "wpf_drag", element_handle=box, delta_x=80, duration_ms=1800)
                wait_text(observer, "DragStatus", "Moving Left")
                busy = observer.tool("wpf_drag", element_handle=box, delta_x=20)
                assert busy.get("isError") and "in progress" in busy["content"][0]["text"], busy
                assert value(dragging.result(timeout=10))["success"]
            wait_text(client, "DragStatus", "Released Left")

            for arguments, error in [
                ({"element_handle": box, "target_handle": target, "delta_x": 1}, "exclusively"),
                ({"element_handle": "elem_missing", "delta_x": 20}, "handle"),
                ({"element_handle": box, "delta_x": 20, "start_x": 999, "start_y": 10}, "bounds"),
                ({"element_handle": box, "delta_x": 100000}, "endpoints"),
            ]:
                rejected = client.tool("wpf_drag", **arguments)
                assert rejected.get("isError") and error.lower() in rejected["content"][0]["text"].lower(), rejected
                no_buttons_held()

            print(f"PASS {args.configuration}: MCP schema, OLE drop, slider, splitter, all mouse buttons, "
                  "local points, negative CLI delta, concurrent reads, interaction exclusion, invalid endpoints, button release")
    finally:
        for client in clients:
            client.close()
        if sample.poll() is None:
            sample.terminate()
        sample.wait(timeout=10)


if __name__ == "__main__":
    main()
