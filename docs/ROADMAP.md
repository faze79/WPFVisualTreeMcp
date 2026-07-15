# Roadmap

Forward-looking development plan. The short "Roadmap" section in the README tracks
shipped phases; this file is the detailed, prioritized plan for what's next and *why*.

Effort key: **S** ≈ hours, **M** ≈ a day or two, **L** ≈ several days.
Status: 💡 idea · 📋 designed · 🚧 in progress · ✅ done.

## Guiding principle

The project's edge is the **insider view** — real visual tree, dependency properties,
bindings and DataContext via injection, with no probe installed in the target app —
**plus the hands to act on it**. Every new capability should deepen one of two loops:

1. **Understand → act → verify** — the agent inspects, changes something, and *confirms
   the change had the intended effect* without a human in between.
2. **Reproduce** — turn an ad-hoc driven session into something durable (a test, a report).

The theme below that the maintainer proposed — *change a property value and immediately
see whether a planned modification is effective* — is loop #1 made first-class, and it's
the top priority.

---

## Priority 1 — Live tweak & measure

The headline theme. Today the agent can **read** properties (`wpf_get_element_properties`,
`wpf_watch_property`) but cannot **write** an arbitrary one. Adding live editing, paired
with a way to *measure* the effect, lets an agent answer "will this change work?" in
seconds instead of an edit-rebuild-relaunch cycle.

### 1a. `wpf_set_property` — live property editing ✅ *(v0.9.0)*

Set an arbitrary dependency property on an element at runtime, with a revert path.

**Why:** Try a layout/style/visibility tweak on the running app and see it instantly —
the Snoop / VS "Live Property Explorer" experience, exposed to an agent. This is the
maintainer's proposal and the single most-requested capability for a live inspector.

**Design (anchored to existing code):**
- Resolve the `DependencyProperty` by name exactly as `PropertyReader.GetDependencyProperty`
  already does.
- Coerce the string value to `dp.PropertyType` via `TypeDescriptor.GetConverter(dp.PropertyType)`
  — this covers the common types out of the box: `Thickness`, `Brush`, `Visibility`,
  `double`, `GridLength`, `Color`, `HorizontalAlignment`, enums, etc.
- Before writing, capture `element.ReadLocalValue(dp)` (returns `DependencyProperty.UnsetValue`
  when there is no local value) so the change can be undone.
