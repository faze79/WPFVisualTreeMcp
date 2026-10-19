# Tools Reference

Complete reference for all MCP tools provided by WpfVisualTreeMcp.

## Aggregate diagnostics

`wpf_report(root_handle?, max_elements=500, max_depth=25, max_findings=100)`
scans the realized visual tree across open windows, or the selected subtree.
It reports current binding and validation errors with element handles for follow-up,
plus counts of disabled, invisible and zero-size elements. These state counts are
observations, not automatic diagnoses. The report does not materialize virtualized items.

The result includes `capturedAtUtc`, `processId`, `scope`, `scannedElements`,
`bindingsChecked`, `findings`, `findingCount` and `truncationReasons`.
`recentBindingErrors` contains at most 50 captured process-wide traces since Inspector
attachment, including when a subtree is selected; they are historical evidence and
may no longer apply. `capturedBindingErrorCount` is the retained history count.
Strings are limited to 2048 characters, with an ellipsis when shortened.

Limits are 1–2000 elements, depth 1–100 and 1–500 findings, with a cooperative
three-second scan budget. Each omitted portion is identified in `truncationReasons`;
an empty findings list with incomplete coverage is not a clean bill of health.

CLI equivalent: `wpfinspect report --pid 1234 --root elem_00000001 --max 500 --depth 25 --max-findings 100`.

## Screenshots

### wpf_capture_screenshot

Use `element_handle` to select a control, or omit it for the main window.
`mode="render"` (default) renders off-screen; `mode="screen"` captures visible
desktop pixels, including open popups and menus, and requires an unobstructed window.

Set `full_content=true` with **render mode** to capture the content of a
ScrollViewer or a control containing one. Non-virtualized content is rendered
directly; virtualized lists are paged and stitched. Logically scrolling controls
currently support vertical stitching only. Target a specific scroll control when
several are present; otherwise the descendant ScrollViewer with the largest extent
is selected.

**Full-content capture temporarily changes scroll position** and can trigger the
application's scroll/layout handlers. Original offsets are restored in a `finally`
block, including failures. It shares the interaction gate with drag, touch and other
mutations, which are rejected while capture is running. Changing application data
during capture can affect the result.

`max_width`/`max_height` default to 1920/1080. Full-content requests accept up to
16384 per dimension, with a separate 8-megapixel output cap; ordinary screenshots
retain their 3840/2160 limits. Full-content capture uses bounded render tiles,
a 64-megapixel retained-image budget, a 32-MiB PNG cap and a cooperative 25-second
deadline. Bounds failures return errors. Application code that blocks the UI thread
cannot be forcibly interrupted by the Inspector.

```powershell
wpfinspect screenshot --pid 1234 --handle elem_42 --full-content --max-height 4000 --out full.png
```

## Mouse Interaction

### wpf_drag

Perform a complete physical mouse drag: press, move along a straight path, release.
Useful for drag-and-drop, slider thumbs, splitters and custom canvas controls.
**State-changing:** moves the OS cursor and activates the source window.

| Parameter | Default | Meaning |
|-----------|---------|---------|
| `element_handle` | Required | Source element from `wpf_find_elements` |
| `target_handle` | Omitted | Destination element; mutually exclusive with displacement |
| `delta_x`, `delta_y` | Omitted | Displacement in the source element's local WPF DIPs; omitted axis is zero |
| `start_x`, `start_y` | Source center | Optional pair, relative to the source's top-left, in WPF DIPs |
| `target_x`, `target_y` | Target center | Optional pair, relative to the target's top-left, in WPF DIPs; requires `target_handle` |
| `button` | `left` | `left`, `right`, or `middle` |
| `duration_ms` | `500` | Movement duration, from 100 to 10000 ms (plus brief press/release settling) |

Provide **either a target handle or a non-zero displacement**. Local coordinate
pairs must be complete and inside the respective element's rendered bounds.
WPF transforms and DPI conversion are applied before sending OS input. For a
slider, find its `Thumb` descendant and use that as the source.

```json
{"element_handle":"elem_00000042","target_handle":"elem_00000057","duration_ms":700}
```

