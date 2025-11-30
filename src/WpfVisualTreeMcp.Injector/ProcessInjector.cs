using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace WpfVisualTreeMcp.Injector;

/// <summary>
/// Handles injection of the Inspector DLL into target WPF processes.
/// </summary>
/// <remarks>
/// This is a stub implementation. Full DLL injection requires native code
/// or more advanced techniques like AppDomain injection via debugging APIs.
///
/// For production use, consider:
/// 1. Using a native C++ helper for CreateRemoteThread-based injection
/// 2. Using .NET debugging APIs (ICorDebug) for managed injection
/// 3. Using a hooking library like EasyHook or Detours
///
/// For development/testing, the preferred approach is to have the target
/// application directly reference the Inspector DLL (self-hosted mode).
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

        try
        {
            var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                throw new InvalidOperationException("Target process has exited");
            }

            // Verify it's a .NET process
            if (!IsManagedProcess(process))
            {
                throw new InvalidOperationException("Target process is not a .NET application");
            }

            // TODO: Implement actual injection
            // This requires either:
            // 1. P/Invoke to CreateRemoteThread with LoadLibrary
            // 2. Using the CLR debugging APIs
            // 3. Using a library like EasyHook

            throw new NotImplementedException(
                "DLL injection is not yet implemented. " +
                "For testing, use self-hosted mode by referencing the Inspector DLL directly.");
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException($"Process with ID {processId} not found");
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

    #region Native Methods (for future implementation)

    // These are placeholder P/Invoke declarations for future DLL injection implementation

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

    private const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint PAGE_READWRITE = 0x04;

    #endregion
}