- Write with `element.SetValue(dp, converted)` on the UI Dispatcher.
- Reject read-only DPs (`DependencyPropertyKey`) with a clear error; report the coerced
  value read back (like `set-text`'s read-back) so the agent sees what actually landed.

**Revert (shipped):** `wpf_revert_property` restores the saved binding, the saved local
value, or `ClearValue(dp)` when there was none (falls back to style/inherited/default).
A per-session undo stack lets you revert the most recent edit, a filtered one, or `all=true`
to undo a whole experiment. Verified live: overwriting a data-bound `Text` reports
`previousSource: "Binding"` and revert restores the binding.

**Risks:** state-changing and can visibly break the app; mitigated by revert and by
marking it clearly STATE-CHANGING. Some types have no string converter — return a helpful
error listing the property type. Setting a property that is data-bound replaces the binding
with a local value (document this; it's usually what you want for a quick test, and revert
restores the binding).

### 1b. `wpf_snapshot` + `wpf_diff` — before/after snapshot ✅ *(v0.10.0)*

Capture a compact snapshot of an element subtree (layout metrics, key render properties,
visibility, bounds) and diff two snapshots.

**Why:** This is the *measure* half of "is the change effective?". Flow: snapshot →
`wpf_set_property` (or any driving action) → snapshot → **diff shows exactly what moved**
(ActualWidth 0→120, Visibility Collapsed→Visible, a child that appeared). Turns "looks
right in the screenshot" into a precise, machine-checkable delta.

**Design:** reuse `PropertyReader.GetLayoutInfo` + a curated set of visual properties;
serialize a normalized snapshot keyed by element path; diff is a structural compare
producing added/removed/changed entries. Pairs naturally with a before/after
`wpf_capture_screenshot(mode='screen')` for the visual side.

### 1c. `wpf_evaluate_binding` / "why is this the value?" 💡 (M)

Given an element + property, explain where the value comes from (local, style, trigger,
inherited, binding) and, for bindings, resolve the path against the current DataContext
and report each hop's value or the exact failure point.

**Why:** The most common WPF question an agent gets asked is "why is X wrong/empty/disabled?".
We already read bindings and DataContext; this closes the loop by *evaluating* the path,
not just reporting it. Directly reinforces the binding-diagnostics differentiator.

---

## Priority 2 — Reproduce & compete on QA

### 2a. `wpf_record` → `wpf_export_test` 📋 (L) — [issue #13](https://github.com/faze79/WPFVisualTreeMcp/issues/13)

Record a driven workflow and export a runnable xUnit + driver test. Selectors, not
session handles; recorded assertions map onto `wpf_wait_for` + a value read.

**Why:** The strongest competitor (WPF Buddy, 24★) leads on the *test-generation* story —
record → replay → export a maintainable test — which is what the enterprise WPF audience
buys. We have the widest interaction surface and real inspection; this is the missing hook
to compete on their terrain. Design details are in the issue.

### 2b. `wpf_report` — one-call UI health report 💡 (S)

Aggregate binding errors + a visual-tree summary + any disabled/invisible key controls
into a single structured report an agent can open a session with.

**Why:** Cheap, high-signal, and a natural "first thing to run". Reuses existing tools.

---

## Priority 3 — Architecture & reach

### 3a. Streaming notifications (property changes / binding errors) 💡 (L)

`wpf_watch_property` registers a watch, but the one-shot CLI can't stream change events —
you have to re-poll. Add a push channel so an MCP client receives `PropertyChanged` /
`BindingError` notifications as they happen.

**Why:** Real-time debugging ("tell me the moment this binding errors"). This is a
transport/architecture change (MCP notifications + a persistent Inspector→server channel),
hence L. The concurrent-IPC work in v0.8.0 is a prerequisite that's now in place.

### 3b. Inspector-only NuGet package for self-hosted mode 💡 (S)

Ship the Inspector as its own package so an app can reference it and call
`InspectorService.Initialize(...)` at startup, instead of runtime injection.

**Why:** Some teams can't/won't allow injection (hardened processes, policy). Self-hosting
is already supported in code (see the sample app) but not packaged. Also the path to the
in-process diagnostics competitors require — except here it's opt-in, not mandatory.

### 3c. WinUI 3 support 💡 (L)

Extend the injection + tree walking to WinUI 3 / Windows App SDK targets.

**Why:** Expands the addressable market beyond classic WPF; WinUI 3 is where new .NET
desktop work is going. Large because the visual-tree/injection specifics differ.

---

## Backlog / smaller ideas

- **`wpf_element_at_point`** (S) — hit-test a screen coordinate → the element there.
  Useful for "what is this control?" and grounding screenshots to handles.
- **Batch actions** (M) — run an ordered list of tool calls in one round-trip; cuts
  latency for multi-step flows and is a building block for `wpf_record`.
- **Trigger / animation / template inspection** (M) — explain *why* a property has its
  current value when a Style/ControlTemplate trigger or animation is driving it.
- **`wpf_focus` / keyboard-navigation report** (S) — tab order and current focus, for
  accessibility and for reliable `send-keys` targeting.
- **Structured error taxonomy** (S) — machine-readable error codes on IPC responses so
  agents branch on cause (stale-handle vs wrong-type vs not-found) without string-matching.

---

## Suggested sequencing

1. ~~**`wpf_set_property` + `wpf_revert_*`** (1a)~~ — ✅ shipped in v0.9.0.
2. ~~**`wpf_snapshot` + `wpf_diff`** (1b)~~ — ✅ shipped in v0.10.0. The "change → measure →
   is it effective?" loop is now fully automated (live-edit, then diff a before/after snapshot).
3. **`wpf_evaluate_binding`** (1c) and **`wpf_report`** (2b) — cheap diagnostics wins.
4. **`wpf_record` → `wpf_export_test`** (2a) — the big competitive play; batch actions
   (backlog) as a prerequisite.
5. **Streaming** (3a) and **reach** (3b/3c) — larger architectural investments once the
   agent-facing surface is complete.

## Cross-cutting concerns for every new tool

- **Verify live**, not just with mocked unit tests — the last several fixes (dropped IPC
  params, dead binding-error capture, the wait/UI-thread interaction) were only caught by
  driving the sample app. Add an end-to-end check for each new tool.
- **State-changing tools** get a revert/undo path where feasible and are clearly labeled.
- **Selectors over handles** for anything durable — handles are session-scoped.
- **Keep the CLI and MCP tool in lockstep** — every new `[McpServerTool]` gets a matching
  CLI subcommand and `--help`.
