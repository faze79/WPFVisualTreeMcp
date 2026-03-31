// WpfInspectorBootstrapper.cpp
// Native DLL that bootstraps the managed WPF Inspector when injected into a process.
//
// This DLL is loaded via CreateRemoteThread + LoadLibrary. When loaded, it:
// 1. Gets the CLR runtime already running in the WPF process
// 2. Loads the managed WpfVisualTreeMcp.Inspector.dll
// 3. Calls InspectorService.Initialize(processId)

#include <windows.h>
#include <metahost.h>
#include <string>
#include <stdio.h>

#pragma comment(lib, "mscoree.lib")

// Forward declarations
HRESULT InitializeInspector();
void WriteDebugLog(const wchar_t* message);

// Global variables
HMODULE g_hModule = NULL;
wchar_t g_modulePath[MAX_PATH] = { 0 };

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
            HRESULT hr = InitializeInspector();
            if (FAILED(hr))
            {
                wchar_t msg[256];
                swprintf_s(msg, L"InitializeInspector failed with HRESULT: 0x%08X", hr);
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

void WriteDebugLog(const wchar_t* message)
{
    // Write to a debug log file in the temp directory
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

std::wstring GetInspectorDllPath()
{
    // Inspector DLL should be in the same directory as this bootstrapper
    std::wstring path(g_modulePath);
    size_t pos = path.find_last_of(L"\\/");
    if (pos != std::wstring::npos)
    {
        path = path.substr(0, pos + 1);
    }
    path += L"WpfVisualTreeMcp.Inspector.dll";
    return path;
}

HRESULT InitializeInspector()
{
    WriteDebugLog(L"InitializeInspector starting...");

    HRESULT hr = S_OK;
    ICLRMetaHost* pMetaHost = NULL;
    ICLRRuntimeInfo* pRuntimeInfo = NULL;
    ICLRRuntimeHost* pClrRuntimeHost = NULL;

    // Get the CLR metahost
    hr = CLRCreateInstance(CLSID_CLRMetaHost, IID_ICLRMetaHost, (LPVOID*)&pMetaHost);
    if (FAILED(hr))
    {
        WriteDebugLog(L"CLRCreateInstance failed");
        return hr;
    }

    // Get the runtime that's already loaded in this process
    // For .NET Framework 4.x WPF apps, this will be v4.0.30319
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
            swprintf_s(msg, L"Found runtime: %s", version);
            WriteDebugLog(msg);
            break; // Use the first loaded runtime
        }
    }
    pEnumerator->Release();

    if (pRuntimeInfo == NULL)
    {
        WriteDebugLog(L"No .NET runtime found in process");
        pMetaHost->Release();
        return E_FAIL;
    }

    // Get the runtime host interface
    hr = pRuntimeInfo->GetInterface(CLSID_CLRRuntimeHost, IID_ICLRRuntimeHost, (LPVOID*)&pClrRuntimeHost);
    if (FAILED(hr))
    {
        WriteDebugLog(L"GetInterface for CLRRuntimeHost failed");
        pRuntimeInfo->Release();
        pMetaHost->Release();
        return hr;
    }

    // Get the Inspector DLL path
    std::wstring inspectorPath = GetInspectorDllPath();
    wchar_t msg[512];
    swprintf_s(msg, L"Loading Inspector from: %s", inspectorPath.c_str());
    WriteDebugLog(msg);

    // Check if file exists
    DWORD attrs = GetFileAttributesW(inspectorPath.c_str());
    if (attrs == INVALID_FILE_ATTRIBUTES)
    {
        WriteDebugLog(L"Inspector DLL not found!");
        pClrRuntimeHost->Release();
        pRuntimeInfo->Release();
        pMetaHost->Release();
        return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
    }

    // Get current process ID
    DWORD processId = GetCurrentProcessId();
    swprintf_s(msg, L"Current process ID: %d", processId);
    WriteDebugLog(msg);

    // Execute managed code: InspectorService.Initialize(processId)
    // Parameters: assembly path, type name, method name, argument
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

    // Cleanup
    pClrRuntimeHost->Release();
    pRuntimeInfo->Release();
    pMetaHost->Release();

    return hr;
}

// Export for external initialization (optional)
extern "C" __declspec(dllexport) HRESULT __stdcall Bootstrap()
{
    return InitializeInspector();
}
