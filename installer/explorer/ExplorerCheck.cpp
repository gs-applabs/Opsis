#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <cwchar>
#include <cstdio>

using ClassObject = HRESULT(STDAPICALLTYPE*)(REFCLSID, REFIID, void**);
const CLSID classic{0x7F45B246,0x0BA9,0x4C86,{0xAB,0x71,0x09,0xF9,0x69,0x61,0x0F,0x62}};
const CLSID modern{0xCA419189,0x286E,0x49FF,{0x9D,0x90,0x2F,0x46,0x24,0x27,0xB5,0xDE}};
constexpr wchar_t settings[] = L"Software\\Classes\\DesktopBackground\\shellex\\ContextMenuHandlers\\OpsisStudio";

#define VERIFY(expr) do { if (!(expr)) { fprintf(stderr, "Explorer check failed at line %d: %s\n", __LINE__, #expr); return 1; } } while (0)

int wmain()
{
    HMODULE module = LoadLibraryW(L"native\\OpsisExplorer.dll");
    VERIFY(module);
    auto getClass = reinterpret_cast<ClassObject>(GetProcAddress(module, "DllGetClassObject"));
    VERIFY(getClass);
    HKEY key{};
    VERIFY(RegCreateKeyExW(HKEY_CURRENT_USER, settings, 0, nullptr, 0, KEY_SET_VALUE, nullptr, &key, nullptr) == ERROR_SUCCESS);
    constexpr wchar_t executable[] = L"C:\\Opsis fixture\\Opsis.Studio.exe";
    const DWORD greek = 1;
    VERIFY(RegSetValueExW(key, L"Executable", 0, REG_SZ, reinterpret_cast<const BYTE*>(executable), sizeof(executable)) == ERROR_SUCCESS);
    VERIFY(RegSetValueExW(key, L"Greek", 0, REG_DWORD, reinterpret_cast<const BYTE*>(&greek), sizeof(greek)) == ERROR_SUCCESS);
    RegCloseKey(key);

    IClassFactory* factory{};
    VERIFY(SUCCEEDED(getClass(classic, IID_PPV_ARGS(&factory))));
    IContextMenu* legacy{};
    VERIFY(SUCCEEDED(factory->CreateInstance(nullptr, IID_PPV_ARGS(&legacy))));
    HMENU popup = CreatePopupMenu();
    VERIFY(popup);
    VERIFY(HRESULT_CODE(legacy->QueryContextMenu(popup, 0, 100, 199, CMF_NORMAL)) == 5);
    HMENU opsis = GetSubMenu(popup, 0);
    VERIFY(opsis && GetMenuItemCount(opsis) == 2 && GetMenuItemID(opsis, 0) == 100);
    HMENU wallpaper = GetSubMenu(opsis, 1);
    VERIFY(wallpaper && GetMenuItemCount(wallpaper) == 4);
    wchar_t label[96]{};
    VERIFY(GetMenuStringW(opsis, 1, label, ARRAYSIZE(label), MF_BYPOSITION) && wcscmp(label, L"Κινούμενο wallpaper") == 0);
    for (UINT i = 0; i < 4; ++i) VERIFY(GetMenuItemID(wallpaper, i) == 101 + i);
    VERIFY(DestroyMenu(popup));
    legacy->Release();
    factory->Release();

    VERIFY(SUCCEEDED(getClass(modern, IID_PPV_ARGS(&factory))));
    IExplorerCommand* root{};
    VERIFY(SUCCEEDED(factory->CreateInstance(nullptr, IID_PPV_ARGS(&root))));
    EXPCMDFLAGS flags{};
    VERIFY(SUCCEEDED(root->GetFlags(&flags)) && flags == ECF_HASSUBCOMMANDS);
    IEnumExplorerCommand* flyout{};
    VERIFY(SUCCEEDED(root->EnumSubCommands(&flyout)));
    IExplorerCommand* entries[7]{};
    ULONG count{};
    VERIFY(flyout->Next(ARRAYSIZE(entries), entries, &count) == S_FALSE && count == 6);
    constexpr const wchar_t* titles[] = {
        L"Νέο DeskContainer", L"Κινούμενο wallpaper", L"Προηγούμενο", L"Επόμενο", L"Παύση / συνέχιση", L"Διακοπή"
    };
    for (ULONG i = 0; i < count; ++i)
    {
        PWSTR title{};
        VERIFY(SUCCEEDED(entries[i]->GetTitle(nullptr, &title)) && wcscmp(title, titles[i]) == 0);
        CoTaskMemFree(title);
        EXPCMDSTATE state{};
        VERIFY(SUCCEEDED(entries[i]->GetState(nullptr, FALSE, &state)) && state == (i == 1 ? ECS_DISABLED : ECS_ENABLED));
        VERIFY(SUCCEEDED(entries[i]->GetFlags(&flags)) && flags == ECF_DEFAULT);
        entries[i]->Release();
    }
    flyout->Release();
    root->Release();
    factory->Release();
    RegDeleteTreeW(HKEY_CURRENT_USER, settings);
    FreeLibrary(module);
    puts("Classic nested HMENU and compact Explorer flyout passed.");
}
