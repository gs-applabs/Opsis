#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include <atomic>
#include <cwchar>
#include <new>
#include <strsafe.h>

namespace
{
    constexpr GUID classicId{0x7F45B246,0x0BA9,0x4C86,{0xAB,0x71,0x09,0xF9,0x69,0x61,0x0F,0x62}};
    constexpr GUID modernId{0xCA419189,0x286E,0x49FF,{0x9D,0x90,0x2F,0x46,0x24,0x27,0xB5,0xDE}};
    constexpr wchar_t settingsKey[] = L"Software\\Classes\\DesktopBackground\\shellex\\ContextMenuHandlers\\Opsis";
    enum Command { NewContainer, Previous, Next, Pause, Stop, Header, Root };

    struct CommandText { const wchar_t* verb; const wchar_t* en; const wchar_t* el; };
    constexpr CommandText labels[] = {
        {L"new-container", L"New DeskContainer", L"Νέο DeskContainer"},
        {L"prev-wallpaper", L"Previous", L"Προηγούμενο"},
        {L"next-wallpaper", L"Next", L"Επόμενο"},
        {L"toggle-wallpaper", L"Pause / resume", L"Παύση / συνέχιση"},
        {L"stop-wallpaper", L"Stop", L"Διακοπή"},
        {nullptr, L"Animated wallpaper", L"Κινούμενο wallpaper"},
        {nullptr, L"Opsis", L"Opsis"}
    };
    std::atomic<long> objects{0};
    struct Lifetime
    {
        Lifetime() noexcept { ++objects; }
        ~Lifetime() { --objects; }
    };

    bool Greek()
    {
        DWORD value = 0, size = sizeof(value);
        return RegGetValueW(HKEY_CURRENT_USER, settingsKey, L"Greek", RRF_RT_REG_DWORD,
            nullptr, &value, &size) == ERROR_SUCCESS ? value != 0 : PRIMARYLANGID(GetUserDefaultUILanguage()) == LANG_GREEK;
    }

    const wchar_t* Caption(Command command) { return Greek() ? labels[command].el : labels[command].en; }

    bool GetExecutable(wchar_t (&path)[32768])
    {
        DWORD bytes = sizeof(path);
        if (RegGetValueW(HKEY_CURRENT_USER, settingsKey, L"Executable", RRF_RT_REG_SZ | RRF_NOEXPAND,
                nullptr, path, &bytes) != ERROR_SUCCESS) return false;
        auto name = wcsrchr(path, L'\\');
        return name && !wcschr(path, L'"') && !PathIsRelativeW(path) &&
            CompareStringOrdinal(name + 1, -1, L"Opsis.exe", -1, TRUE) == CSTR_EQUAL;
    }

    HRESULT InvokeAction(Command command)
    {
        if (command < NewContainer || command > Stop) return E_INVALIDARG;
        wchar_t executable[32768]{};
        if (!GetExecutable(executable)) return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
        wchar_t line[32768]{};
        auto hr = StringCchPrintfW(line, ARRAYSIZE(line), L"\"%s\" --desktop-action %s", executable, labels[command].verb);
        if (FAILED(hr)) return hr;
        STARTUPINFOW startup{};
        startup.cb = sizeof(startup);
        PROCESS_INFORMATION process{};
        if (!CreateProcessW(executable, line, nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &startup, &process))
            return HRESULT_FROM_WIN32(GetLastError());
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        return S_OK;
    }

    class ExplorerCommand;
    class CommandEnumerator final : public IEnumExplorerCommand, private Lifetime
    {
        std::atomic<ULONG> refs{1};
        ULONG position = 0;
    public:
        explicit CommandEnumerator(ULONG current = 0) noexcept : position(current) {}
        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override
        {
            if (!result) return E_POINTER;
            *result = nullptr;
            if (iid != IID_IUnknown && iid != __uuidof(IEnumExplorerCommand)) return E_NOINTERFACE;
            *result = static_cast<IEnumExplorerCommand*>(this);
            AddRef();
            return S_OK;
        }
        ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
        ULONG STDMETHODCALLTYPE Release() override { const auto count = --refs; if (!count) delete this; return count; }
        HRESULT STDMETHODCALLTYPE Next(ULONG count, IExplorerCommand** found, ULONG* fetched) override;
        HRESULT STDMETHODCALLTYPE Skip(ULONG count) override
        {
            constexpr ULONG capacity = 6;
            const auto available = capacity - position;
            position += count < available ? count : available;
            return count <= available ? S_OK : S_FALSE;
        }
        HRESULT STDMETHODCALLTYPE Reset() override { position = 0; return S_OK; }
        HRESULT STDMETHODCALLTYPE Clone(IEnumExplorerCommand** clone) override
        {
            if (!clone) return E_POINTER;
            *clone = new (std::nothrow) CommandEnumerator(position);
            return *clone ? S_OK : E_OUTOFMEMORY;
        }
    };

