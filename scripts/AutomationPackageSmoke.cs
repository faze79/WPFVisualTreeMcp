using System.Diagnostics;
using System.Text.Json;
using WpfVisualTreeMcp.Automation;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var samplePath = args[0];
var exportDirectory = args[1];
var gestures = args.Contains("--gestures");
Check(!File.Exists(Path.Combine(AppContext.BaseDirectory, "WpfVisualTreeMcp.Server.dll")), "SDK unexpectedly depends on Server");
Check(!File.Exists(Path.Combine(AppContext.BaseDirectory, "ModelContextProtocol.dll")), "SDK unexpectedly depends on MCP");
using var process = Process.Start(new ProcessStartInfo(samplePath) { UseShellExecute = false })!;
try
{
    Check(process.WaitForInputIdle(10000), "Sample did not start");
    await using (var app = await WpfApplication.AttachAsync(process.Id))
    {
        var status = app.ByAutomationId("test.status");
        await status.AssertTextAsync("idle");
        var watch = Stopwatch.StartNew();
        await app.ByAutomationId("test.load").ClickAsync();
        await status.WaitForTextAsync("loaded", timeout: TimeSpan.FromSeconds(5));
        Check(watch.ElapsedMilliseconds >= 300, "Exact wait accepted 'not loaded'");
        await status.AssertTextAsync("loaded");
        Console.WriteLine($"PASS: TextBox changed asynchronously to exact 'loaded' in {watch.ElapsedMilliseconds} ms.");
        try { await status.WaitForTextAsync("Loaded", TimeSpan.FromMilliseconds(100)); throw new Exception("Expected exact-match timeout"); }
        catch (TimeoutException e) { Check(e.Message.Contains("last observed: 'loaded'"), e.Message); }
        using (var cancel = new CancellationTokenSource(80))
        {
            try { await app.ByAutomationId("missing").WaitForTextAsync("loaded", cancellationToken: cancel.Token); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { }
        }

        var username = app.ByAutomationId("user.name");
        await username.WaitForAsync(ElementCondition.Enabled);
        await username.GetPropertiesAsync();
        await username.GetBindingsAsync();
        await username.GetResourcesAsync();
        await username.GetWindowResourcesAsync();
        await username.GetStylesAsync();
        await username.GetLayoutInfoAsync();
        await username.GetDataContextAsync();
        await username.EvaluateBindingAsync("Text");
        await username.ExplainTriggersAsync("Background");
        await username.WatchPropertyAsync("Text");
        await username.HighlightAsync(10);
        await app.GetVisualTreeAsync();
        await app.ReportAsync();
        await app.GetResourcesAsync();
        await app.GetWindowResourcesAsync();
        await app.GetBindingErrorsAsync();
        await app.ClearBindingErrorsAsync();
        await app.ExportTreeAsync();
        var image = await app.CaptureScreenshotAsync("render");
        Check(image.Width > 0 && image.ImageBase64.Length > 100, "Screenshot is empty");
        var before = await username.SnapshotAsync("before");
        await username.SetTextAsync("changed");
        var after = await username.SnapshotAsync("after");
        await app.DiffAsync(before.Label, after.Label);

        await app.StartRecordingAsync("SDK package round trip");
        await username.SetTextAsync("Alice");
        await username.WaitForTextAsync("Alice");
        var recorded = await app.GetRecordingAsync();
        Check(recorded.Workflow!.Steps.Count == 1, "Client wait polluted the recording with polling requests");
        await username.AssertTextAsync("Alice");
        Check((await app.WaitForElementAsync(new ElementQuery { AutomationId = "user.name" }, ElementCondition.Enabled)).Matched, "Recorded wait failed");
        await app.ByAutomationId("items.list").SelectItemAsync(5);
        await app.ByAutomationId("items.list").AssertPropertyAsync("SelectedIndex", "5");
        await username.SetPropertyAsync("Tag", "SDK");
        await username.AssertPropertyAsync("Tag", "SDK");
        await username.RevertPropertyAsync("Tag");
        await username.AssertPropertyAsync("Tag", null);

        if (gestures)
        {
            await username.SendKeysAsync("Ctrl+A");
            await app.ByAutomationId("open.drag").ClickAsync();
            await app.ByAutomationId("drag.target").WaitForAsync();
            await app.ByAutomationId("drag.source").DragToAsync(app.ByAutomationId("drag.target"), new DragSettings { DurationMs = 600 });
            await app.ByAutomationId("drag.status").AssertTextAsync("Dropped 1");
            await app.ByAutomationId("drag.box").DragByAsync(20);
            var canvas = app.ByAutomationId("drag.canvas");
            await canvas.MoveMouseAsync(new(300, 70), 100);
            await app.ByAutomationId("drag.pointer").AssertTextAsync("Move:300,70");
            await canvas.ScrollAsync(-120, position: new(300, 70));
            await app.ByAutomationId("drag.wheel").AssertTextAsync("Wheel:-120; Horizontal:0");
            await app.ByAutomationId("open.touch").ClickAsync();
            var surface = app.ByAutomationId("touch.surface");
            await surface.WaitForAsync();
            foreach (var gesture in new[] { "tap", "long_press", "swipe", "pinch", "rotate" })
            {
                await app.ByAutomationId("touch.reset").ClickAsync();
                switch (gesture)
                {
                    case "tap": await surface.TapAsync(); break;
                    case "long_press": await surface.LongPressAsync(new TouchSettings { DurationMs = 600 }); break;
                    case "swipe": await surface.PanAsync(60); break;
                    case "pinch": await surface.ZoomAsync(1.5, new TouchSettings { Radius = 40 }); break;
                    case "rotate": await surface.RotateAsync(30, new TouchSettings { Radius = 40 }); break;
                }
                using var telemetry = JsonDocument.Parse((await app.ByAutomationId("touch.telemetry").GetPropertyAsync("Text"))!);
                var contacts = gesture is "pinch" or "rotate" ? 2 : 1;
                Check(telemetry.RootElement.GetProperty("downs").GetInt32() == contacts && telemetry.RootElement.GetProperty("ups").GetInt32() == contacts, gesture + " touch contacts");
                using var manipulation = JsonDocument.Parse((await app.ByAutomationId("touch.manipulation").GetPropertyAsync("Text"))!);
                if (gesture == "swipe") Check(manipulation.RootElement.GetProperty("x").GetDouble() > 30, "pan effect");
                if (gesture == "pinch") Check(manipulation.RootElement.GetProperty("scale").GetDouble() > 1.2, "zoom effect");
                if (gesture == "rotate") Check(Math.Abs(manipulation.RootElement.GetProperty("rotation").GetDouble()) > 15, "rotation effect");
            }
        }
        var state = await app.StopRecordingAsync();
        Check(state.Workflow!.Steps.Count >= 8, "Recording incomplete");
        Directory.CreateDirectory(exportDirectory);
        foreach (var (name, content) in await app.ExportTestAsync()) await File.WriteAllTextAsync(Path.Combine(exportDirectory, name), content);
        Console.WriteLine($"PASS: SDK inspection/actions and export; {state.Workflow.Steps.Count} steps; physical gestures={gestures}.");
    }
    Check(!process.HasExited, "Disposing the SDK terminated the target process");
    Console.WriteLine("PASS: Dispose detaches without closing the app.");
}
finally
{
    if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); }
}
