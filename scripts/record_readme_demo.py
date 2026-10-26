"""Record the README demo: native MCP gestures, followed by WPF inspection.

Requires Windows, an unlocked desktop, Pillow and a built SampleWpfApp. Keep the
mouse idle while recording. Only the sample launched here is controlled/closed.

Example (from the repository root, after building the MCP server):
  dotnet build samples/SampleWpfApp -c Release -p:SampleTargetFramework=net10.0-windows --output TestResults/readme-demo/sample
  python scripts/record_readme_demo.py --sample TestResults/readme-demo/sample/SampleWpfApp.exe

Screenshots come from a second MCP connection while the first sends native input.
Touch rings are drawn by the sample from actual WPF touch events; the mouse cursor
overlay uses its actual OS position. Assertions verify application responses.
The original inspection sequence stays in docs/assets/demo-inspection.gif so
regenerating the demo never appends the gesture sequence twice.
"""
import argparse
import base64
from concurrent.futures import ThreadPoolExecutor
import ctypes
from ctypes import wintypes
import io
import json
from pathlib import Path
import subprocess
import time

from PIL import Image, ImageDraw, ImageFont

from test_drag import find, no_buttons_held, wait_text
from test_injection import McpClient, value
from test_touch import telemetry


ROOT = Path(__file__).resolve().parents[1]
SIZE = (784, 657)
VIEW_HEIGHT = 560


