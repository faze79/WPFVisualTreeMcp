// WpfInspectorBootstrapper.cpp
// Native DLL that bootstraps the managed WPF Inspector when injected into a process.
//
// This DLL is loaded via CreateRemoteThread + LoadLibrary. When loaded, it:
// 1. Detects the .NET runtime type (Framework CLR vs CoreCLR)
// 2. For .NET Framework: uses CLRCreateInstance + ExecuteInDefaultAppDomain
// 3. For .NET 5+/8+: uses hostfxr + load_assembly_and_get_function_pointer
// 4. Calls InspectorService.Initialize(processId)

#include <windows.h>
#include <metahost.h>
#include <string>
#include <stdio.h>

#pragma comment(lib, "mscoree.lib")

// ============================================================================
// hostfxr types (declared manually to avoid nethost.h SDK dependency)
// These are stable ABI types from the .NET hosting API.
// ============================================================================

enum hostfxr_delegate_type
{
    hdt_com_activation = 0,
    hdt_load_in_memory_assembly = 1,
    hdt_winrt_activation = 2,
    hdt_com_register = 3,
    hdt_com_unregister = 4,
    hdt_load_assembly_and_get_function_pointer = 5,
    hdt_get_function_pointer = 6,
};

typedef int32_t(__cdecl* hostfxr_initialize_for_runtime_config_fn)(
    const wchar_t* runtime_config_path,
    const void* parameters,
    void** host_context_handle);

typedef int32_t(__cdecl* hostfxr_get_runtime_delegate_fn)(
    const void* host_context_handle,
    hostfxr_delegate_type type,
    void** delegate);

typedef int32_t(__cdecl* hostfxr_close_fn)(
    const void* host_context_handle);

// component_entry_point_fn: managed delegate signature for load_assembly_and_get_function_pointer
typedef int (__cdecl* component_entry_point_fn)(void* arg, int32_t arg_size_in_bytes);

// load_assembly_and_get_function_pointer delegate type
typedef int32_t(__cdecl* load_assembly_and_get_function_pointer_fn)(
    const wchar_t* assembly_path,
    const wchar_t* type_name,
    const wchar_t* method_name,
    const wchar_t* delegate_type_name,
    void* reserved,
    void** delegate);

// ============================================================================
// Forward declarations
// ============================================================================

HRESULT InitializeInspectorFramework();
HRESULT InitializeInspectorCoreCLR();
void WriteDebugLog(const wchar_t* message);

// Global variables
HMODULE g_hModule = NULL;
wchar_t g_modulePath[MAX_PATH] = { 0 };

// ============================================================================
// DLL entry point
// ============================================================================

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        g_hModule = hModule;
        GetModuleFileNameW(hModule, g_modulePath, MAX_PATH);
        WriteDebugLog(L"Bootstrapper DLL attached");

        // Initialize the Inspector when the DLL is loaded
        // Use a separate thread to avoid deadlocks with the loader lock
        CreateThread(NULL, 0, [](LPVOID) -> DWORD {
            Sleep(100); // Brief delay to ensure process is stable

            HRESULT hr;
            if (GetModuleHandleW(L"coreclr.dll") != NULL)
            {
                WriteDebugLog(L"CoreCLR detected, using hostfxr path");
                hr = InitializeInspectorCoreCLR();
            }
            else
            {
                WriteDebugLog(L".NET Framework CLR detected, using COM hosting path");
                hr = InitializeInspectorFramework();
            }

            if (FAILED(hr))
            {
                wchar_t msg[256];
                swprintf_s(msg, L"Inspector initialization failed with HRESULT: 0x%08X", hr);
                WriteDebugLog(msg);
            }
            return 0;
        }, NULL, 0, NULL);
        break;

    case DLL_PROCESS_DETACH:
        WriteDebugLog(L"Bootstrapper DLL detached");
        break;
    }
    return TRUE;
}

// ============================================================================
// Logging
// ============================================================================

void WriteDebugLog(const wchar_t* message)
{
    wchar_t logPath[MAX_PATH];
    GetTempPathW(MAX_PATH, logPath);
    wcscat_s(logPath, L"WpfInspectorBootstrapper.log");

    FILE* fp = nullptr;
    if (_wfopen_s(&fp, logPath, L"a") == 0 && fp)
    {
        SYSTEMTIME st;
        GetLocalTime(&st);
        fwprintf(fp, L"[%04d-%02d-%02d %02d:%02d:%02d.%03d] %s\n",
            st.wYear, st.wMonth, st.wDay,
            st.wHour, st.wMinute, st.wSecond, st.wMilliseconds,
            message);
        fclose(fp);
    }
}

