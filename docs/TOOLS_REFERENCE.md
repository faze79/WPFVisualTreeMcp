# Tools Reference

Complete reference for all MCP tools provided by WpfVisualTreeMcp.

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

*At least one of `process_id` or `process_name` must be provided.

**Returns:**
```json
{
  "success": true,
  "process_id": 1234,
  "session_id": "abc123",
  "main_window_handle": "window_0x12345"
}
```

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
