"""Record through MCP/CLI, export xUnit, and run it against a fresh sample app.

--gestures adds real desktop mouse/keyboard/touch; keep the desktop unlocked and
input idle. Without it the test uses UI Automation and is suitable for CI.
"""
import argparse
import copy
import json
import os
from pathlib import Path
import subprocess
import time
import uuid

from test_injection import McpClient, value


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--server", type=Path, required=True)
    parser.add_argument("--sample", type=Path, required=True)
    parser.add_argument("--gestures", action="store_true")
    args = parser.parse_args()
    server, sample_path = args.server.resolve(), args.sample.resolve()
    work = Path(__file__).resolve().parents[1] / "TestResults" / ("workflow-" + uuid.uuid4().hex)
    work.mkdir(parents=True)
    sample = subprocess.Popen([str(sample_path)])
    client = None

    def cli(*arguments):
        result = subprocess.run([str(server), *arguments, "--pid", str(sample.pid)],
                                capture_output=True, text=True, encoding="utf-8", timeout=145)
        assert result.returncode == 0, result.stdout + result.stderr
        return json.loads(result.stdout)

    try:
        time.sleep(1.5)
        with (work / "mcp.log").open("w", encoding="utf-8") as log:
            client = McpClient(server, log)
            schemas = {t["name"]: t["inputSchema"] for t in client.call("tools/list", {})["tools"]}
            for name in ("wpf_click_element", "wpf_move_mouse", "wpf_scroll", "wpf_drag", "wpf_touch_gesture",
                         "wpf_select_item", "wpf_set_text", "wpf_send_keys", "wpf_assert", "wpf_set_property", "wpf_revert_property"):
                assert "automation_id" in schemas[name]["properties"], name
                assert "element_handle" not in schemas[name].get("required", []), name
            for name, required in (("wpf_scroll", "delta"), ("wpf_touch_gesture", "gesture"), ("wpf_set_text", "text")):
                assert required in schemas[name]["required"], name
            assert value(client.tool("wpf_attach", process_id=sample.pid, auto_inject=True))["success"]
            invalid_cli = subprocess.run([str(server), "send-keys", "--pid", str(sample.pid), "--keys", "Enter", "--automation-id"],
                                         capture_output=True, text=True, encoding="utf-8", timeout=30)
            assert invalid_cli.returncode != 0 and "requires a value" in invalid_cli.stdout, invalid_cli.stdout

            def tool(name, **kw):
                return value(client.tool(name, **kw))

            def find(name):
                results = tool("wpf_find_elements", element_name=name)["elements"]
                exact = [r for r in results if r.get("name") == name]
                assert len(exact) == 1, results
                return exact[0]["handle"]

            cli("record", "start", "--name", "Fresh process round trip")
            username = find("UserNameTextBox")
            assert tool("wpf_find_elements", automation_id="user.name")["elements"][0]["handle"] == username
            assert tool("wpf_find_elements_deep", automation_id="user.name")["count"] == 1
            assert tool("wpf_find_elements", automation_id="user")["count"] == 0
            assert tool("wpf_find_elements", automation_id="USER.NAME")["count"] == 0
            assert tool("wpf_find_elements", automation_id="user.name", type_name="Button")["count"] == 0
            assert tool("wpf_wait_for_element", automation_id="user.name", condition="enabled")["matched"]
            assert cli("find", "--automation-id", "user.name")["count"] == 1
            assert cli("find-deep", "--automation-id", "user.name")["count"] == 1
            assert cli("wait-for", "--automation-id", "user.name", "--condition", "exists")["matched"]
            assert client.tool("wpf_click_element", automation_id="missing.id").get("isError")
            assert client.tool("wpf_click_element", automation_id="form.clear", root_handle=username).get("isError")
            assert client.tool("wpf_click_element", automation_id="form.clear", element_handle=username).get("isError")
            assert client.tool("wpf_click_element", automation_id="form.clear", root_handle="stale_handle").get("isError")
            text = "Workflow àé漢字 🎯 " + ("x" * 5000)
            tool("wpf_set_text", automation_id="user.name", text=text)
            tool("wpf_assert", automation_id="user.name", property_name="Text", expected_value=text)
            count = len(tool("wpf_get_recording")["workflow"]["steps"])
            scoped_wait = tool("wpf_wait_for_element", element_name="ClearButton", root_handle=username, timeout_ms=100)
            assert not scoped_wait["matched"], "Wait ignored its root subtree"
            assert client.tool("wpf_assert", element_handle=username, property_name="Text", expected_value="wrong").get("isError")
            assert len(tool("wpf_get_recording")["workflow"]["steps"]) == count, "Failed assertion was recorded"
            if args.gestures:
                tool("wpf_send_keys", automation_id="user.name", keys="Ctrl+A")
                cli("send-keys", "--automation-id", "user.name", "--keys", "Home")
            tool("wpf_click_element", automation_id="form.clear")
            tool("wpf_assert", element_handle=username, property_name="Text", expected_value="")
            cli("set-text", "--automation-id", "user.name", "--text", "Clear through CLI by ID")
            cli("click", "--automation-id", "form.clear", "--root", find("ClearButton"))
            tool("wpf_assert", element_handle=username, property_name="Text", expected_value="")
            items = find("ItemsListBox")
            tool("wpf_select_item", automation_id="items.list", index=4)
            cli("select-item", "--automation-id", "items.list", "--index", "5")
            cli("assert", "--automation-id", "items.list", "--property", "SelectedIndex", "--expected", "5")
            tool("wpf_set_property", automation_id="user.name", property_name="Tag", value="ID test")
            cli("assert", "--automation-id", "user.name", "--property", "Tag", "--expected", "ID test")
            cli("revert-property", "--automation-id", "user.name", "--property", "Tag")
            cli("set-property", "--automation-id", "user.name", "--property", "Tag", "--value", "CLI test")
            tool("wpf_revert_property", automation_id="user.name", property_name="Tag")
            tool("wpf_assert", automation_id="user.name", property_name="Tag", expected_value="{null}")
            tool("wpf_wait_for_element", element_name="RemoveItemButton", condition="enabled")

            if args.gestures:
                tool("wpf_click_element", automation_id="open.drag")
                assert tool("wpf_wait_for_element", element_name="DropTarget")["matched"]
                tool("wpf_drag", automation_id="drag.source", target_automation_id="drag.target",
                     start_x=15, start_y=20, target_x=20, target_y=20, duration_ms=600)
                assert tool("wpf_wait_for_element", element_name="DropStatus", text="Dropped 1")["matched"]
                cli("drag", "--automation-id", "drag.source", "--target-automation-id", "drag.target", "--duration", "600")
                assert tool("wpf_wait_for_element", element_name="DropStatus", text="Dropped 2")["matched"]
                canvas = find("DragCanvas")
                cli("move-mouse", "--automation-id", "drag.canvas", "--x", "100", "--y", "70", "--hover-ms", "100")
                tool("wpf_move_mouse", automation_id="drag.canvas", x=300, y=70, hover_ms=100)
                tool("wpf_assert", element_handle=find("PointerStatus"), property_name="Text", expected_value="Move:300,70")
                tool("wpf_click_element", element_handle=canvas, x=300, y=70)
                tool("wpf_assert", element_handle=find("PointerStatus"), property_name="Text", expected_value="Click:Left:300,70;Count:1")
                tool("wpf_click_element", element_handle=canvas, x=280, y=70, click_type="double")
                tool("wpf_assert", element_handle=find("PointerStatus"), property_name="Text", expected_value="Click:Left:280,70;Count:2")
                tool("wpf_click_element", element_handle=canvas, x=260, y=70, click_type="right")
                tool("wpf_assert", element_handle=find("PointerStatus"), property_name="Text", expected_value="Click:Right:260,70;Count:1")
                tool("wpf_scroll", automation_id="drag.canvas", delta=-240, x=300, y=70)
                tool("wpf_assert", element_handle=find("WheelStatus"), property_name="Text", expected_value="Wheel:-240; Horizontal:0")
                cli("scroll", "--automation-id", "drag.canvas", "--delta", "120", "--horizontal", "--x", "300", "--y", "70")
                tool("wpf_assert", element_handle=find("WheelStatus"), property_name="Text", expected_value="Wheel:-240; Horizontal:120")
                tool("wpf_click_element", automation_id="open.touch")
                assert tool("wpf_wait_for_element", element_name="TouchSurface")["matched"]
                manipulation_handle = find("TouchManipulation")
                gesture_cases = [
                    ("tap", {}, []), ("long_press", {"duration_ms": 600}, ["--duration", "600"]),
                    ("swipe", {"delta_x": 60}, ["--delta-x", "60"]),
                    ("pinch", {"scale": 1.5, "radius": 40}, ["--scale", "1.5", "--radius", "40"]),
                    ("rotate", {"angle_degrees": 30, "radius": 40}, ["--angle", "30", "--radius", "40"])]
                for gesture, parameters, flags in gesture_cases:
                    for via_cli in (False, True):
                        tool("wpf_click_element", automation_id="touch.reset")
                        if via_cli:
                            cli("touch", "--automation-id", "touch.surface", "--gesture", gesture, *flags)
                        else:
                            tool("wpf_touch_gesture", automation_id="touch.surface", gesture=gesture, **parameters)
                        contacts = 2 if gesture in ("pinch", "rotate") else 1
                        assert tool("wpf_wait_for_element", automation_id="touch.telemetry",
                                    text=f'"downs":{contacts},"ups":{contacts}')["matched"]
                        props = tool("wpf_get_element_properties", element_handle=manipulation_handle)["properties"]
                        state = json.loads(next(p["value"] for p in props if p["name"] == "Text"))
                        if gesture == "swipe": assert state["x"] > 30, state
                        if gesture == "pinch": assert state["scale"] > 1.2, state
                        if gesture == "rotate": assert abs(state["rotation"]) > 15, state

            state = tool("wpf_record", action="stop")
            workflow = state["workflow"]
            assert not state["active"]
            raw = json.dumps(workflow)
            assert "elem_" not in raw and "elementHandle" not in raw and "rootHandle" not in raw
            assert any(s["action"] == "AssertElement" for s in workflow["steps"])
            assert any(s["action"] == "ClickElement" and s["target"]["automationId"] == "form.clear" for s in workflow["steps"])
            id_actions = {"SetText", "SelectItem", "AssertElement", "SetProperty", "RevertProperty"}
            if args.gestures:
                id_actions |= {"Drag", "TouchGesture", "MousePointer", "SendKeys"}
                assert all(s["destination"]["automationId"] == "drag.target"
                           for s in workflow["steps"] if s["action"] == "Drag")
            for action in id_actions:
                assert any(s["action"] == action and s.get("target", {}).get("automationId")
                           for s in workflow["steps"]), f"Missing recorded AutomationId for {action}"
            exported = tool("wpf_export_test", format="xunit")["files"]
            project = work / "exported"
            written = cli("export-test", "--format", "xunit", "--out", str(project))
            assert set(written["files"]) == set(exported)
            for name, content in exported.items():
                # C# raw strings preserve checkout line endings. Do not let Python
                # normalize CRLF while comparing the exact CLI and MCP exports.
                with (project / name).open(encoding="utf-8-sig", newline="") as output:
                    assert output.read() == content, f"MCP/CLI export differs: {name}"
            cli("get-recording", "--out", str(work / "raw.json"))
            assert json.loads((work / "raw.json").read_text(encoding="utf-8-sig")) == workflow

            invalid = copy.deepcopy(workflow)
            invalid["steps"][-1]["action"] = "RunShell"
            assert client.tool("wpf_replay", workflow=invalid).get("isError"), "Invalid workflow accepted"
            # Assert the first set-text was not performed before discovering the bad final step.
            tool("wpf_assert", element_handle=username, property_name="Text", expected_value="")
            client.close()
            client = None
        sample.terminate()
        sample.wait(timeout=10)
        environment = os.environ.copy()
        environment["WPF_TEST_APP"] = str(sample_path)
        environment["WPFINSPECT_PATH"] = str(server)
        result = subprocess.run(["dotnet", "test", str(project / "RecordedWorkflow.Tests.csproj"), "--verbosity", "minimal"],
                                env=environment, capture_output=True, text=True, encoding="utf-8", timeout=240)
        (work / "exported-test.log").write_text(result.stdout + result.stderr, encoding="utf-8")
        assert result.returncode == 0, result.stdout + result.stderr
        print(f"PASS: {len(workflow['steps'])} recorded steps; MCP/CLI export identical; invalid workflow rejected before actions; fresh-process xUnit round trip. Gestures={args.gestures}. Artifacts: {work}")
    finally:
        if client:
            client.close()
        if sample.poll() is None:
            sample.terminate()
        sample.wait(timeout=10)


if __name__ == "__main__":
    main()
