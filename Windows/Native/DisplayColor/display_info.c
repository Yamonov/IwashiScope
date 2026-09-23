#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdlib.h>
#include <wchar.h>

// Read-only query. Never change ICM mode, profiles, calibration ramps or HDR/ACM.
// Query the effective legacy-ICC profile, not an EXTENDED/native profile that
// would cause double conversion when Advanced Color treats WPF output as sRGB.
__declspec(dllexport) int __cdecl iw_display_info(HWND window, wchar_t *device,
    unsigned int device_count, wchar_t *profile, unsigned int profile_count,
    int *advanced_mode)
{
    MONITORINFOEXW monitor = {0};
    HDC dc;
    DWORD count = profile_count;
    UINT32 path_count = 0, mode_count = 0;
    DISPLAYCONFIG_PATH_INFO *paths = NULL;
    DISPLAYCONFIG_MODE_INFO *modes = NULL;
    int result = 0;
    *advanced_mode = -1; // Unknown is not reported as SDR.
    if (!device_count || !profile_count) return ERROR_INVALID_PARAMETER;
    profile[0] = device[0] = 0;
    monitor.cbSize = sizeof(monitor);
    if (!GetMonitorInfoW(MonitorFromWindow(window, MONITOR_DEFAULTTOPRIMARY),
        (MONITORINFO *)&monitor)) return (int)GetLastError();
    wcsncpy_s(device, device_count, monitor.szDevice, _TRUNCATE);
    dc = CreateDCW(L"DISPLAY", monitor.szDevice, NULL, NULL);
    if (!dc) return (int)GetLastError();
    if (!GetICMProfileW(dc, &count, profile)) result = (int)GetLastError();
    DeleteDC(dc);

    // Display topology can change between the size and data calls; retry only
    // the documented insufficient-buffer condition, with bounded allocations.
    for (int attempt = 0; attempt < 3; ++attempt) {
        LONG error = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, &path_count, &mode_count);
        if (error != ERROR_SUCCESS || path_count > 256 || mode_count > 1024) break;
        paths = (DISPLAYCONFIG_PATH_INFO *)calloc(path_count, sizeof(*paths));
        modes = (DISPLAYCONFIG_MODE_INFO *)calloc(mode_count, sizeof(*modes));
        if (!paths || !modes) break;
        error = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, &path_count, paths,
            &mode_count, modes, NULL);
        if (error == ERROR_SUCCESS) {
            for (UINT32 i = 0; i < path_count; ++i) {
                DISPLAYCONFIG_SOURCE_DEVICE_NAME source = {0};
                source.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                source.header.size = sizeof(source);
                source.header.adapterId = paths[i].sourceInfo.adapterId;
                source.header.id = paths[i].sourceInfo.id;
                if (DisplayConfigGetDeviceInfo(&source.header) != ERROR_SUCCESS ||
                    _wcsicmp(source.viewGdiDeviceName, monitor.szDevice)) continue;
                DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 color = {0};
                color.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2;
                color.header.size = sizeof(color);
                color.header.adapterId = paths[i].targetInfo.adapterId;
                color.header.id = paths[i].targetInfo.id;
                if (DisplayConfigGetDeviceInfo(&color.header) == ERROR_SUCCESS) {
                    *advanced_mode = (int)color.activeColorMode; // 0 SDR, 1 WCG, 2 HDR
                } else {
                    DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO old = {0};
                    old.header = color.header;
                    old.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO;
                    old.header.size = sizeof(old);
                    if (DisplayConfigGetDeviceInfo(&old.header) == ERROR_SUCCESS)
                        *advanced_mode = old.advancedColorEnabled ? 3 : 0;
                }
                break;
            }
        }
        free(paths); free(modes); paths = NULL; modes = NULL;
        if (error != ERROR_INSUFFICIENT_BUFFER) break;
    }
    free(paths); free(modes);
    return result;
}
