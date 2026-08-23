#include <windows.h>
#include <string>
#include <vector>

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    std::vector<wchar_t> modulePath(32768);
    DWORD moduleLength = GetModuleFileNameW(nullptr, modulePath.data(), static_cast<DWORD>(modulePath.size()));
    if (moduleLength == 0 || moduleLength >= modulePath.size()) return 9;
    std::wstring directory(modulePath.data(), moduleLength);
    const size_t separator = directory.find_last_of(L"\\/");
    if (separator == std::wstring::npos) return 9;
    directory.resize(separator);

    const std::wstring scriptPath = directory + L"\\LivelyShuffle.ps1";
    const std::wstring disabledMarkerPath = directory + L"\\wallpaper-auto.disabled";
    if (GetFileAttributesW(disabledMarkerPath.c_str()) != INVALID_FILE_ATTRIBUTES) {
        return 0;
    }
    std::wstring command = L"powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"";
    command += scriptPath;
    command += L"\"";

    HANDLE job = CreateJobObjectW(nullptr, nullptr);
    if (!job) return 10;

    JOBOBJECT_EXTENDED_LIMIT_INFORMATION jobInfo = {};
    jobInfo.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
    if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, &jobInfo, sizeof(jobInfo))) {
        CloseHandle(job);
        return 11;
    }

    STARTUPINFOW startup = {};
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESHOWWINDOW;
    startup.wShowWindow = SW_HIDE;

    PROCESS_INFORMATION process = {};
    DWORD flags = CREATE_NO_WINDOW | CREATE_SUSPENDED;
    if (!CreateProcessW(nullptr, command.data(), nullptr, nullptr, FALSE, flags, nullptr, nullptr, &startup, &process)) {
        CloseHandle(job);
        return 12;
    }

    if (!AssignProcessToJobObject(job, process.hProcess)) {
        TerminateProcess(process.hProcess, 13);
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        CloseHandle(job);
        return 13;
    }

    ResumeThread(process.hThread);
    CloseHandle(process.hThread);
    WaitForSingleObject(process.hProcess, INFINITE);

    DWORD exitCode = 0;
    GetExitCodeProcess(process.hProcess, &exitCode);
    CloseHandle(process.hProcess);
    CloseHandle(job);
    return static_cast<int>(exitCode);
}