// ============================================================================
// Path resolution helpers
// ============================================================================

std::wstring GetBootstrapperDir()
{
    std::wstring dir(g_modulePath);
    size_t pos = dir.find_last_of(L"\\/");
    if (pos != std::wstring::npos)
        dir = dir.substr(0, pos + 1);
    return dir;
}

/// Finds the .NET Framework Inspector DLL (net48)
std::wstring GetInspectorDllPath()
{
    std::wstring dir = GetBootstrapperDir();

    // 1. Same directory (co-located layout)
    std::wstring sameDirPath = dir + L"WpfVisualTreeMcp.Inspector.dll";
    if (GetFileAttributesW(sameDirPath.c_str()) != INVALID_FILE_ATTRIBUTES)
    {
        WriteDebugLog(L"Found net48 Inspector in same directory");
        return sameDirPath;
    }

    // 2. Parent's parent (publish layout: native/x64/ -> publish root)
    std::wstring parentPath = dir + L"..\\..\\WpfVisualTreeMcp.Inspector.dll";
    if (GetFileAttributesW(parentPath.c_str()) != INVALID_FILE_ATTRIBUTES)
    {
        WriteDebugLog(L"Found net48 Inspector in publish root (../../)");
        return parentPath;
    }

    WriteDebugLog(L"net48 Inspector DLL not found");
    return sameDirPath;
}

/// Finds the CoreCLR Inspector DLL (net8.0-windows) in coreclr/ subdirectory
std::wstring GetInspectorDllPath_CoreCLR()
{
    std::wstring dir = GetBootstrapperDir();

    // 1. coreclr/ subdirectory next to bootstrapper (publish layout)
    std::wstring coreclrPath = dir + L"coreclr\\WpfVisualTreeMcp.Inspector.dll";
    if (GetFileAttributesW(coreclrPath.c_str()) != INVALID_FILE_ATTRIBUTES)
    {
        WriteDebugLog(L"Found CoreCLR Inspector in coreclr/ subdirectory");
        return coreclrPath;
    }

    // 2. Same directory fallback (flat layout)
    std::wstring sameDirPath = dir + L"WpfVisualTreeMcp.Inspector.dll";
    if (GetFileAttributesW(sameDirPath.c_str()) != INVALID_FILE_ATTRIBUTES)
    {
        WriteDebugLog(L"Found Inspector in same directory (flat layout)");
        return sameDirPath;
    }

    WriteDebugLog(L"CoreCLR Inspector DLL not found");
    return coreclrPath;
}

/// Derives the runtimeconfig.json path from the Inspector DLL path
std::wstring GetRuntimeConfigPath(const std::wstring& inspectorPath)
{
    // Look for .coreclr.runtimeconfig.json next to the Inspector DLL
    std::wstring dir = inspectorPath;
    size_t pos = dir.find_last_of(L"\\/");
    if (pos != std::wstring::npos)
        dir = dir.substr(0, pos + 1);

    std::wstring configPath = dir + L"WpfVisualTreeMcp.Inspector.coreclr.runtimeconfig.json";
    if (GetFileAttributesW(configPath.c_str()) != INVALID_FILE_ATTRIBUTES)
        return configPath;

    // Fallback: standard naming
    configPath = dir + L"WpfVisualTreeMcp.Inspector.runtimeconfig.json";
    return configPath;
}

// ============================================================================
// .NET Framework initialization (existing approach)
// ============================================================================