```json
{"element_handle":"elem_00000042","delta_x":-80,"button":"left"}
```

The result contains `success`, `method: "Physical.Drag"`, `element_type`, `button`,
`duration_ms`, `start` and `end` coordinates with `coordinate_space: "screen_pixels"`.
Success means the input sequence was sent; it does **not** establish that the
application accepted a drop. Read properties, use `wpf_wait_for`, or take a
screenshot to verify the outcome.

Both endpoints must be on a monitor and unobstructed in the attached application.
Elements are brought into view before resolving coordinates; the tool does not
scroll during a drag or drag into a different process. The desktop must be
interactive and unlocked. Keep the physical mouse idle during the operation.
Focus loss cancels movement, and button release is attempted on every error or
cancellation after a press. Native input failures are returned as errors.
Concurrent state-changing Inspector commands are rejected while an interaction
is in progress; read-only inspection remains available.

CLI equivalents:

```powershell
WpfVisualTreeMcp.Server.exe drag --pid 1234 --handle elem_00000042 --target elem_00000057 --duration 700
WpfVisualTreeMcp.Server.exe drag --pid 1234 --handle elem_00000042 --delta-x -80 --button left
```

The CLI uses `--start-x/--start-y` and `--target-x/--target-y` for local points.
Output is JSON with `success`, `result` and `coordinateSpace`; failures exit with code 1.

To exercise the implementation, open **Drag demo** in `SampleWpfApp`. Run
`./scripts/Test-Drag.ps1` on an interactive Windows desktop after building Release
to test the real MCP/CLI path on net48 x64/x86 and net8.0-windows x64. These desktop
tests are kept separate from unattended CI; the unit suite covers invalid inputs,
IPC serialization, cancellation, input failure, and release cleanup.

## Touch Interaction

### wpf_touch_gesture

Inject native Windows touch input using one or two contacts. **State-changing:**
activates the element's window. Use it for touch-enabled controls and manipulation
surfaces. It simulates touchscreen contacts, not precision-touchpad gestures.

| Parameter | Default | Meaning |
|-----------|---------|---------|
| `element_handle` | Required | Target element containing the complete gesture |
| `gesture` | Required | `tap`, `long_press`, `swipe`, `pinch`, or `rotate` |
| `x`, `y` | Element center | Optional pair in local WPF DIPs; start point for swipe, center for other gestures |
| `delta_x`, `delta_y` | Omitted | Swipe displacement; at least one non-zero axis required; omitted axis is zero |
| `scale` | Required for pinch | Final/initial finger separation, 0.1–10 except 1; above 1 spreads, below 1 contracts |
| `angle_degrees` | Required for rotate | -360–360 except 0; positive is clockwise in local coordinates |
| `radius` | Fitted to element | Initial distance of each finger from the center for pinch/rotate; minimum 4 DIPs |
| `duration_ms` | Gesture-specific | Tap: 100 (range 50–500); long press: 1000 (500–10000); others: 700 (100–10000) |

Parameters belonging to a different gesture are rejected. Both fingers start on a
horizontal line through the center. WPF transforms and DPI are applied to the whole
path. Every point must remain inside the rendered element and visible; contacts must
stay at least 8 screen pixels apart. No input is sent if preflight validation fails.

Examples of MCP arguments:

```json
{"element_handle":"elem_42","gesture":"tap"}
{"element_handle":"elem_42","gesture":"long_press","duration_ms":1200}
{"element_handle":"elem_42","gesture":"swipe","x":80,"y":120,"delta_x":200}
{"element_handle":"elem_42","gesture":"pinch","scale":1.5,"radius":50}
{"element_handle":"elem_42","gesture":"rotate","angle_degrees":-90,"radius":60}
```

The result contains `success`, `method: "Physical.Touch"`, `gesture`, `element_type`,
`contact_count`, `duration_ms`, and `start`/`end` arrays in `screen_pixels`.
Success means Windows accepted the contact sequence; it does not prove the app
recognized a gesture. The tool leaves the target's touch handlers and compatibility
settings unchanged. Query the resulting state or take a screenshot after the action.