class Recorder:
    def __init__(self, client, observer):
        self.client, self.observer = client, observer
        self.frames, self.durations, self.evidence = [], [], []
        self.font = ImageFont.truetype("C:/Windows/Fonts/consola.ttf", 14)
        self.bold = ImageFont.truetype("C:/Windows/Fonts/consolab.ttf", 14)
        self.window = None

    def screenshot(self, mouse=False):
        result = self.observer.tool("wpf_capture_screenshot",
                                    element_handle=self.window["handle"],
                                    mode="render", max_width=784, max_height=560)
        assert not result.get("isError"), result
        block = next(item for item in result["content"] if item["type"] == "image")
        with Image.open(io.BytesIO(base64.b64decode(block["data"]))) as image:
            shot = image.convert("RGB")
        frame = Image.new("RGB", SIZE, "white")
        left, top = (SIZE[0] - shot.width) // 2, (VIEW_HEIGHT - shot.height) // 2
        frame.paste(shot, (left, top))
        if mouse:
            point = wintypes.POINT()
            if ctypes.windll.user32.GetCursorPos(ctypes.byref(point)):
                bounds = self.window["screenBounds"]
                x = left + (point.x - bounds["x"]) * shot.width / bounds["width"]
                y = top + (point.y - bounds["y"]) * shot.height / bounds["height"]
                if left <= x < left + shot.width and top <= y < top + shot.height:
                    ImageDraw.Draw(frame).polygon(
                        [(x, y), (x + 2, y + 21), (x + 7, y + 15),
                         (x + 12, y + 25), (x + 16, y + 23), (x + 11, y + 13),
                         (x + 20, y + 13)], fill="white", outline="#111827", width=2)
        return frame

    def append(self, shot, title, command, status, duration=100, success=False):
        frame = shot.copy()
        draw = ImageDraw.Draw(frame)
        draw.rectangle((0, VIEW_HEIGHT, 784, 657), fill="#1e1e1e")
        draw.text((16, 568), title, font=self.bold, fill="#8be28b")
        for index, line in enumerate(command.splitlines()):
            draw.text((16, 591 + index * 17), line, font=self.font, fill="#f1f5f9")
        draw.text((16, 635), status, font=self.font,
                  fill="#8be28b" if success else "#b7bec9")
        self.frames.append(frame)
        self.durations.append(duration)

    def step(self, title, command, tool, arguments, verify, mouse=False):
        self.append(self.screenshot(mouse), title, command, "Native input via MCP", 650)
        with ThreadPoolExecutor(max_workers=1) as pool:
            pending = pool.submit(self.client.tool, tool, **arguments)
            while not pending.done():
                started = time.monotonic()
                shot = self.screenshot(mouse)
                elapsed = time.monotonic() - started
                self.append(shot, title, command, "Native input via MCP",
                            max(100, round(elapsed * 100) * 10))
                time.sleep(max(0, 0.1 - elapsed))
            result = value(pending.result())
        assert result["success"], result
        message = verify()
        self.append(self.screenshot(mouse), title, command, "PASS  " + message,
                    1000, success=True)
        self.evidence.append({"title": title, "tool": tool, "arguments": arguments,
                              "result": result, "verification": message})
        print(f"PASS {title}: {message}", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sample", type=Path, required=True)
    parser.add_argument("--server", type=Path, default=ROOT / "src/WpfVisualTreeMcp.Server/bin/Release/net8.0/WpfVisualTreeMcp.Server.exe")
    parser.add_argument("--intro", type=Path, default=ROOT / "docs/assets/demo-inspection.gif")
    parser.add_argument("--output", type=Path, default=ROOT / "docs/assets/demo-drag-touch.gif")
    parser.add_argument("--evidence", type=Path, default=ROOT / "TestResults/readme-demo")
    args = parser.parse_args()
    args.evidence.mkdir(parents=True, exist_ok=True)
    frames, durations = [], []
    with Image.open(args.intro) as intro:
        assert intro.size == SIZE, intro.size
        for index in range(intro.n_frames):
            intro.seek(index)
            frames.append(intro.convert("RGB"))
            durations.append(intro.info.get("duration", 125))
    clients = []
    sample = subprocess.Popen([str(args.sample.resolve())])
    try:
        time.sleep(1.5)
        assert sample.poll() is None, "Sample exited during startup"
        with (args.evidence / "recording-server.log").open("w", encoding="utf-8") as log:
            for _ in range(2):
                client = McpClient(args.server.resolve(), log)
                clients.append(client)
                assert value(client.tool("wpf_attach", process_id=sample.pid))["success"]
            client, observer = clients
            recorder = Recorder(client, observer)
            value(client.tool("wpf_click_element", automation_id="open.drag"))
            wait_text(client, "DropStatus", "Drop here")
            recorder.window = find(observer, type_name="Window", text="Mouse drag demo")

            def dropped():
                text = wait_text(client, "DropStatus", "Dropped 1")
                no_buttons_held()
                return text + "; real WPF/OLE drop accepted"

            recorder.step("DRAG 1/2  Native drag-and-drop by AutomationId",
                          'wpf_drag(automation_id="drag.source",\n         target_automation_id="drag.target")',
                          "wpf_drag", {"automation_id": "drag.source",
                                       "target_automation_id": "drag.target", "duration_ms": 1400},
                          dropped, mouse=True)

            def moved():
                status = wait_text(client, "DragStatus", "Released Left")
                assert int(status.split("moves=")[1]) > 2, status
                no_buttons_held()
                return "Canvas item moved; mouse capture released"

            recorder.step("DRAG 2/2  Move a canvas item",
                          'wpf_drag(automation_id="drag.box", delta_x=240)',
                          "wpf_drag", {"automation_id": "drag.box", "delta_x": 240,
                                       "duration_ms": 1500}, moved, mouse=True)

            value(client.tool("wpf_click_element", automation_id="open.touch"))
            find(client, "TouchSurface")
            recorder.window = find(observer, type_name="Window", text="Touch gesture demo")
            gestures = [
                ("Tap", "tap", {"duration_ms": 300}, "Tap received; touch contact released"),
                ("Press and hold", "long_press", {"duration_ms": 1100}, "Long press received; held for at least 900 ms"),
                ("Pan", "swipe", {"delta_x": 110, "delta_y": 35, "duration_ms": 1500}, "Card translated by native WPF manipulation"),
                ("Pinch to zoom", "pinch", {"scale": 1.6, "radius": 55, "duration_ms": 1500}, "Two contacts; card scaled to 1.6x"),
                ("Rotate", "rotate", {"angle_degrees": 65, "radius": 65, "duration_ms": 1500}, "Two contacts; card rotated about 65 degrees"),
            ]
            for number, (label, gesture, options, success) in enumerate(gestures, 1):
                value(client.tool("wpf_click_element", automation_id="touch.reset"))
                time.sleep(0.15)

                def verify_touch():
                    data = telemetry(client)
                    contacts = 2 if gesture in ("pinch", "rotate") else 1
                    assert data["downs"] == data["ups"] == data["max_contacts"] == contacts, data
                    assert data["active"] == 0, data
                    if gesture == "long_press":
                        assert data["hold_ms"] >= 900, data
                    if gesture == "swipe":
                        assert data["x"] > 80 and data["y"] > 15, data
                    if gesture == "pinch":
                        assert abs(data["scale"] - 1.6) < 0.2, data
                    if gesture == "rotate":
                        assert abs(data["rotation"] - 65) < 15, data
                    return success

                extra = ", ".join(f"{key}={val}" for key, val in options.items()
                                  if key != "duration_ms")
                command = ('wpf_touch_gesture(automation_id="touch.surface",\n'
                           f'                  gesture="{gesture}"' +
                           (", " + extra if extra else "") + ")")
                # Keep the command readable within the fixed-width README image.
                if gesture in ("pinch", "rotate"):
                    command = command.replace(f", radius={options['radius']}", "")
                recorder.step(f"TOUCH {number}/5  {label}", command, "wpf_touch_gesture",
                              {"automation_id": "touch.surface", "gesture": gesture, **options},
                              verify_touch)

            frames = recorder.frames + frames
            durations = recorder.durations + durations
            (args.evidence / "gestures.json").write_text(
                json.dumps(recorder.evidence, indent=2), encoding="utf-8")
            # Lossless GIF delta frames keep this mostly-static UI recording small.
            args.output.parent.mkdir(parents=True, exist_ok=True)
            frames[0].save(args.output, save_all=True, append_images=frames[1:],
                           duration=durations, loop=0, optimize=True, disposal=1)
            indices = [0]
            # Include each gesture's last (verified) frame in the review sheet.
            indices.extend(i for i, duration in enumerate(recorder.durations)
                           if duration == 1000)
            indices.append(len(frames) - 1)
            sheet = Image.new("RGB", (3 * 392, 3 * 329), "white")
            for index, frame_index in enumerate(indices):
                sheet.paste(frames[frame_index].resize((392, 329)),
                            ((index % 3) * 392, (index // 3) * 329))
            sheet.save(args.evidence / "contact-sheet.png")
            print(f"Saved {args.output}: {len(frames)} frames, "
                  f"{sum(durations) / 1000:.1f}s, {args.output.stat().st_size:,} bytes", flush=True)
    finally:
        for client in clients:
            client.close()
        if sample.poll() is None:
            sample.terminate()
        sample.wait(timeout=10)


if __name__ == "__main__":
    main()
