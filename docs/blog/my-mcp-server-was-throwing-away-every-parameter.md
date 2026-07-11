---
title: "My MCP server was silently throwing away every parameter — and the AI found it by reading its own logs"
published: false
tags: dotnet, mcp, ai, wpf
canonical_url:
cover_image:
---

I built an MCP server that lets an AI agent inspect and drive running WPF applications —
dump the visual tree, read data bindings, click buttons, type into text boxes. Six
releases in, it worked. Or rather: it looked like it worked, which is a very different
thing, and it took an AI reading my own log files to notice the difference.

This is the story of a one-line bug that made 90% of my API a no-op, why every demo
still passed, and what it taught me about how AI agents actually fail.

## The setup

The architecture is a three-process sandwich:

```
AI agent (Claude Code)
   ↓ MCP protocol, JSON-RPC over stdio
MCP server (.NET 8)
   ↓ named pipes
Inspector DLL, injected into the target WPF app (.NET Framework 4.8)
   ↓ WPF Dispatcher
The live visual tree
```

Requests travel over the pipe as a small envelope: a type tag plus a payload.

```csharp
public static string SerializeRequest(IpcRequest request)
{
    var wrapper = new
    {
        type = request.RequestType,
        data = request        // <-- here be dragons
    };
    return JsonSerializer.Serialize(wrapper, Options);
}
```

`request` is an `IpcRequest` — the abstract base class. The concrete type is something
like `FindElementsRequest`, carrying `TypeName`, `ElementName`, `MaxResults`, and so on.

Spot the bug? I didn't, for six releases.

## The bug

**`System.Text.Json` serializes by the *declared* type, not the runtime type.**

The anonymous object's `data` member is statically typed as `IpcRequest`. The base class
declares exactly two properties: `RequestId` and `RequestType`. So that's all that got
serialized. Every property declared on the derived class — every search filter, every
element handle, every string of text to type — was silently dropped on the floor.

The wire format looked like this:

```json
{"type":"FindElements","data":{"requestId":"a1b2c3","requestType":"FindElements"}}
```

108 bytes, every single time, no matter what the caller asked for.

Downstream, the Inspector deserialized the payload into a `FindElementsRequest` with
every field at its default. `TypeName`? null. `ElementHandle`? empty string. And then it
did exactly what it was told:

- **`find --type Button`** ignored the filter and returned the first 50 elements of the
  tree — about 20 KB of `Grid`, `Border`, `ContentPresenter` noise.
- **Every handle-based operation** — read properties, get bindings, click, set text, send
  keys — failed with `"ElementHandle required"`. All of them. That's 15 of my 20 tools.

The fix, in full:

```csharp
data = (object)request   // serialize by runtime type
```

One cast. Six releases.

## Why every demo passed

Here's the part that actually bothers me.

The operations that *appeared* to work were exactly the ones that take **no parameters**:
screenshot the main window (defaults to the app's main window), dump the visual tree
(defaults to depth 25 from the root). Those are the money-shot demos. Those are what you
run when you show someone the project. They worked perfectly, because there was nothing
to drop.

So the failure mode wasn't "the tool is broken." It was "the tool is impressive in
exactly the ways you show people, and broken in exactly the ways you use it."

And when an agent *did* hit the broken paths, the errors were plausible:

> `Element handle 'elem_00000052' not found.`

That reads like a stale handle. The app restarted, the element was recycled, whatever —
a known, documented, *expected* failure mode of this kind of tool. Both I and the agent
had a ready-made explanation, so neither of us dug further. The bug hid behind its own
error message.

## How it was actually found

I asked Claude to look at my repo, read the logs, and suggest improvements to how the
model finds controls.

It read `%TEMP%\WpfInspector_Debug.log` — which I had been generating for months and
never really *read* — and noticed a pattern I had looked straight past:

```
HandleClientAsync: Read 110 bytes
HandleRequestAsync: requestType=FindElements
HandleClientAsync: Response ready (length=20550)
...
HandleClientAsync: Read 112 bytes
HandleRequestAsync: requestType=GetLayoutInfo
HandleClientAsync: Response ready (length=65)
```

Every request the same ~110 bytes regardless of its parameters. Every `FindElements`
response the same 20550 bytes regardless of its filter. Every handle-based call a
65-byte response — the length of a JSON error object.

That's not a stale handle. That's a request that never carried the handle in the first
place. Three numbers in a log file I'd been staring at for months, and the shape of the
data told the whole story.

The uncomfortable lesson: **I wasn't reading my own telemetry, and my error messages were
good enough to be believed and wrong enough to mislead.** An error message that offers a
plausible cause is worse than one that says "something is broken here", because it stops
the investigation.

## What I changed beyond the fix

**A regression test on the wire format, not the API.** Unit tests all passed before, because
they tested `WpfTools` against a mocked bridge — the serializer was never in the loop. The
new tests assert on the actual JSON:

```csharp
[Fact]
public void SerializeRequest_IncludesDerivedClassProperties()
{
    var json = IpcSerializer.SerializeRequest(new FindElementsRequest
    {
        TypeName = "Button",
        Text = "Save",
        MaxResults = 25
    });

    json.Should().Contain("\"typeName\":\"Button\"");
    json.Should().Contain("\"text\":\"Save\"");
    json.Should().Contain("\"maxResults\":25");
}
```

If you have a serialization boundary, test *the bytes that cross it*. Mocks will happily
confirm that your code calls a method that does nothing.

**Errors that admit uncertainty.** The old message named one likely cause. The new one
names the cause *and* the recovery, without pretending to know which applies:

> Element handle 'elem_00000052' not found. Handles expire when the target app restarts,
> the Inspector is re-injected, or the element is removed from the UI and garbage-collected.
> Re-run `wpf_find_elements` to get fresh handles.

An agent reading this can act on it. An agent reading "the handle may have expired"
concludes the world is fine and moves on — which is exactly what both of us did.

**Then, the features I originally set out to build.** With parameters actually reaching the
Inspector, the query engine I'd been meaning to write became worth writing: find elements
by *visible text* (the caption of a button, even when it lives in a nested template), by
property values, by visibility — with results carrying the element's text, enabled state
and on-screen bounds, so the agent picks the right control in one call instead of five.

And then the same session found a *second* dead feature: binding-error capture, the
project's headline diagnostic, had never captured a single error. The Inspector attaches
a `TraceListener` to `PresentationTraceSources.DataBindingSource` at runtime — but WPF
ignores runtime listener changes unless you call `PresentationTraceSources.Refresh()`
first. The listener was attached, wired up, and deaf. Another one-liner. Another feature
that had been silently doing nothing since the day I wrote it.

## The pattern

Both bugs are the same bug wearing different clothes: **code that runs successfully while
accomplishing nothing.** No exception, no crash, no red test. A serializer that serializes
the wrong thing. A listener that listens to the wrong source. The system reports success at
every layer, and the only evidence is a number in a log file that's smaller than it should be.

These don't get caught by tests that mock the boundary, and they don't get caught by demos
that avoid the parameters. They get caught by someone — or something — willing to read the
boring logs and ask why every response is exactly the same size.

That, it turns out, is a thing AI agents are unreasonably good at.

---

*The project is [WPFVisualTreeMcp](https://github.com/faze79/WPFVisualTreeMcp): an MCP server
that lets AI agents inspect and drive running WPF applications — visual tree, data bindings,
screenshots, clicks and keyboard input, injected into any WPF process with no source changes.
MIT licensed. The bug above is fixed in v0.7.0; the binding-error one in v0.7.1.*