CLI equivalents:

```powershell
WpfVisualTreeMcp.Server.exe touch --pid 1234 --handle elem_42 --gesture tap
WpfVisualTreeMcp.Server.exe touch --pid 1234 --handle elem_42 --gesture swipe --delta-x -100
WpfVisualTreeMcp.Server.exe touch --pid 1234 --handle elem_42 --gesture pinch --scale 1.5 --radius 50
WpfVisualTreeMcp.Server.exe touch --pid 1234 --handle elem_42 --gesture rotate --angle -90
```

Other CLI options are `--x`, `--y`, `--delta-y`, and `--duration`. CLI JSON contains
`success`, `result` and `coordinateSpace`; errors exit with code 1.

#### Touch compatibility and verification

The Windows touch injection API requires Windows 8+. Use an unlocked interactive
desktop and keep physical input idle. The target must implement touch or WPF
manipulations (`IsManipulationEnabled` and the relevant event handlers).

Some legacy WPF input configurations ignore injected contacts even when Windows
accepts them. For applications you own, WPF's optional WM_POINTER stack can be
enabled **before any windows are created** (Windows 10 Creators Update or later):

```csharp
AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.EnablePointerSupport", true);
```

For .NET Framework the equivalent app.config setting is:

```xml
<configuration>
  <runtime>
    <AppContextSwitchOverrides value="Switch.System.Windows.Input.Stylus.EnablePointerSupport=true" />
  </runtime>
</configuration>
```

The sample explicitly opts in at startup. The Inspector does not enable this switch
inside arbitrary running applications. Review Microsoft's [WPF pointer-stack compatibility
notes](https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/mitigation-pointer-based-touch-and-stylus-support)
before changing an application's input configuration. Native API reference:
[InjectTouchInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-injecttouchinput).

Open **Touch demo** in the sample to see actual touch counts, hold time, translation,
scale and rotation. Run `./scripts/Test-Touch.ps1` on an interactive Windows desktop
after a Release build. It checks real WPF touch/manipulation events on net48 x64/x86
and net8.0-windows x64, including both pinch directions and signed rotation. The
unattended unit suite covers geometry, argument validation, contact IDs, native
structure alignment and cleanup after cancellation or injection failure.

## Process Management

### wpf_list_processes

List all running WPF applications available for inspection.

**Parameters:** None

**Returns:**
```json
{
  "processes": [
    {
      "process_id": 1234,
      "process_name": "MyWpfApp",
      "main_window_title": "My Application",
      "is_attached": false,
      "dotnet_version": "4.8.0"
    }
  ]
}
```

**Example Usage:**
```
Show me all running WPF applications I can inspect.
```

---

### wpf_attach

Attach to a WPF application by process ID or name.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `process_id` | integer | No* | Process ID to attach to |
| `process_name` | string | No* | Process name to attach to |
| `auto_inject` | boolean | No | Inject if needed (default `false`) |

*At least one of `process_id` or `process_name` must be provided.

**Returns:**
```json
{
  "success": true,
  "process_id": 1234,
  "session_id": "abc123",
  "main_window_handle": "window_0x12345",
  "inspector_ready": true,
  "inspector_status": "Loaded (injected)"
}
```

`success` and `inspector_ready` are `false` if injection fails, initialization
times out, or no Inspector is running with `auto_inject=false`. Check them
before calling inspection tools. `inspector_status` explains the failure;
subsequent tools report the process ID and this cause. Retry attach after
fixing the problem. CLI `attach` exposes `inspectorReady` and exits with code 1
on failure.

**Example Usage:**
```
Attach to the WPF application with process ID 1234.
```
```
Attach to MyWpfApp.exe so I can inspect it.
```

---

## Tree Navigation

### wpf_get_visual_tree

Get the visual tree hierarchy starting from a root element.

**Parameters:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `root_handle` | string | No | Main window | Handle of root element |
| `max_depth` | integer | No | 10 | Maximum tree depth to return |

