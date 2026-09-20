// Isolated protocol feasibility fixture, NOT a replacement FilesMate window.
// Hosts a genuine Windows Shell view in our own process; never loads SnowShot code.
#include <windows.h>
#include <exdisp.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <wrl.h>
#include <iostream>
using namespace Microsoft::WRL;

class Browser final : public RuntimeClass<RuntimeClassFlags<ClassicCom>,
    ChainInterfaces<IWebBrowserApp, IWebBrowser, IDispatch>,
    IServiceProvider, ChainInterfaces<IShellBrowser, IOleWindow>> {
public:
    HWND window{};
    PITEMID_CHILD selection{};
    ComPtr<IShellView> view;
    IFACEMETHODIMP QueryService(REFGUID service, REFIID iid, void** result) override {
        *result = nullptr;
        return service == SID_STopLevelBrowser ? QueryInterface(iid, result) : E_NOINTERFACE;
    }
    IFACEMETHODIMP GetWindow(HWND* output) override { *output = window; return S_OK; }
    IFACEMETHODIMP QueryActiveShellView(IShellView** output) override { return view.CopyTo(output); }
    IFACEMETHODIMP get_HWND(SHANDLE_PTR* output) override { *output = reinterpret_cast<SHANDLE_PTR>(window); return S_OK; }
    IFACEMETHODIMP GetTypeInfoCount(UINT* output) override { *output = 0; return S_OK; }
    IFACEMETHODIMP GetTypeInfo(UINT, LCID, ITypeInfo** output) override { *output = nullptr; return E_NOTIMPL; }
    IFACEMETHODIMP GetIDsOfNames(REFIID, LPOLESTR*, UINT, LCID, DISPID*) override { return DISP_E_UNKNOWNNAME; }
    IFACEMETHODIMP Invoke(DISPID, REFIID, LCID, WORD, DISPPARAMS*, VARIANT*, EXCEPINFO*, UINT*) override { return DISP_E_MEMBERNOTFOUND; }
#define STUB(name, args) IFACEMETHODIMP name args override { return E_NOTIMPL; }
    STUB(GoBack, ()) STUB(GoForward, ()) STUB(GoHome, ()) STUB(GoSearch, ())
    STUB(Navigate, (BSTR, VARIANT*, VARIANT*, VARIANT*, VARIANT*))
    STUB(Refresh, ()) STUB(Refresh2, (VARIANT*)) STUB(Stop, ())
    STUB(get_Application, (IDispatch**)) STUB(get_Parent, (IDispatch**))
    STUB(get_Container, (IDispatch**)) STUB(get_Document, (IDispatch**))
    STUB(get_TopLevelContainer, (VARIANT_BOOL*)) STUB(get_Type, (BSTR*))
    STUB(get_Left, (long*)) STUB(put_Left, (long)) STUB(get_Top, (long*)) STUB(put_Top, (long))
    STUB(get_Width, (long*)) STUB(put_Width, (long)) STUB(get_Height, (long*)) STUB(put_Height, (long))
    STUB(get_LocationName, (BSTR*)) STUB(get_LocationURL, (BSTR*)) STUB(get_Busy, (VARIANT_BOOL*))
    STUB(Quit, ()) STUB(ClientToWindow, (int*, int*)) STUB(PutProperty, (BSTR, VARIANT))
    STUB(GetProperty, (BSTR, VARIANT*)) STUB(get_Name, (BSTR*)) STUB(get_FullName, (BSTR*))
    STUB(get_Path, (BSTR*)) STUB(get_Visible, (VARIANT_BOOL*)) STUB(put_Visible, (VARIANT_BOOL))
    STUB(get_StatusBar, (VARIANT_BOOL*)) STUB(put_StatusBar, (VARIANT_BOOL))
    STUB(get_StatusText, (BSTR*)) STUB(put_StatusText, (BSTR))
    STUB(get_ToolBar, (int*)) STUB(put_ToolBar, (int))
    STUB(get_MenuBar, (VARIANT_BOOL*)) STUB(put_MenuBar, (VARIANT_BOOL))
    STUB(get_FullScreen, (VARIANT_BOOL*)) STUB(put_FullScreen, (VARIANT_BOOL))
    STUB(ContextSensitiveHelp, (BOOL)) STUB(InsertMenusSB, (HMENU, LPOLEMENUGROUPWIDTHS))
    STUB(SetMenuSB, (HMENU, HOLEMENU, HWND)) STUB(RemoveMenusSB, (HMENU))
    STUB(SetStatusTextSB, (LPCWSTR)) STUB(EnableModelessSB, (BOOL))
    STUB(TranslateAcceleratorSB, (MSG*, WORD)) STUB(BrowseObject, (PCUIDLIST_RELATIVE, UINT))
    STUB(GetViewStateStream, (DWORD, IStream**)) STUB(GetControlWindow, (UINT, HWND*))
    STUB(SendControlMsg, (UINT, UINT, WPARAM, LPARAM, LRESULT*))
    IFACEMETHODIMP OnViewWindowActive(IShellView*) override { return S_OK; }
    STUB(SetToolbarItems, (LPTBBUTTONSB, UINT, UINT))
#undef STUB
};