    class ExplorerCommand final : public IExplorerCommand, private Lifetime
    {
        std::atomic<ULONG> refs{1};
        Command command;
    public:
        explicit ExplorerCommand(Command value) noexcept : command(value) {}
        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override
        {
            if (!result) return E_POINTER;
            *result = nullptr;
            if (iid != IID_IUnknown && iid != __uuidof(IExplorerCommand)) return E_NOINTERFACE;
            *result = static_cast<IExplorerCommand*>(this);
            AddRef();
            return S_OK;
        }
        ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
        ULONG STDMETHODCALLTYPE Release() override { const auto count = --refs; if (!count) delete this; return count; }
        HRESULT STDMETHODCALLTYPE GetTitle(IShellItemArray*, LPWSTR* title) override
        {
            return title ? SHStrDupW(Caption(command), title) : E_POINTER;
        }
        HRESULT STDMETHODCALLTYPE GetIcon(IShellItemArray*, LPWSTR* icon) override
        {
            if (!icon) return E_POINTER;
            *icon = nullptr;
            if (command != Root) return E_NOTIMPL;
            wchar_t path[32768]{};
            return GetExecutable(path) ? SHStrDupW(path, icon) : E_NOTIMPL;
        }
        HRESULT STDMETHODCALLTYPE GetToolTip(IShellItemArray*, LPWSTR* tip) override
        {
            if (!tip) return E_POINTER;
            *tip = nullptr;
            return E_NOTIMPL;
        }
        HRESULT STDMETHODCALLTYPE GetCanonicalName(GUID* value) override
        {
            if (!value) return E_POINTER;
            *value = modernId;
            value->Data4[7] ^= static_cast<BYTE>(command);
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE GetState(IShellItemArray*, BOOL, EXPCMDSTATE* state) override
        {
            if (!state) return E_POINTER;
            wchar_t path[32768]{};
            *state = !GetExecutable(path) ? ECS_HIDDEN : command == Header ? ECS_DISABLED : ECS_ENABLED;
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE Invoke(IShellItemArray*, IBindCtx*) override { return InvokeAction(command); }
        HRESULT STDMETHODCALLTYPE GetFlags(EXPCMDFLAGS* flags) override
        {
            if (!flags) return E_POINTER;
            *flags = command == Root ? ECF_HASSUBCOMMANDS : ECF_DEFAULT;
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE EnumSubCommands(IEnumExplorerCommand** commands) override
        {
            if (!commands) return E_POINTER;
            *commands = nullptr;
            if (command != Root) return E_NOTIMPL;
            *commands = new (std::nothrow) CommandEnumerator();
            return *commands ? S_OK : E_OUTOFMEMORY;
        }
    };

    HRESULT CommandEnumerator::Next(ULONG count, IExplorerCommand** found, ULONG* fetched)
    {
        if (!found || (count != 1 && !fetched)) return E_POINTER;
        if (fetched) *fetched = 0;
        constexpr Command entries[] = {NewContainer, Header, Previous, Command::Next, Pause, Stop};
        ULONG copied = 0;
        const auto start = position;
        while (copied < count && position < ARRAYSIZE(entries))
        {
            auto child = new (std::nothrow) ExplorerCommand(entries[position]);
            if (!child)
            {
                while (copied) { found[--copied]->Release(); found[copied] = nullptr; }
                position = start;
                return E_OUTOFMEMORY;
            }
            found[copied++] = child;
            ++position;
        }
        if (fetched) *fetched = copied;
        return copied == count ? S_OK : S_FALSE;
    }

    class LegacyMenu final : public IContextMenu, public IShellExtInit, private Lifetime
    {
        std::atomic<ULONG> refs{1};
    public:
        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override
        {
            if (!result) return E_POINTER;
            *result = nullptr;
            if (iid == IID_IUnknown || iid == __uuidof(IContextMenu)) *result = static_cast<IContextMenu*>(this);
            else if (iid == __uuidof(IShellExtInit)) *result = static_cast<IShellExtInit*>(this);
            else return E_NOINTERFACE;
            AddRef();
            return S_OK;
        }
        ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
        ULONG STDMETHODCALLTYPE Release() override { const auto count = --refs; if (!count) delete this; return count; }
        HRESULT STDMETHODCALLTYPE Initialize(PCIDLIST_ABSOLUTE, IDataObject*, HKEY) override { return S_OK; }
        HRESULT STDMETHODCALLTYPE QueryContextMenu(HMENU menu, UINT position, UINT first, UINT last, UINT flags) override
        {
            if ((flags & CMF_DEFAULTONLY) || last < first || last - first < Stop) return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, 0);
            HMENU opsis = CreatePopupMenu(), playback = CreatePopupMenu();
            if (!opsis || !playback)
            {
                if (opsis) DestroyMenu(opsis);
                if (playback) DestroyMenu(playback);
                return E_OUTOFMEMORY;
            }
            BOOL ok = AppendMenuW(opsis, MF_STRING, first + NewContainer, Caption(NewContainer));
            for (int c = Previous; c <= Stop && ok; ++c)
                ok = AppendMenuW(playback, MF_STRING, first + c, Caption(static_cast<Command>(c)));
            if (!ok || !AppendMenuW(opsis, MF_POPUP, reinterpret_cast<UINT_PTR>(playback), Caption(Header)))
            {
                DestroyMenu(playback);
                DestroyMenu(opsis);
                return E_FAIL;
            }
            if (!InsertMenuW(menu, position, MF_BYPOSITION | MF_POPUP, reinterpret_cast<UINT_PTR>(opsis), Caption(Root)))
            {
                DestroyMenu(opsis);
                return E_FAIL;
            }
            return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, Stop + 1);
        }
        HRESULT STDMETHODCALLTYPE InvokeCommand(CMINVOKECOMMANDINFO* info) override
        {
            if (!info) return E_POINTER;
            if (IS_INTRESOURCE(info->lpVerb))
                return InvokeAction(static_cast<Command>(LOWORD(reinterpret_cast<ULONG_PTR>(info->lpVerb))));
            for (int i = NewContainer; i <= Stop; i++)
            {
                char verb[40]{};
                WideCharToMultiByte(CP_UTF8, 0, labels[i].verb, -1, verb, ARRAYSIZE(verb), nullptr, nullptr);
                if (lstrcmpiA(info->lpVerb, verb) == 0) return InvokeAction(static_cast<Command>(i));
            }
            return E_INVALIDARG;
        }
        HRESULT STDMETHODCALLTYPE GetCommandString(UINT_PTR id, UINT type, UINT*, LPSTR buffer, UINT size) override
        {
            if (id > Stop) return E_INVALIDARG;
            if (type == GCS_VERBW && buffer && size)
                return StringCchCopyW(reinterpret_cast<LPWSTR>(buffer), size, labels[id].verb);
            return E_NOTIMPL;
        }
    };