**Returns:**
```json
{
  "root": {
    "handle": "elem_0x12345",
    "type": "System.Windows.Controls.Grid",
    "name": "LayoutRoot",
    "children": [
      {
        "handle": "elem_0x12346",
        "type": "System.Windows.Controls.StackPanel",
        "name": null,
        "children": []
      }
    ]
  },
  "total_elements": 42,
  "max_depth_reached": false
}
```

**Example Usage:**
```
Show me the visual tree of the main window, up to 5 levels deep.
```

---

### wpf_find_elements

Search for elements by type, name, or property value.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `type_name` | string | No | Element type (e.g., "Button", "TextBox") |
| `element_name` | string | No | x:Name of the element |
| `property_filter` | object | No | Property name/value pairs to filter by |

**Returns:**
```json
{
  "elements": [
    {
      "handle": "elem_0x12345",
      "type": "System.Windows.Controls.Button",
      "name": "SubmitButton",
      "path": "Window > Grid > StackPanel > Button"
    }
  ],
  "count": 1
}
```

**Example Usage:**
```
Find all Button elements in the visual tree.
```
```
Find elements named "SubmitButton".
```
```
Find all TextBox elements where IsEnabled is false.
```

---

## Property Inspection

### wpf_get_element_properties

Get all dependency properties of a UI element.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `element_handle` | string | Yes | Handle of the element to inspect |

**Returns:**
```json
{
  "element": {
    "handle": "elem_0x12345",
    "type": "System.Windows.Controls.Button"
  },
  "properties": [
    {
      "name": "Content",
      "type": "System.String",
      "value": "Click Me",
      "source": "Local"
    },
    {
      "name": "IsEnabled",
      "type": "System.Boolean",
      "value": "True",
      "source": "Default"
    }
  ]
}
```

**Property Sources:**
- `Default` - Default value from property metadata
- `Local` - Set directly on the element
- `Style` - Set via a Style
- `Template` - Set via a ControlTemplate
- `Inherited` - Inherited from parent element
- `Animation` - Set by an animation

**Example Usage:**
```
Show me all properties of the element with handle "elem_0x12345".
```

---

### wpf_get_layout_info

Get layout information for an element.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `element_handle` | string | Yes | Handle of the element |

**Returns:**
```json
{
  "element": {
    "handle": "elem_0x12345",
    "type": "System.Windows.Controls.Grid"
  },
  "layout": {
    "actual_width": 800,
    "actual_height": 600,
    "desired_size": { "width": 800, "height": 600 },
    "render_size": { "width": 800, "height": 600 },
    "margin": { "left": 10, "top": 10, "right": 10, "bottom": 10 },
    "padding": { "left": 5, "top": 5, "right": 5, "bottom": 5 },
    "horizontal_alignment": "Stretch",
    "vertical_alignment": "Stretch",
    "visibility": "Visible"
  }
}
```

**Example Usage:**
```
What are the dimensions and margins of the LayoutRoot grid?
```

---

## Binding Analysis

### wpf_get_bindings

Get all data bindings for an element with their status.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `element_handle` | string | Yes | Handle of the element |

**Returns:**
```json
{
  "element": {
    "handle": "elem_0x12345",
    "type": "System.Windows.Controls.TextBox"
  },
  "bindings": [
    {
      "property": "Text",
      "path": "UserName",
      "source": "ViewModel",
      "mode": "TwoWay",
      "update_trigger": "PropertyChanged",
      "status": "Active",
      "current_value": "John Doe"
    }
  ]
}
```

**Binding Statuses:**
- `Active` - Binding is working correctly
- `Unresolved` - Source property not found
- `PathError` - Invalid binding path
- `Inactive` - Binding is not active

**Example Usage:**
```
Show me all bindings on the UserNameTextBox element.
```

---

### wpf_get_binding_errors

List all binding errors in the application.

**Parameters:** None

**Returns:**
```json
{
  "errors": [
    {
      "element_handle": "elem_0x12345",
      "element_type": "System.Windows.Controls.TextBox",
      "element_name": "UserNameTextBox",
      "property": "Text",
      "binding_path": "UserNmae",
      "error_type": "PathError",
      "message": "BindingExpression path error: 'UserNmae' property not found on 'ViewModel'"
    }
  ],
  "count": 1
}
```

