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

        return InjectDll(process, bootstrapperPath);
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
        var assemblyLocation = typeof(ProcessInjector).Assembly.Location;
        var directory = Path.GetDirectoryName(assemblyLocation);
        return Path.Combine(directory!, "WpfVisualTreeMcp.Inspector.dll");
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
