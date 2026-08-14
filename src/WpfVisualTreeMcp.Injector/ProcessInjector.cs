using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WpfVisualTreeMcp.Injector;

/// <summary>
/// Handles injection of the Inspector DLL into target WPF processes.
/// </summary>
/// <remarks>
/// Uses CreateRemoteThread + LoadLibrary technique to inject a native bootstrapper
/// that then loads the managed Inspector DLL via CLR hosting APIs.
/// </remarks>
public class ProcessInjector
{
    /// <summary>
    /// Attempts to inject the Inspector DLL into a target process.
    /// </summary>
    /// <param name="processId">Target process ID.</param>
    /// <param name="inspectorDllPath">Path to the Inspector DLL.</param>
    /// <returns>True if injection was successful.</returns>
    public bool InjectIntoProcess(int processId, string inspectorDllPath)
    {
        if (!File.Exists(inspectorDllPath))
        {
            throw new FileNotFoundException("Inspector DLL not found", inspectorDllPath);
        }

        Process process;
        try
        {
            process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                throw new InvalidOperationException("Target process has exited");
            }
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException($"Process with ID {processId} not found");
        }

        // Verify it's a .NET process
        if (!IsManagedProcess(process))
        {
            throw new InvalidOperationException("Target process is not a .NET application");
        }

        // Check if already loaded
        if (IsInspectorLoaded(process))
        {
            return true; // Already injected
        }

        // Detect target process architecture for correct bootstrapper
        bool targetIs64Bit = IsProcess64Bit(process);

        // Get the bootstrapper DLL path (native DLL that will load the managed Inspector)
        var bootstrapperPath = GetBootstrapperDllPath(targetIs64Bit);
        if (!File.Exists(bootstrapperPath))
        {
            throw new FileNotFoundException(
                $"Bootstrapper DLL not found ({(targetIs64Bit ? "x64" : "x86")}). " +
                "Build the native bootstrapper first or run 'dotnet publish' to include it.",
                bootstrapperPath);
        }

        // Same-bitness injection happens in-process. Cross-bitness injection (e.g.
        // a 64-bit server reaching a 32-bit WPF app) cannot work in-process because
        // the remote LoadLibraryW thread would start at the injector's own kernel32
        // address, which is invalid in the target's address space. Spawn the
        // matching-bitness helper instead so it can do the LoadLibrary call from a
        // process that shares the target's bitness.
        bool injectorIs64Bit = Environment.Is64BitProcess;
        if (targetIs64Bit != injectorIs64Bit)
        {
            return InjectViaHelper(processId, bootstrapperPath, targetIs64Bit);
        }

