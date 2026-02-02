using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WpfVisualTreeMcp.Injector;

/// <summary>
/// Handles injection of the Inspector DLL into target WPF processes.
/// Uses Windows API (CreateRemoteThread + LoadLibrary) for injection.
/// </summary>
public class ProcessInjector : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Injects the Inspector into a target WPF process.
    /// </summary>
    /// <param name="processId">Target process ID.</param>
    /// <returns>True if injection was successful.</returns>
    public InjectionResult InjectIntoProcess(int processId)
    {
        var bootstrapperPath = GetBootstrapperDllPath();
        return InjectIntoProcess(processId, bootstrapperPath);
    }

    /// <summary>
    /// Injects a DLL into a target process and optionally calls an exported function.
    /// </summary>
    /// <param name="processId">Target process ID.</param>
    /// <param name="dllPath">Path to the DLL to inject.</param>
    /// <param name="exportToCall">Optional export function to call after injection.</param>
    /// <returns>Injection result with details.</returns>
    public InjectionResult InjectIntoProcess(int processId, string dllPath, string? exportToCall = "Initialize")
    {
        var result = new InjectionResult { ProcessId = processId, DllPath = dllPath };

        try
        {
            // Validate DLL exists
            if (!File.Exists(dllPath))
            {
                result.Success = false;
                result.Error = $"DLL not found: {dllPath}";
                return result;
            }

            // Get target process
            Process process;
            try
            {
                process = Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                result.Success = false;
                result.Error = $"Process with ID {processId} not found";
                return result;
            }

            if (process.HasExited)
            {
                result.Success = false;
                result.Error = "Target process has exited";
                return result;
            }

            // Check if it's a .NET process
            if (!IsManagedProcess(process))
            {
                result.Success = false;
                result.Error = "Target process is not a .NET application";
                return result;
            }

            // Check architecture match
            if (!IsArchitectureMatch(process))
            {
                result.Success = false;
                result.Error = $"Architecture mismatch: Target process is {(Is64BitProcess(process) ? "64-bit" : "32-bit")}, " +
                              $"but injector is {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}";
                return result;
            }

            // Check if already injected
            if (IsInspectorLoaded(process))
            {
                result.Success = true;
                result.AlreadyInjected = true;
                result.Message = "Inspector is already loaded in the target process";
                return result;
            }

            // Perform injection
            InjectDll(process, dllPath);
            result.DllInjected = true;

            // Wait a bit for DLL to load
            Thread.Sleep(500);

            // Call the export function if specified
            if (!string.IsNullOrEmpty(exportToCall))
            {
                CallExportedFunction(process, dllPath, exportToCall);
                result.ExportCalled = true;
            }

            result.Success = true;
            result.Message = "Injection successful";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            result.Exception = ex;
            return result;
        }
    }

    /// <summary>
    /// Injects a DLL into a process using CreateRemoteThread + LoadLibrary.
    /// </summary>
    private void InjectDll(Process process, string dllPath)
    {
        IntPtr hProcess = IntPtr.Zero;
        IntPtr allocatedMemory = IntPtr.Zero;
        IntPtr hThread = IntPtr.Zero;

        try
        {
            // Open the target process
            hProcess = OpenProcess(
                PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                false,
                process.Id);

            if (hProcess == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to open target process");
            }

            // Get the absolute path to the DLL
            var absolutePath = Path.GetFullPath(dllPath);
            var pathBytes = Encoding.Unicode.GetBytes(absolutePath + '\0');

            // Allocate memory in the target process for the DLL path
            allocatedMemory = VirtualAllocEx(
                hProcess,
                IntPtr.Zero,
                (uint)pathBytes.Length,
                MEM_COMMIT | MEM_RESERVE,
                PAGE_READWRITE);

            if (allocatedMemory == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to allocate memory in target process");
            }

            // Write the DLL path to the allocated memory
            if (!WriteProcessMemory(hProcess, allocatedMemory, pathBytes, (uint)pathBytes.Length, out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to write to target process memory");
            }

            // Get the address of LoadLibraryW in kernel32.dll
            var kernel32 = GetModuleHandle("kernel32.dll");
            if (kernel32 == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to get kernel32.dll handle");
            }

            var loadLibraryAddr = GetProcAddress(kernel32, "LoadLibraryW");
            if (loadLibraryAddr == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to get LoadLibraryW address");
            }

            // Create a remote thread that calls LoadLibraryW with our DLL path
            hThread = CreateRemoteThread(
                hProcess,
                IntPtr.Zero,
                0,
                loadLibraryAddr,
                allocatedMemory,
                0,
                out _);

            if (hThread == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create remote thread");
            }

            // Wait for the thread to complete
            var waitResult = WaitForSingleObject(hThread, 10000); // 10 second timeout
            if (waitResult == WAIT_TIMEOUT)
            {
                throw new TimeoutException("Timeout waiting for DLL to load in target process");
            }

            if (waitResult == WAIT_FAILED)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Wait for remote thread failed");
            }

            // Check if LoadLibrary succeeded by getting the thread exit code
            if (GetExitCodeThread(hThread, out var exitCode) && exitCode == 0)
            {
                throw new InvalidOperationException("LoadLibrary failed in target process (returned NULL)");
            }
        }
        finally
        {
            // Clean up
            if (hThread != IntPtr.Zero)
                CloseHandle(hThread);

            if (allocatedMemory != IntPtr.Zero)
                VirtualFreeEx(hProcess, allocatedMemory, 0, MEM_RELEASE);

            if (hProcess != IntPtr.Zero)
                CloseHandle(hProcess);
        }
    }

    /// <summary>
    /// Calls an exported function in a DLL that's already loaded in the target process.
    /// </summary>
    private void CallExportedFunction(Process process, string dllPath, string exportName)
    {
        IntPtr hProcess = IntPtr.Zero;
        IntPtr hThread = IntPtr.Zero;

        try
        {
            hProcess = OpenProcess(
                PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                false,
                process.Id);

            if (hProcess == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to open target process");
            }

            // Find the DLL's base address in the target process
            var dllName = Path.GetFileName(dllPath);
            IntPtr dllBase = IntPtr.Zero;

            // Refresh module list
            process.Refresh();

            foreach (ProcessModule module in process.Modules)
            {
                if (module.ModuleName.Equals(dllName, StringComparison.OrdinalIgnoreCase))
                {
                    dllBase = module.BaseAddress;
                    break;
                }
            }

            if (dllBase == IntPtr.Zero)
            {
                throw new InvalidOperationException($"DLL '{dllName}' not found in target process modules");
            }

            // Load the DLL in our process to find the export offset
            var localModule = LoadLibraryEx(dllPath, IntPtr.Zero, DONT_RESOLVE_DLL_REFERENCES);
            if (localModule == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to load DLL locally for export resolution");
            }

            try
            {
                var localExportAddr = GetProcAddress(localModule, exportName);
                if (localExportAddr == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"Export '{exportName}' not found in DLL");
                }

                // Calculate the offset
                var offset = (long)localExportAddr - (long)localModule;

                // Calculate the remote address
                var remoteExportAddr = new IntPtr((long)dllBase + offset);

                // Create a remote thread to call the export
                hThread = CreateRemoteThread(
                    hProcess,
                    IntPtr.Zero,
                    0,
                    remoteExportAddr,
                    IntPtr.Zero,
                    0,
                    out _);

                if (hThread == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create remote thread for export call");
                }

                // Wait for completion
                WaitForSingleObject(hThread, 5000);
            }
            finally
            {
                FreeLibrary(localModule);
            }
        }
        finally
        {
            if (hThread != IntPtr.Zero)
                CloseHandle(hThread);

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
                if (name == "clr.dll" || name == "coreclr.dll" || name == "mscorwks.dll" ||
                    name.StartsWith("clrjit") || name == "mscorjit.dll")
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
            process.Refresh();
            foreach (ProcessModule module in process.Modules)
            {
                var name = module.ModuleName.ToLowerInvariant();
                if (name == "wpfvisualtreemcp.inspector.dll" ||
                    name == "wpfvisualtreemcp.bootstrapper.dll")
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
    /// Checks if the target process architecture matches the current process.
    /// </summary>
    public bool IsArchitectureMatch(Process process)
    {
        return Is64BitProcess(process) == Environment.Is64BitProcess;
    }

    /// <summary>
    /// Checks if a process is 64-bit.
    /// </summary>
    public bool Is64BitProcess(Process process)
    {
        if (!Environment.Is64BitOperatingSystem)
            return false;

        if (!IsWow64Process(process.Handle, out var isWow64))
            return false;

        return !isWow64;
    }

    /// <summary>
    /// Gets the path to the Bootstrapper DLL.
    /// </summary>
    public string GetBootstrapperDllPath()
    {
        var assemblyLocation = typeof(ProcessInjector).Assembly.Location;
        var directory = Path.GetDirectoryName(assemblyLocation);
        return Path.Combine(directory!, "WpfVisualTreeMcp.Bootstrapper.dll");
    }

    /// <summary>
    /// Gets the path to the Inspector DLL.
    /// </summary>
    public string GetInspectorDllPath()
    {
        var assemblyLocation = typeof(ProcessInjector).Assembly.Location;
        var directory = Path.GetDirectoryName(assemblyLocation);
        return Path.Combine(directory!, "WpfVisualTreeMcp.Inspector.dll");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    #region Native Methods

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
    private static extern bool VirtualFreeEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        uint dwSize,
        uint dwFreeType);

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
    private static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string lpLibFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    // Process access rights
    private const uint PROCESS_CREATE_THREAD = 0x0002;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_VM_READ = 0x0010;

    // Memory allocation constants
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;

    // Wait constants
    private const uint WAIT_TIMEOUT = 0x00000102;
    private const uint WAIT_FAILED = 0xFFFFFFFF;

    // LoadLibraryEx flags
    private const uint DONT_RESOLVE_DLL_REFERENCES = 0x00000001;

    #endregion
}

/// <summary>
/// Result of an injection attempt.
/// </summary>
public class InjectionResult
{
    public bool Success { get; set; }
    public int ProcessId { get; set; }
    public string? DllPath { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public bool AlreadyInjected { get; set; }
    public bool DllInjected { get; set; }
    public bool ExportCalled { get; set; }
    public Exception? Exception { get; set; }
}
