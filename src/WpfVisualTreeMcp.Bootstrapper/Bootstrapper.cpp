// WpfVisualTreeMcp.Bootstrapper
// C++/CLI bootstrapper for injecting the Inspector into WPF applications
// This DLL is injected into the target process and loads the managed Inspector assembly

#include <windows.h>
#include <msclr/marshal_cppstd.h>

using namespace System;
using namespace System::IO;
using namespace System::Reflection;
using namespace System::Threading;
using namespace System::Diagnostics;

// Global state
static bool g_initialized = false;
static String^ g_inspectorPath = nullptr;

// Forward declarations
void InitializeInspector();
void LogMessage(String^ message);

// Export function that can be called after injection
extern "C" __declspec(dllexport) void __stdcall Initialize()
{
    if (g_initialized)
        return;

    g_initialized = true;

    // Run initialization on a new thread to avoid blocking
    Thread^ initThread = gcnew Thread(gcnew ThreadStart(&InitializeInspector));
    initThread->SetApartmentState(ApartmentState::STA);
    initThread->Start();
}

// Export function to check if inspector is loaded
extern "C" __declspec(dllexport) bool __stdcall IsLoaded()
{
    return g_initialized;
}

void InitializeInspector()
{
    try
    {
        LogMessage("Bootstrapper: Starting initialization...");

        // Get the path to the Inspector DLL (same directory as bootstrapper)
        String^ bootstrapperPath = Assembly::GetExecutingAssembly()->Location;
        String^ directory = Path::GetDirectoryName(bootstrapperPath);
        g_inspectorPath = Path::Combine(directory, "WpfVisualTreeMcp.Inspector.dll");

        LogMessage("Bootstrapper: Inspector path = " + g_inspectorPath);

        if (!File::Exists(g_inspectorPath))
        {
            LogMessage("Bootstrapper: ERROR - Inspector DLL not found at: " + g_inspectorPath);
            return;
        }

        // Load the Inspector assembly
        LogMessage("Bootstrapper: Loading Inspector assembly...");
        Assembly^ inspectorAssembly = Assembly::LoadFrom(g_inspectorPath);

        if (inspectorAssembly == nullptr)
        {
            LogMessage("Bootstrapper: ERROR - Failed to load Inspector assembly");
            return;
        }

        LogMessage("Bootstrapper: Inspector assembly loaded successfully");

        // Get the InspectorService type
        Type^ inspectorType = inspectorAssembly->GetType("WpfVisualTreeMcp.Inspector.InspectorService");
        if (inspectorType == nullptr)
        {
            LogMessage("Bootstrapper: ERROR - InspectorService type not found");
            return;
        }

        // Get the Initialize method
        MethodInfo^ initMethod = inspectorType->GetMethod("Initialize",
            BindingFlags::Public | BindingFlags::Static,
            nullptr,
            gcnew array<Type^>{ int::typeid },
            nullptr);

        if (initMethod == nullptr)
        {
            LogMessage("Bootstrapper: ERROR - Initialize method not found");
            return;
        }

        // Call Initialize with the current process ID
        int processId = Process::GetCurrentProcess()->Id;
        LogMessage("Bootstrapper: Calling InspectorService.Initialize(" + processId + ")...");

        // Need to dispatch to the WPF dispatcher thread
        // Wait for Application.Current to be available
        int retries = 0;
        while (System::Windows::Application::Current == nullptr && retries < 50)
        {
            Thread::Sleep(100);
            retries++;
        }

        if (System::Windows::Application::Current == nullptr)
        {
            LogMessage("Bootstrapper: WARNING - Application.Current is null, attempting direct initialization");
            initMethod->Invoke(nullptr, gcnew array<Object^>{ processId });
        }
        else
        {
            // Dispatch to UI thread
            System::Windows::Application::Current->Dispatcher->Invoke(
                gcnew Action<int>(
                    [initMethod](int pid) {
                        initMethod->Invoke(nullptr, gcnew array<Object^>{ pid });
                    }
                ),
                processId
            );
        }

        LogMessage("Bootstrapper: Initialization complete!");
    }
    catch (Exception^ ex)
    {
        LogMessage("Bootstrapper: EXCEPTION - " + ex->ToString());
    }
}

void LogMessage(String^ message)
{
    try
    {
        String^ logPath = Path::Combine(Path::GetTempPath(), "WpfInspector_Bootstrapper.log");
        String^ timestamp = DateTime::Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        File::AppendAllText(logPath, "[" + timestamp + "] " + message + Environment::NewLine);
    }
    catch (...)
    {
        // Ignore logging errors
    }
}

// DllMain - called when the DLL is loaded
BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        // Don't initialize here - wait for explicit Initialize() call
        // This avoids loader lock issues
        DisableThreadLibraryCalls(hModule);
        break;
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}