        return InjectDll(process, bootstrapperPath);
    }

    /// <summary>
    /// Low-level injection entry point: performs CreateRemoteThread + LoadLibraryW
    /// on the given native DLL in the given process, with no architecture detection
    /// or bootstrapper resolution. The caller is responsible for ensuring that the
    /// current process and target process share the same bitness — this is invoked
    /// from inside <see cref="InjectIntoProcess"/> directly (same-bitness path) and
    /// from the cross-bitness helper exe.
    /// </summary>
    public bool InjectBootstrapper(int processId, string bootstrapperDllPath)
    {
        if (!File.Exists(bootstrapperDllPath))
        {
            throw new FileNotFoundException("Bootstrapper DLL not found", bootstrapperDllPath);
        }

        Process process;
        try
        {
            process = Process.GetProcessById(processId);
            if (process.HasExited)
                throw new InvalidOperationException("Target process has exited");
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException($"Process with ID {processId} not found");
        }

        return InjectDll(process, bootstrapperDllPath);
    }

    /// <summary>
    /// Spawns the architecture-matching helper exe to perform the LoadLibrary
    /// injection in the correct bitness. Returns true if the helper reports
    /// success (exit code 0).
    /// </summary>
    private bool InjectViaHelper(int processId, string bootstrapperDllPath, bool targetIs64Bit)
    {
        var helperPath = GetHelperExePath(targetIs64Bit);
        if (!File.Exists(helperPath))
        {
            throw new FileNotFoundException(
                $"Architecture-mismatch injector helper not found ({(targetIs64Bit ? "x64" : "x86")}). " +
                "Build / publish the WpfVisualTreeMcp.InjectorHelper project so " +
                $"'{Path.GetFileName(helperPath)}' lands next to the server.",
                helperPath);
        }

        var psi = new ProcessStartInfo
        {
            FileName = helperPath,
            // Quote the DLL path in case it contains spaces; both --pid and the
            // helper's own filename are guaranteed not to.
            Arguments = $"--pid {processId} --dll \"{bootstrapperDllPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var helperProc = Process.Start(psi);
        if (helperProc == null)
            return false;

        if (!helperProc.WaitForExit(15000))
        {
            try { helperProc.Kill(); } catch { /* best effort */ }
            throw new TimeoutException(
                $"Architecture-mismatch helper timed out injecting into process {processId}.");
        }

        // Drain output streams to avoid the helper hanging on a full pipe.
        var stdout = helperProc.StandardOutput.ReadToEnd();
        var stderr = helperProc.StandardError.ReadToEnd();
        // (Streams are surfaced via the helper's exit code; we deliberately don't
        // throw on non-empty stderr because warnings are informational.)
        _ = stdout;
        _ = stderr;

        return helperProc.ExitCode == 0;
    }

    /// <summary>
    /// Resolves the path to the bitness-matching injector helper exe (looks under
    /// <c>native/{arch}/WpfInjectorHelper.exe</c> next to this assembly, falling
    /// back to the dev build output).
    /// </summary>
    private string GetHelperExePath(bool targetIs64Bit)
    {
        var assemblyLocation = typeof(ProcessInjector).Assembly.Location;
        var directory = Path.GetDirectoryName(assemblyLocation)!;
        var arch = targetIs64Bit ? "x64" : "x86";

        // 1. native/{arch}/ subdirectory (publish layout — what we ship)
        var nativePath = Path.Combine(directory, "native", arch, "WpfInjectorHelper.exe");
        if (File.Exists(nativePath))
            return nativePath;

        // 2. Build output relative to source (dev environment). The InjectorHelper
        // is published per-RID, so its bin layout is bin/<Configuration>/net8.0/win-<arch>/.
        foreach (var config in new[] { "Release", "Debug" })
        {
            var devPath = Path.GetFullPath(Path.Combine(
                directory, "..", "..", "..", "..",
                "src", "WpfVisualTreeMcp.InjectorHelper",
                "bin", config, "net8.0", $"win-{arch}",
                "WpfInjectorHelper.exe"));
            if (File.Exists(devPath))
                return devPath;
        }

        // Return the expected publish path for the error message.
        return nativePath;
    }

    /// <summary>
    /// Injects a native DLL into the target process using CreateRemoteThread + LoadLibrary.
    /// </summary>
    private bool InjectDll(Process process, string dllPath)
    {
        IntPtr hProcess = IntPtr.Zero;
        IntPtr allocatedMem = IntPtr.Zero;
        IntPtr hThread = IntPtr.Zero;

        try
        {
            // Open target process with required access
            hProcess = OpenProcess(
                PROCESS_CREATE_THREAD | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ | PROCESS_QUERY_INFORMATION,
                false,
                process.Id);

            if (hProcess == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"Failed to open process {process.Id}");
            }

            // Get full path and convert to bytes (Unicode for LoadLibraryW)
            var fullPath = Path.GetFullPath(dllPath);
            var pathBytes = Encoding.Unicode.GetBytes(fullPath + "\0");

            // Allocate memory in target process for the DLL path
            allocatedMem = VirtualAllocEx(
                hProcess,
                IntPtr.Zero,
                (uint)pathBytes.Length,
                MEM_COMMIT | MEM_RESERVE,
                PAGE_READWRITE);

            if (allocatedMem == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Failed to allocate memory in target process");
            }

            // Write DLL path to allocated memory
            if (!WriteProcessMemory(hProcess, allocatedMem, pathBytes, (uint)pathBytes.Length, out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Failed to write DLL path to target process memory");
            }

            // Get address of LoadLibraryW in kernel32.dll
            var kernel32 = GetModuleHandle("kernel32.dll");
            if (kernel32 == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Failed to get kernel32.dll handle");
            }

            var loadLibraryAddr = GetProcAddress(kernel32, "LoadLibraryW");
            if (loadLibraryAddr == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Failed to get LoadLibraryW address");
            }

            // Create remote thread to call LoadLibraryW with our DLL path
            hThread = CreateRemoteThread(
                hProcess,
                IntPtr.Zero,
                0,
                loadLibraryAddr,
                allocatedMem,
                0,
                out _);

            if (hThread == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Failed to create remote thread");
            }

            // Wait for thread to complete (with timeout)
            var waitResult = WaitForSingleObject(hThread, 10000); // 10 second timeout
            if (waitResult == WAIT_TIMEOUT)
            {
                throw new TimeoutException("Remote thread timed out while loading DLL");
            }
            else if (waitResult == WAIT_FAILED)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Failed to wait for remote thread");
            }

            // Check if LoadLibrary succeeded by getting the thread exit code
            if (!GetExitCodeThread(hThread, out uint exitCode))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Failed to get remote thread exit code");
            }

            // LoadLibrary returns the module handle (non-zero on success)
            return exitCode != 0;
        }
        finally
        {
            // Cleanup
            if (hThread != IntPtr.Zero)
                CloseHandle(hThread);
            if (allocatedMem != IntPtr.Zero)
                VirtualFreeEx(hProcess, allocatedMem, 0, MEM_RELEASE);
            if (hProcess != IntPtr.Zero)
                CloseHandle(hProcess);
        }
    }

    /// <summary>
    /// Checks if a process is likely a managed (.NET) process.
    /// </summary>
    public bool IsManagedProcess(Process process)
    {
        try
        {
            foreach (ProcessModule module in process.Modules)
            {
                var name = module.ModuleName.ToLowerInvariant();
                if (name == "clr.dll" || name == "coreclr.dll" || name == "mscorwks.dll")
                {
                    return true;
                }
            }
        }
        catch
        {
            // Can't access modules - assume not managed or insufficient permissions
        }

        return false;
    }

    /// <summary>
    /// Checks if the Inspector DLL is already loaded in a process.
    /// </summary>
    public bool IsInspectorLoaded(Process process)
    {
        try
        {
            foreach (ProcessModule module in process.Modules)
            {
                if (module.ModuleName.Equals("WpfVisualTreeMcp.Inspector.dll", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Can't access modules
        }

        return false;
    }

    /// <summary>
    /// Gets the path where the Inspector DLL should be located.
    /// </summary>
    public string GetInspectorDllPath()
    {
        return GetInspectorDllPath(Environment.Is64BitProcess);
    }

    /// <summary>
    /// Gets the path where the Inspector DLL should be located for the target process.
    /// </summary>
    public string GetInspectorDllPath(Process targetProcess)
    {
        ArgumentNullException.ThrowIfNull(targetProcess);
        return GetInspectorDllPath(IsProcess64Bit(targetProcess));
    }

    private string GetInspectorDllPath(bool targetIs64Bit)
    {
        var assemblyLocation = typeof(ProcessInjector).Assembly.Location;
        var directory = Path.GetDirectoryName(assemblyLocation)!;
        var fileName = "WpfVisualTreeMcp.Inspector.dll";
        var sameDirPath = Path.Combine(directory, fileName);
        if (File.Exists(sameDirPath))
            return sameDirPath;

        var arch = targetIs64Bit ? "x64" : "x86";
        return Path.Combine(directory, "native", arch, fileName);
    }

    /// <summary>
    /// Gets the path where the native bootstrapper DLL should be located.
    /// Searches multiple locations to support both dev and publish layouts.
    /// </summary>
    public string GetBootstrapperDllPath(bool targetIs64Bit = true)
    {
        var assemblyLocation = typeof(ProcessInjector).Assembly.Location;
        var directory = Path.GetDirectoryName(assemblyLocation)!;
        var arch = targetIs64Bit ? "x64" : "x86";
        var win32Arch = targetIs64Bit ? "x64" : "Win32";

        // 1. Same directory (legacy/dev layout)
        var sameDirPath = Path.Combine(directory, "WpfInspectorBootstrapper.dll");
        if (File.Exists(sameDirPath))
            return sameDirPath;

        // 2. native/{arch}/ subdirectory (publish layout)
        var nativePath = Path.Combine(directory, "native", arch, "WpfInspectorBootstrapper.dll");
        if (File.Exists(nativePath))
            return nativePath;

        // 3. Build output relative to source (dev environment)
        var devPath = Path.GetFullPath(Path.Combine(directory, "..", "..", "..", "src",
            "WpfVisualTreeMcp.Bootstrapper", "build", win32Arch, "Release",
            "WpfInspectorBootstrapper.dll"));
        if (File.Exists(devPath))
            return devPath;

        // Return the expected publish path for error messaging
        return nativePath;
    }

    /// <summary>
    /// Determines whether the target process is 64-bit.
    /// </summary>
    private bool IsProcess64Bit(Process process)
    {
        if (!Environment.Is64BitOperatingSystem)
            return false;

        try
        {
            if (!IsWow64Process(process.Handle, out bool isWow64))
                return Environment.Is64BitProcess; // fallback
            return !isWow64; // if NOT running under WoW64, it's a 64-bit process
        }
        catch
        {
            return Environment.Is64BitProcess; // fallback
        }
    }

    #region Native Methods

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint dwDesiredAccess,
        bool bInheritHandle,
        int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        uint dwSize,
        uint flAllocationType,
        uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        uint nSize,
        out int lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(
        IntPtr hProcess,
        IntPtr lpThreadAttributes,
        uint dwStackSize,
        IntPtr lpStartAddress,
        IntPtr lpParameter,
        uint dwCreationFlags,
        out uint lpThreadId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        uint dwSize,
        uint dwFreeType);

    // Process access rights
    private const uint PROCESS_CREATE_THREAD = 0x0002;
    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    // Memory allocation types
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint MEM_RELEASE = 0x8000;

    // Memory protection
    private const uint PAGE_READWRITE = 0x04;

    // Wait return values
    private const uint WAIT_OBJECT_0 = 0x00000000;
    private const uint WAIT_TIMEOUT = 0x00000102;
    private const uint WAIT_FAILED = 0xFFFFFFFF;

    #endregion
}