    class Factory final : public IClassFactory, private Lifetime
    {
        std::atomic<ULONG> refs{1};
        bool modern;
    public:
        explicit Factory(bool value) noexcept : modern(value) {}
        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override
        {
            if (!result) return E_POINTER;
            *result = nullptr;
            if (iid != IID_IUnknown && iid != IID_IClassFactory) return E_NOINTERFACE;
            *result = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
        ULONG STDMETHODCALLTYPE Release() override { const auto count = --refs; if (!count) delete this; return count; }
        HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** result) override
        {
            if (!result) return E_POINTER;
            *result = nullptr;
            if (outer) return CLASS_E_NOAGGREGATION;
            IUnknown* handler = nullptr;
            if (modern) handler = new (std::nothrow) ExplorerCommand(Root);
            else handler = static_cast<IContextMenu*>(new (std::nothrow) LegacyMenu());
            if (!handler) return E_OUTOFMEMORY;
            auto hr = handler->QueryInterface(iid, result);
            handler->Release();
            return hr;
        }
        HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override { if (lock) ++objects; else --objects; return S_OK; }
    };
}

#pragma comment(linker, "/EXPORT:DllGetClassObject")
#pragma comment(linker, "/EXPORT:DllCanUnloadNow")

STDAPI DllGetClassObject(REFCLSID clsid, REFIID iid, void** result)
{
    if (!result) return E_POINTER;
    *result = nullptr;
    if (clsid != modernId && clsid != classicId) return CLASS_E_CLASSNOTAVAILABLE;
    auto factory = new (std::nothrow) Factory(clsid == modernId);
    if (!factory) return E_OUTOFMEMORY;
    auto hr = factory->QueryInterface(iid, result);
    factory->Release();
    return hr;
}

STDAPI DllCanUnloadNow()
{
    return objects.load() == 0 ? S_OK : S_FALSE;
}