HRESULT InitializeInspectorFramework()
{
    WriteDebugLog(L"InitializeInspectorFramework starting...");

    HRESULT hr = S_OK;
    ICLRMetaHost* pMetaHost = NULL;
    ICLRRuntimeInfo* pRuntimeInfo = NULL;
    ICLRRuntimeHost* pClrRuntimeHost = NULL;

    hr = CLRCreateInstance(CLSID_CLRMetaHost, IID_ICLRMetaHost, (LPVOID*)&pMetaHost);
    if (FAILED(hr))
    {
        WriteDebugLog(L"CLRCreateInstance failed");
        return hr;
    }

    IEnumUnknown* pEnumerator = NULL;
    hr = pMetaHost->EnumerateLoadedRuntimes(GetCurrentProcess(), &pEnumerator);
    if (FAILED(hr))
    {
        WriteDebugLog(L"EnumerateLoadedRuntimes failed");
        pMetaHost->Release();
        return hr;
    }

    IUnknown* pUnknown = NULL;
    ULONG fetched = 0;
    while (pEnumerator->Next(1, &pUnknown, &fetched) == S_OK)
    {
        hr = pUnknown->QueryInterface(IID_ICLRRuntimeInfo, (LPVOID*)&pRuntimeInfo);
        pUnknown->Release();
        if (SUCCEEDED(hr))
        {
            wchar_t version[64];
            DWORD versionSize = 64;
            pRuntimeInfo->GetVersionString(version, &versionSize);

            wchar_t msg[256];
            swprintf_s(msg, L"Found .NET Framework runtime: %s", version);
            WriteDebugLog(msg);
            break;
        }
    }
    pEnumerator->Release();

    if (pRuntimeInfo == NULL)
    {
        WriteDebugLog(L"No .NET Framework runtime found");
        pMetaHost->Release();
        return E_FAIL;
    }

    hr = pRuntimeInfo->GetInterface(CLSID_CLRRuntimeHost, IID_ICLRRuntimeHost, (LPVOID*)&pClrRuntimeHost);
    if (FAILED(hr))
    {
        WriteDebugLog(L"GetInterface for CLRRuntimeHost failed");
        pRuntimeInfo->Release();
        pMetaHost->Release();
        return hr;
    }

    std::wstring inspectorPath = GetInspectorDllPath();
    wchar_t msg[512];
    swprintf_s(msg, L"Loading net48 Inspector from: %s", inspectorPath.c_str());
    WriteDebugLog(msg);

    DWORD attrs = GetFileAttributesW(inspectorPath.c_str());
    if (attrs == INVALID_FILE_ATTRIBUTES)
    {
        WriteDebugLog(L"Inspector DLL not found!");
        pClrRuntimeHost->Release();
        pRuntimeInfo->Release();
        pMetaHost->Release();
        return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
    }

    DWORD processId = GetCurrentProcessId();
    swprintf_s(msg, L"Current process ID: %d", processId);
    WriteDebugLog(msg);

    wchar_t argStr[32];
    swprintf_s(argStr, L"%d", processId);

    DWORD returnValue = 0;
    hr = pClrRuntimeHost->ExecuteInDefaultAppDomain(
        inspectorPath.c_str(),
        L"WpfVisualTreeMcp.Inspector.InspectorService",
        L"Initialize",
        argStr,
        &returnValue);

    if (FAILED(hr))
    {
        swprintf_s(msg, L"ExecuteInDefaultAppDomain failed: 0x%08X", hr);
        WriteDebugLog(msg);
    }
    else
    {
        swprintf_s(msg, L"Inspector initialized successfully! Return value: %d", returnValue);
        WriteDebugLog(msg);
    }

    pClrRuntimeHost->Release();
    pRuntimeInfo->Release();
    pMetaHost->Release();

    return hr;
}

// ============================================================================
// CoreCLR (.NET 5+/8+) initialization via hostfxr
// ============================================================================