**Example Usage:**
```
Are there any binding errors in the application?
```

---

## Resources & Styles

### wpf_get_resources

Enumerate resource dictionaries and their contents.

**Parameters:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `scope` | string | No | "application" | Scope: "application", "window", or "element" |
| `element_handle` | string | No | - | Required when scope is "element" |

**Returns:**
```json
{
  "resources": [
    {
      "key": "PrimaryBrush",
      "type": "System.Windows.Media.SolidColorBrush",
      "value": "#FF0078D7",
      "source": "App.xaml"
    },
    {
      "key": "ButtonStyle",
      "type": "System.Windows.Style",
      "target_type": "System.Windows.Controls.Button",
      "source": "Themes/Generic.xaml"
    }
  ]
}
```

**Example Usage:**
```
List all application-level resources.
```
```
What resources are defined at the window level?
```

---

### wpf_get_styles

Get applied styles and templates for an element.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `element_handle` | string | Yes | Handle of the element |

**Returns:**
```json
{
  "element": {
    "handle": "elem_0x12345",
    "type": "System.Windows.Controls.Button"
  },
  "style": {
    "key": "PrimaryButtonStyle",
    "target_type": "System.Windows.Controls.Button",
    "based_on": "DefaultButtonStyle",
    "setters": [
      { "property": "Background", "value": "#FF0078D7" },
      { "property": "Foreground", "value": "#FFFFFFFF" }
    ]
  },
  "template": {
    "type": "ControlTemplate",
    "visual_tree_summary": "Border > ContentPresenter"
  }
}
```

**Example Usage:**
```
What style is applied to the SubmitButton?
```

---

## Monitoring

### wpf_watch_property

Monitor a property for changes.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `element_handle` | string | Yes | Handle of the element |
| `property_name` | string | Yes | Name of the property to watch |

**Returns:**
```json
{
  "watch_id": "watch_123",
  "element_handle": "elem_0x12345",
  "property_name": "Text",
  "initial_value": "Hello"
}
```

**Note:** Property changes are reported as MCP notifications.

**Example Usage:**
```
Watch the Text property of the SearchTextBox for changes.
```

---

## Visualization

### wpf_highlight_element

Visually highlight an element in the running application.

**Parameters:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `element_handle` | string | Yes | - | Handle of the element |
| `duration_ms` | integer | No | 2000 | How long to show highlight (ms) |

**Returns:**
```json
{
  "success": true,
  "element_handle": "elem_0x12345",
  "duration_ms": 2000
}
```

**Example Usage:**
```
Highlight the SubmitButton in the application so I can see where it is.
```

---

## Export

### wpf_export_tree

Export visual tree to XAML or JSON.

**Parameters:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `element_handle` | string | No | Main window | Handle of root element |
| `format` | string | No | "json" | Export format: "xaml" or "json" |

**Returns:**
```json
{
  "format": "json",
  "content": "{ ... serialized tree ... }",
  "element_count": 42
}
```

For XAML format:
```json
{
  "format": "xaml",
  "content": "<Grid x:Name=\"LayoutRoot\">...</Grid>",
  "element_count": 42
}
```

**Example Usage:**
```
Export the visual tree to JSON so I can analyze it offline.
```
```
Generate XAML representation of the current window's visual tree.
```

---

## Comparison

### wpf_get_visual_tree_diff

Compare visual tree snapshots to detect changes.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `snapshot_id_a` | string | Yes | First snapshot ID |
| `snapshot_id_b` | string | Yes | Second snapshot ID |

**Returns:**
```json
{
  "added": [
    { "handle": "elem_new", "type": "Button", "parent": "elem_0x12345" }
  ],
  "removed": [
    { "handle": "elem_old", "type": "TextBlock", "parent": "elem_0x12345" }
  ],
  "modified": [
    {
      "handle": "elem_0x12346",
      "changes": [
        { "property": "Text", "old_value": "Hello", "new_value": "World" }
      ]
    }
  ]
}
```

**Example Usage:**
```
Compare the visual tree snapshots before and after the button click.
```