LRESULT CALLBACK procedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    if (message == WM_TIMER || message == WM_CLOSE) { PostQuitMessage(0); return 0; }
    return DefWindowProcW(window, message, wParam, lParam);
}

int run(const wchar_t* folderPath, const wchar_t* selectedName) {
    auto instance = GetModuleHandleW(nullptr);
    WNDCLASSW definition{};
    definition.hInstance = instance;
    definition.lpfnWndProc = procedure;
    definition.lpszClassName = L"CabinetWClass";
    if (!RegisterClassW(&definition)) return 2;
    // A normal native top-level window with an independently registered class.
    // Off-screen and shown without activation; never takes over the user's foreground.
    HWND window = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, definition.lpszClassName,
        L"FilesMate Shell compatibility fixture", WS_OVERLAPPEDWINDOW,
        -20000, -20000, 400, 300, nullptr, nullptr, instance, nullptr);
    if (!window) return 3;
    auto browser = Make<Browser>();
    browser->window = window;
    PIDLIST_ABSOLUTE folderId = ILCreateFromPathW(folderPath);
    ComPtr<IShellFolder> desktop, folder;
    HRESULT hr = SHGetDesktopFolder(&desktop);
    if (SUCCEEDED(hr) && folderId) hr = desktop->BindToObject(folderId, nullptr, IID_PPV_ARGS(&folder));
    else hr = E_INVALIDARG;
    if (SUCCEEDED(hr)) hr = folder->CreateViewObject(window, IID_PPV_ARGS(&browser->view));
    std::wcout << L"create_view=0x" << std::hex << static_cast<unsigned long>(hr) << std::endl;
    HWND viewWindow{};
    FOLDERSETTINGS settings{FVM_DETAILS, FWF_NOCLIENTEDGE};
    RECT bounds{0, 0, 400, 300};
    if (SUCCEEDED(hr)) hr = browser->view->CreateViewWindow(nullptr, &settings,
        static_cast<IShellBrowser*>(browser.Get()), &bounds, &viewWindow);
    std::wcout << L"create_view_window=0x" << std::hex << static_cast<unsigned long>(hr) << std::endl;
    ComPtr<IShellWindows> windows;
    long cookie{};
    bool registered{};
    if (SUCCEEDED(hr)) hr = CoCreateInstance(CLSID_ShellWindows, nullptr, CLSCTX_LOCAL_SERVER, IID_PPV_ARGS(&windows));
    if (SUCCEEDED(hr)) {
        hr = windows->Register(static_cast<IWebBrowserApp*>(browser.Get()),
            static_cast<long>(reinterpret_cast<LONG_PTR>(window)), SWC_BROWSER, &cookie);
        registered = SUCCEEDED(hr);
    }
    std::wcout << L"host_setup=0x" << std::hex << static_cast<unsigned long>(hr) << std::dec
               << L" hwnd=" << reinterpret_cast<uintptr_t>(window) << std::endl;
    if (SUCCEEDED(hr)) {
        ShowWindow(window, SW_SHOWNOACTIVATE);
        ShowWindow(viewWindow, SW_SHOWNOACTIVATE);
        browser->view->UIActivate(SVUIA_ACTIVATE_NOFOCUS);
        hr = folder->ParseDisplayName(window, nullptr, const_cast<wchar_t*>(selectedName), nullptr, &browser->selection, nullptr);
        SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(browser.Get()));
        // DefView enumerates asynchronously. Wait for its message loop before selecting.
        SetTimer(window, 2, 1000, [](HWND target, UINT, UINT_PTR timer, DWORD) {
            auto instance = reinterpret_cast<Browser*>(GetWindowLongPtrW(target, GWLP_USERDATA));
            auto selected = instance->view->SelectItem(instance->selection,
                SVSI_SELECT | SVSI_DESELECTOTHERS | SVSI_FOCUSED | SVSI_ENSUREVISIBLE);
            std::wcout << L"select=0x" << std::hex << static_cast<unsigned long>(selected) << std::dec << std::endl;
            KillTimer(target, timer);
        });
        SetTimer(window, 1, 20000, nullptr);
        MSG message{};
        while (GetMessageW(&message, nullptr, 0, 0) > 0) {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
    }
    if (registered) windows->Revoke(cookie);
    if (browser->view) browser->view->DestroyViewWindow();
    browser->view.Reset();
    CoTaskMemFree(browser->selection);
    DestroyWindow(window);
    CoTaskMemFree(folderId);
    UnregisterClassW(definition.lpszClassName, instance);
    return FAILED(hr) ? 4 : 0;
}

int wmain(int argc, wchar_t** argv) {
    if (argc != 3) return 1;
    if (FAILED(OleInitialize(nullptr))) return 1;
    int result = run(argv[1], argv[2]);
    OleUninitialize();
    return result;
}