HRESULT InitializeInspectorCoreCLR()
{
    WriteDebugLog(L"InitializeInspectorCoreCLR starting...");

    // 1. Find hostfxr.dll (already loaded in any .NET 5+ process)
    HMODULE hHostfxr = GetModuleHandleW(L"hostfxr.dll");
    if (!hHostfxr)
    {
        WriteDebugLog(L"hostfxr.dll not found in process");
        return E_FAIL;
    }
    WriteDebugLog(L"Found hostfxr.dll");

    // 2. Resolve hostfxr function pointers
    auto pfnInitialize = (hostfxr_initialize_for_runtime_config_fn)
        GetProcAddress(hHostfxr, "hostfxr_initialize_for_runtime_config");
    auto pfnGetDelegate = (hostfxr_get_runtime_delegate_fn)
        GetProcAddress(hHostfxr, "hostfxr_get_runtime_delegate");
    auto pfnClose = (hostfxr_close_fn)
        GetProcAddress(hHostfxr, "hostfxr_close");

    if (!pfnInitialize || !pfnGetDelegate || !pfnClose)
    {
        WriteDebugLog(L"Failed to resolve hostfxr functions");
        return E_FAIL;
    }
    WriteDebugLog(L"Resolved hostfxr functions");

    // 3. Find the CoreCLR Inspector DLL and runtimeconfig
    std::wstring inspectorPath = GetInspectorDllPath_CoreCLR();
    std::wstring runtimeConfigPath = GetRuntimeConfigPath(inspectorPath);

    wchar_t msg[512];
    swprintf_s(msg, L"CoreCLR Inspector path: %s", inspectorPath.c_str());
    WriteDebugLog(msg);
    swprintf_s(msg, L"RuntimeConfig path: %s", runtimeConfigPath.c_str());
    WriteDebugLog(msg);

    if (GetFileAttributesW(inspectorPath.c_str()) == INVALID_FILE_ATTRIBUTES)
    {
        WriteDebugLog(L"CoreCLR Inspector DLL not found!");
        return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
    }

    if (GetFileAttributesW(runtimeConfigPath.c_str()) == INVALID_FILE_ATTRIBUTES)
    {
        WriteDebugLog(L"RuntimeConfig JSON not found!");
        return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
    }

    // 4. Initialize hostfxr context
    //    Returns 0 (Success) or 1 (Success_HostAlreadyInitialized) - both are OK
    void* hostContextHandle = nullptr;
    int32_t rc = pfnInitialize(runtimeConfigPath.c_str(), nullptr, &hostContextHandle);

    swprintf_s(msg, L"hostfxr_initialize_for_runtime_config returned: 0x%08X", rc);
    WriteDebugLog(msg);

    if (rc != 0 && rc != 1 && rc != 2)
    {
        // 0 = Success, 1 = Success_HostAlreadyInitialized, 2 = Success_DifferentRuntimeProperties
        WriteDebugLog(L"hostfxr initialization failed");
        if (hostContextHandle) pfnClose(hostContextHandle);
        return E_FAIL;
    }

    // 5. Get load_assembly_and_get_function_pointer delegate
    load_assembly_and_get_function_pointer_fn loadAssembly = nullptr;
    rc = pfnGetDelegate(hostContextHandle,
        hdt_load_assembly_and_get_function_pointer, (void**)&loadAssembly);

    if (rc != 0 || !loadAssembly)
    {
        swprintf_s(msg, L"hostfxr_get_runtime_delegate failed: 0x%08X", rc);
        WriteDebugLog(msg);
        pfnClose(hostContextHandle);
        return E_FAIL;
    }
    WriteDebugLog(L"Got load_assembly_and_get_function_pointer delegate");

    // 6. Load the net8.0 Inspector assembly and get InitializeUnmanaged
    component_entry_point_fn initFn = nullptr;
    rc = loadAssembly(
        inspectorPath.c_str(),
        L"WpfVisualTreeMcp.Inspector.InspectorService, WpfVisualTreeMcp.Inspector",
        L"InitializeUnmanaged",
        nullptr,    // NULL = component_entry_point_fn signature
        nullptr,
        (void**)&initFn);

    if (rc != 0 || !initFn)
    {
        swprintf_s(msg, L"load_assembly_and_get_function_pointer failed: 0x%08X", rc);
        WriteDebugLog(msg);
        pfnClose(hostContextHandle);
        return E_FAIL;
    }
    WriteDebugLog(L"Loaded Inspector assembly and resolved InitializeUnmanaged");

    // 7. Call managed entry point, passing process ID as 4-byte int
    DWORD processId = GetCurrentProcessId();
    swprintf_s(msg, L"Calling InitializeUnmanaged with PID=%d", processId);
    WriteDebugLog(msg);

    int result = initFn(&processId, sizeof(DWORD));

    swprintf_s(msg, L"InitializeUnmanaged returned: %d", result);
    WriteDebugLog(msg);

    pfnClose(hostContextHandle);
    return (result == 0) ? S_OK : E_FAIL;
}

// Export for external initialization (optional)
extern "C" __declspec(dllexport) HRESULT __stdcall Bootstrap()
{
    if (GetModuleHandleW(L"coreclr.dll") != NULL)
        return InitializeInspectorCoreCLR();
    else
        return InitializeInspectorFramework();
}
