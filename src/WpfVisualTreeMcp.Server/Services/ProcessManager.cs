using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace WpfVisualTreeMcp.Server.Services;

/// <summary>
/// Implementation of process management for WPF applications.
/// </summary>
public class ProcessManager : IProcessManager
{
    private readonly ILogger<ProcessManager> _logger;
    private InspectionSession? _currentSession;
    private readonly object _lock = new();

    public ProcessManager(ILogger<ProcessManager> logger)
    {
        _logger = logger;
    }

    public InspectionSession? CurrentSession
    {
        get
        {
            lock (_lock)
            {
                return _currentSession;
            }
        }
    }

    public Task<IReadOnlyList<WpfProcessInfo>> GetWpfProcessesAsync()
    {
        var wpfProcesses = new List<WpfProcessInfo>();

        try
        {
            var processes = Process.GetProcesses();

            foreach (var process in processes)
            {
                try
                {
                    // Check if this might be a WPF application
                    // A more robust check would involve querying loaded modules
                    if (IsLikelyWpfProcess(process))
                    {
                        var isAttached = _currentSession?.ProcessId == process.Id;

                        wpfProcesses.Add(new WpfProcessInfo
                        {
                            ProcessId = process.Id,
                            ProcessName = process.ProcessName,
                            MainWindowTitle = GetMainWindowTitle(process),
                            IsAttached = isAttached,
                            DotNetVersion = GetDotNetVersion(process)
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not inspect process {ProcessId}", process.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enumerating processes");
        }

        return Task.FromResult<IReadOnlyList<WpfProcessInfo>>(wpfProcesses);
    }

    public async Task<InspectionSession> AttachToProcessAsync(int? processId, string? processName)
    {
        Process? targetProcess = null;

        if (processId.HasValue)
        {
            try
            {
                targetProcess = Process.GetProcessById(processId.Value);
            }
            catch (ArgumentException)
            {
                throw new InvalidOperationException($"Process with ID {processId} not found");
            }
        }
        else if (!string.IsNullOrEmpty(processName))
        {
            var processes = Process.GetProcessesByName(processName.Replace(".exe", ""));
            targetProcess = processes.FirstOrDefault();

            if (targetProcess == null)
            {
                throw new InvalidOperationException($"Process with name '{processName}' not found");
            }
        }
        else
        {
            throw new ArgumentException("Either processId or processName must be provided");
        }

        // Verify it's a WPF process
        if (!IsLikelyWpfProcess(targetProcess))
        {
            _logger.LogWarning("Process {ProcessId} may not be a WPF application", targetProcess.Id);
        }

        // Create a new session
        var session = new InspectionSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            ProcessId = targetProcess.Id,
            MainWindowHandle = $"window_0x{targetProcess.MainWindowHandle:X}",
            AttachedAt = DateTime.UtcNow
        };

        lock (_lock)
        {
            _currentSession = session;
        }

        _logger.LogInformation("Attached to process {ProcessId} ({ProcessName})",
            targetProcess.Id, targetProcess.ProcessName);

        // Check if Inspector is already loaded (self-hosted mode)
        var inspectorLoaded = IsInspectorLoaded(targetProcess);
        if (inspectorLoaded)
        {
            _logger.LogInformation("Inspector DLL already loaded in target process (self-hosted mode)");
            session.InspectorStatus = "Loaded (self-hosted)";
        }
        else
        {
            _logger.LogWarning(
                "Inspector DLL not loaded in target process. " +
                "For external inspection, the target application must reference the Inspector DLL. " +
                "See documentation for self-hosted mode setup.");
            session.InspectorStatus = "Not loaded - use self-hosted mode";
        }

        return session;
    }

    private bool IsInspectorLoaded(Process process)
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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not check loaded modules for process {ProcessId}", process.Id);
        }

        return false;
    }

    public Task DetachAsync(string sessionId)
    {
        lock (_lock)
        {
            if (_currentSession?.SessionId == sessionId)
            {
                _logger.LogInformation("Detaching from process {ProcessId}", _currentSession.ProcessId);
                _currentSession = null;
            }
        }

        return Task.CompletedTask;
    }

    private bool IsLikelyWpfProcess(Process process)
    {
        try
        {
            // Check if the process has a main window (most WPF apps do)
            if (process.MainWindowHandle == IntPtr.Zero)
                return false;

            // Check loaded modules for WPF assemblies
            // This requires appropriate permissions
            try
            {
                foreach (ProcessModule module in process.Modules)
                {
                    var moduleName = module.ModuleName.ToLowerInvariant();
                    if (moduleName.Contains("presentationframework") ||
                        moduleName.Contains("presentationcore") ||
                        moduleName.Contains("wpfgfx"))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // Can't access modules, fall back to heuristics
                // For now, just assume any process with a main window could be WPF
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private string? GetMainWindowTitle(Process process)
    {
        try
        {
            return string.IsNullOrEmpty(process.MainWindowTitle) ? null : process.MainWindowTitle;
        }
        catch
        {
            return null;
        }
    }

    private string? GetDotNetVersion(Process process)
    {
        try
        {
            foreach (ProcessModule module in process.Modules)
            {
                var moduleName = module.ModuleName.ToLowerInvariant();
                if (moduleName == "clr.dll" || moduleName == "coreclr.dll" || moduleName == "mscorwks.dll")
                {
                    var version = module.FileVersionInfo.FileVersion;
                    return version;
                }
            }
        }
        catch
        {
            // Can't access modules
        }

        return null;
    }
}
