// Architecture-matching injector helper.
//
// A 64-bit server cannot inject a DLL into a 32-bit target process via the
// usual CreateRemoteThread + LoadLibraryW technique, because the LoadLibraryW
// address it resolves comes from its own (64-bit) kernel32 and is invalid in
// the target's 32-bit address space. This tiny exe is built as 32-bit
// .NET 8 and is spawned by ProcessInjector when a bitness mismatch is detected;
// it just performs the LoadLibrary remote-thread call in matching bitness and
// exits.
//
// Usage:
//   WpfInjectorHelper.exe --pid <id> --dll <path>
//
// Exit codes:
//   0   injection succeeded
//   1   injection failed (LoadLibraryW returned NULL, target gone, etc.)
//   2   bad arguments

using WpfVisualTreeMcp.Injector;

int pid = -1;
string? dll = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--pid" && i + 1 < args.Length && int.TryParse(args[i + 1], out var p))
    {
        pid = p;
        i++;
    }
    else if (args[i] == "--dll" && i + 1 < args.Length)
    {
        dll = args[i + 1];
        i++;
    }
}

if (pid <= 0 || string.IsNullOrEmpty(dll))
{
    Console.Error.WriteLine("Usage: WpfInjectorHelper.exe --pid <id> --dll <path>");
    return 2;
}

try
{
    var injector = new ProcessInjector();
    var ok = injector.InjectBootstrapper(pid, dll);
    if (!ok)
    {
        Console.Error.WriteLine($"InjectBootstrapper returned false for PID {pid}.");
    }
    return ok ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"InjectorHelper error: {ex.GetType().Name}: {ex.Message}");
    return 1;
}
