"""Interactive Windows test of native touch injection and actual WPF touch/manipulation events."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import json
from pathlib import Path
import subprocess
import time

from test_injection import McpClient, value
from test_drag import find


def telemetry(client, predicate=lambda data: data["ups"] > 0 and data["active"] == 0):
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        data = json.loads(find(client, "TouchTelemetry")["text"])
        manipulation = find(client, "TouchManipulation")["text"]
        try:
            data.update(json.loads(manipulation))
        except ValueError:
            raise AssertionError(manipulation)
        if predicate(data):
            return data
        time.sleep(0.04)
    raise AssertionError(f"Touch events not received as expected: {data}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--server", type=Path, required=True)
    parser.add_argument("--sample", type=Path, required=True)
    parser.add_argument("--configuration", required=True)
    args = parser.parse_args()
    sample = subprocess.Popen([str(args.sample.resolve())])
    clients = []
    try:
        time.sleep(1.5)
        assert sample.poll() is None
        with (args.sample.parent.parent / "touch-server.log").open("w", encoding="utf-8") as log:
            client = McpClient(args.server.resolve(), log)
            clients.append(client)
            assert value(client.tool("wpf_attach", process_id=sample.pid))["success"]
            schema = client.call("tools/list", {})
            tool = next(tool for tool in schema["tools"] if tool["name"] == "wpf_touch_gesture")
            assert set(tool["inputSchema"]["required"]) == {"element_handle", "gesture"}, tool
            value(client.tool("wpf_click_element", element_handle=find(client, "OpenTouchDemoButton")["handle"]))
            surface = find(client, "TouchSurface")["handle"]
            reset = find(client, "ResetTouchButton")["handle"]
            for gesture, options, contacts in [
                ("tap", {}, 1), ("long_press", {"duration_ms": 900}, 1),
                ("swipe", {"delta_x": 100, "delta_y": 30}, 1),
                ("pinch", {"scale": 1.6, "radius": 55}, 2),
                ("pinch", {"scale": 0.6, "radius": 75}, 2),
                ("rotate", {"angle_degrees": 70, "radius": 65}, 2),
                ("rotate", {"angle_degrees": -55}, 2),
            ]:
                value(client.tool("wpf_click_element", element_handle=reset))
                result = value(client.tool("wpf_touch_gesture", element_handle=surface, gesture=gesture, **options))
                assert result["contact_count"] == contacts, result
                data = telemetry(client)
                assert data["downs"] == data["ups"] == contacts and data["max_contacts"] == contacts, data
                if gesture == "long_press": assert data["hold_ms"] >= 750, data
                if gesture == "swipe": assert data["x"] > 70 and data["y"] > 10, data
                if gesture == "pinch": assert abs(data["scale"] - options["scale"]) < 0.2, data
                if gesture == "rotate": assert abs(data["rotation"] - options["angle_degrees"]) < 15, data
                print(f"  {gesture} {options}: {data}", flush=True)

            value(client.tool("wpf_click_element", element_handle=reset))
            cli = subprocess.run([str(args.server.resolve()), "touch", "--pid", str(sample.pid),
                                  "--handle", surface, "--gesture", "rotate", "--angle", "-45"],
                                 capture_output=True, text=True, timeout=30)
            assert cli.returncode == 0 and json.loads(cli.stdout)["success"], (cli.stdout, cli.stderr)
            assert telemetry(client)["rotation"] < -30

            observer = McpClient(args.server.resolve(), log)
            clients.append(observer)
            assert value(observer.tool("wpf_attach", process_id=sample.pid))["success"]
            value(client.tool("wpf_click_element", element_handle=reset))
            with ThreadPoolExecutor() as pool:
                touching = pool.submit(client.tool, "wpf_touch_gesture", element_handle=surface,
                                       gesture="long_press", duration_ms=1800)
                telemetry(observer, lambda data: data["active"] == 1)
                for name, options in [("wpf_drag", {"delta_x": 20}),
                                      ("wpf_touch_gesture", {"gesture": "tap"})]:
                    rejected = observer.tool(name, element_handle=surface, **options)
                    assert rejected.get("isError") and "in progress" in rejected["content"][0]["text"], rejected
                value(touching.result(timeout=10))
            assert telemetry(client)["active"] == 0

            for options, error in [
                ({"gesture": "swipe", "delta_x": 10000}, "bounds"),
                ({"gesture": "rotate", "radius": 1000, "angle_degrees": 90}, "bounds"),
                ({"gesture": "tap", "scale": 2}, "only valid"),
                ({"gesture": "pinch", "scale": 1}, "excluding"),
            ]:
                value(client.tool("wpf_click_element", element_handle=reset))
                rejected = client.tool("wpf_touch_gesture", element_handle=surface, **options)
                assert rejected.get("isError") and error in rejected["content"][0]["text"], rejected
                assert telemetry(client, lambda data: True)["downs"] == 0
            print(f"PASS {args.configuration}: all five gestures, two-finger WPF manipulation, "
                  "CLI, concurrent reads, mouse/touch exclusion, path validation, contacts released")
    finally:
        for client in clients:
            client.close()
        if sample.poll() is None:
            sample.terminate()
        sample.wait(timeout=10)


if __name__ == "__main__":
    main()
