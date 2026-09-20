// Read-only diagnostics for the native Shell selection contract used by SnowShot.
// This does not modify SnowShot, register a browser, or change the foreground window.
#include <windows.h>
#include <exdisp.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <wrl/client.h>
#include <iostream>
#include <string>
using Microsoft::WRL::ComPtr;
std::wstring expectedPath;
bool expectedPathFound{};

std::wstring windowClass(HWND window) {
    wchar_t name[256]{};
    GetClassNameW(window, name, 256);
    return name;
}

HWND visibleChild(HWND parent, const wchar_t* name) {
    struct Search { const wchar_t* name; HWND found{}; } search{name};
    EnumChildWindows(parent, [](HWND child, LPARAM parameter) -> BOOL {
        auto& item = *reinterpret_cast<Search*>(parameter);
        if (IsWindowVisible(child) && windowClass(child) == item.name) {
            item.found = child;
            return FALSE;
        }
        return TRUE;
    }, reinterpret_cast<LPARAM>(&search));
    return search.found;
}

bool check(const wchar_t* stage, HRESULT result) {
    std::wcout << L"  " << stage << L"=0x" << std::hex
               << static_cast<unsigned long>(result) << std::dec << L'\n';
    return SUCCEEDED(result);
}

void inspect(HWND window, IShellWindows* windows) {
    auto name = windowClass(window);
    HWND capturedView = visibleChild(window, L"SHELLDLL_DefView");
    HWND capturedTab = visibleChild(window, L"ShellTabWindowClass");
    DWORD process{};
    GetWindowThreadProcessId(window, &process);
    bool supportedClass = name == L"CabinetWClass" || name == L"ExploreWClass";
    std::wcout << L"window=" << reinterpret_cast<uintptr_t>(window)
               << L" pid=" << process << L" class=" << name << L'\n'
               << L"  snowshot_capture=" << (supportedClass && capturedView ? L"PASS" : L"REJECT")
               << L" visible_shell_view=" << reinterpret_cast<uintptr_t>(capturedView) << L'\n';
    // Continue past the class gate only to diagnose missing COM layers separately.
    long count{};
    if (!check(L"ShellWindows.Count", windows->get_Count(&count))) return;
    int matches{};
    for (long index = 0; index < count; ++index) {
        VARIANT position{}; position.vt = VT_I4; position.lVal = index;
        ComPtr<IDispatch> dispatch;
        ComPtr<IWebBrowserApp> app;
        SHANDLE_PTR handle{};
        if (FAILED(windows->Item(position, &dispatch)) || !dispatch ||
            FAILED(dispatch.As(&app)) || FAILED(app->get_HWND(&handle)) ||
            reinterpret_cast<HWND>(handle) != window) continue;
        ++matches;
        ComPtr<IServiceProvider> provider;
        ComPtr<IShellBrowser> browser;
        ComPtr<IShellView> view;
        if (!check(L"IWebBrowserApp.IServiceProvider", dispatch.As(&provider)) ||
            !check(L"SID_STopLevelBrowser.IShellBrowser", provider->QueryService(SID_STopLevelBrowser, IID_PPV_ARGS(&browser))) ||
            !check(L"QueryActiveShellView", browser->QueryActiveShellView(&view))) continue;
        HWND actualView{};
        if (!check(L"IShellView.GetWindow", view->GetWindow(&actualView))) continue;
        bool sameView = actualView == capturedView && IsWindowVisible(actualView) &&
            (!capturedTab || IsChild(capturedTab, actualView));
        std::wcout << L"  captured_view_matches=" << (sameView ? L"PASS" : L"REJECT") << L'\n';
        ComPtr<IFolderView2> folder;
        ComPtr<IShellItemArray> items;
        if (!check(L"IFolderView2", view.As(&folder))) continue;
        int allCount{}; folder->ItemCount(SVGIO_ALLVIEW, &allCount);
        std::wcout << L"  view_item_count=" << allCount << L'\n';
        if (!check(L"Items.SVGIO_SELECTION", folder->Items(SVGIO_SELECTION, IID_PPV_ARGS(&items)))) continue;
        DWORD itemCount{}, pathCount{};
        if (!check(L"IShellItemArray.GetCount", items->GetCount(&itemCount))) continue;
        for (DWORD itemIndex = 0; itemIndex < itemCount; ++itemIndex) {
            ComPtr<IShellItem> item;
            PWSTR path{};
            if (SUCCEEDED(items->GetItemAt(itemIndex, &item)) &&
                SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)) && path && *path) {
                ++pathCount;
                if (supportedClass && sameView && !expectedPath.empty() &&
                    _wcsicmp(path, expectedPath.c_str()) == 0) expectedPathFound = true;
            }
            CoTaskMemFree(path);
        }
        std::wcout << L"  selected_items=" << itemCount << L" filesystem_paths=" << pathCount << L'\n';
    }
    std::wcout << L"  registered_browser_matches=" << matches << L'\n';
}

int wmain(int argc, wchar_t** argv) {
    if (argc == 3 && std::wstring(argv[1]) == L"--expected") expectedPath = argv[2];
    HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(hr)) return 1;
    {
        ComPtr<IShellWindows> windows;
        if (!check(L"CoCreate.ShellWindows", CoCreateInstance(CLSID_ShellWindows, nullptr,
                CLSCTX_LOCAL_SERVER, IID_PPV_ARGS(&windows)))) { CoUninitialize(); return 2; }
        if (argc == 2) {
            inspect(reinterpret_cast<HWND>(_wcstoui64(argv[1], nullptr, 0)), windows.Get());
        } else {
            // Enumerate only FilesMate and native Explorer windows. No titles or paths are logged.
            EnumWindows([](HWND window, LPARAM parameter) -> BOOL {
                if (!IsWindowVisible(window)) return TRUE;
                DWORD process{}; GetWindowThreadProcessId(window, &process);
                HANDLE handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, process);
                wchar_t path[32768]{}; DWORD length = 32768;
                bool filesMate = handle && QueryFullProcessImageNameW(handle, 0, path, &length) &&
                    (std::wstring(path).ends_with(L"\\FilesMate.App.exe") ||
                     std::wstring(path).ends_with(L"\\FilesMate.SearchHost.exe"));
                if (handle) CloseHandle(handle);
                auto name = windowClass(window);
                if (filesMate || name == L"CabinetWClass" || name == L"ExploreWClass")
                    inspect(window, reinterpret_cast<IShellWindows*>(parameter));
                return TRUE;
            }, reinterpret_cast<LPARAM>(windows.Get()));
        }
    }
    CoUninitialize();
    if (!expectedPath.empty()) {
        std::wcout << L"expected_selection=" << (expectedPathFound ? L"PASS" : L"FAIL") << L'\n';
        return expectedPathFound ? 0 : 3;
    }
}
