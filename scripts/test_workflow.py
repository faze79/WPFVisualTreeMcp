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
            assert value(client.tool("wpf_attach", process_id=sample.pid, auto_inject=True))["success"]

            def tool(name, **kw):
                return value(client.tool(name, **kw))

            def find(name):
                results = tool("wpf_find_elements", element_name=name)["elements"]
                exact = [r for r in results if r.get("name") == name]
                assert len(exact) == 1, results
                return exact[0]["handle"]

            cli("record", "start", "--name", "Fresh process round trip")
            username = find("UserNameTextBox")
            text = "Workflow àé漢字 🎯 " + ("x" * 5000)
            tool("wpf_set_text", element_handle=username, text=text)
            tool("wpf_assert", element_handle=username, property_name="Text", expected_value=text)
            count = len(tool("wpf_get_recording")["workflow"]["steps"])
            scoped_wait = tool("wpf_wait_for_element", element_name="ClearButton", root_handle=username, timeout_ms=100)
            assert not scoped_wait["matched"], "Wait ignored its root subtree"
            assert client.tool("wpf_assert", element_handle=username, property_name="Text", expected_value="wrong").get("isError")
            assert len(tool("wpf_get_recording")["workflow"]["steps"]) == count, "Failed assertion was recorded"
            if args.gestures:
                tool("wpf_send_keys", element_handle=username, keys="Ctrl+A")
            tool("wpf_click_element", element_handle=find("ClearButton"))
            tool("wpf_assert", element_handle=username, property_name="Text", expected_value="")
            items = find("ItemsListBox")
            tool("wpf_select_item", element_handle=items, index=5)
            cli("assert", "--handle", items, "--property", "SelectedIndex", "--expected", "5")
            tool("wpf_wait_for_element", element_name="RemoveItemButton", condition="enabled")

            if args.gestures:
                tool("wpf_click_element", element_handle=find("OpenDragDemoButton"))
                assert tool("wpf_wait_for_element", element_name="DropTarget")["matched"]
                tool("wpf_drag", element_handle=find("DragSource"), target_handle=find("DropTarget"),
                     start_x=15, start_y=20, target_x=20, target_y=20, duration_ms=600)
                assert tool("wpf_wait_for_element", element_name="DropStatus", text="Dropped 1")["matched"]
                tool("wpf_click_element", element_handle=find("OpenTouchDemoButton"))
                assert tool("wpf_wait_for_element", element_name="TouchSurface")["matched"]
                tool("wpf_touch_gesture", element_handle=find("TouchSurface"), gesture="tap")
                assert tool("wpf_wait_for_element", element_name="TouchTelemetry", text='"downs":1,"ups":1')["matched"]

            state = tool("wpf_record", action="stop")
            workflow = state["workflow"]
            assert not state["active"]
            raw = json.dumps(workflow)
            assert "elem_" not in raw and "elementHandle" not in raw and "rootHandle" not in raw
            assert any(s["action"] == "AssertElement" for s in workflow["steps"])
            exported = tool("wpf_export_test", format="xunit")["files"]
            project = work / "exported"
            written = cli("export-test", "--format", "xunit", "--out", str(project))
            assert set(written["files"]) == set(exported)
            for name, content in exported.items():
                assert (project / name).read_text(encoding="utf-8-sig") == content
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
