#include <windows.h>
#include <shlobj.h>
#include <stdio.h>
#include <string.h>

#pragma comment(lib, "user32.lib")
#pragma comment(lib, "shell32.lib")

// Debug logger
void LogDebug(const char* format, ...) {
    char buffer[2048];
    va_list args;
    va_start(args, format);
    vsnprintf(buffer, sizeof(buffer), format, args);
    va_end(args);
    
    FILE* f = fopen("S3IO.log", "a");
    if (f) {
        SYSTEMTIME st;
        GetLocalTime(&st);
        fprintf(f, "[%02d:%02d:%02d.%03d] %s\n", st.wHour, st.wMinute, st.wSecond, st.wMilliseconds, buffer);
        fclose(f);
    }
}

// Command codes matching C# (Pure File I/O Only)
enum Command : unsigned short {
    CMD_NONE = 0,
    CMD_FILE_EXISTS = 101,
    CMD_FILE_READ = 102,
    CMD_FILE_WRITE = 103,
    CMD_FILE_DELETE = 104,
    CMD_DIR_EXISTS = 105,
    CMD_DIR_CREATE = 106,
    CMD_DIR_DELETE = 107,
    CMD_DIR_LISTFILES = 108,
    CMD_DIR_LISTDIRS = 109,
    CMD_GET_DOCUMENTS_PATH = 110,
    CMD_FILE_APPEND = 111
};

// Status codes matching C#
#define STATUS_IDLE 0
#define STATUS_CS_WRITING 1
#define STATUS_READY 2
#define STATUS_CPP_PROCESSING 3
#define STATUS_DONE 4

#define BUFFER_SIZE 16777220 // 16MB + 4 bytes
#define PAYLOAD_OFFSET 15

// Safe memory page traversal to locate the C# unmanaged buffer
volatile byte* FindSharedBuffer() {
    SYSTEM_INFO sysInfo;
    GetSystemInfo(&sysInfo);

    byte* addr = (byte*)sysInfo.lpMinimumApplicationAddress;
    byte* maxAddr = (byte*)sysInfo.lpMaximumApplicationAddress;

    MEMORY_BASIC_INFORMATION mbi;

    while (addr < maxAddr) {
        if (VirtualQuery(addr, &mbi, sizeof(mbi)) == sizeof(mbi)) {
            if (mbi.State == MEM_COMMIT &&
                mbi.Type == MEM_PRIVATE &&
                (mbi.Protect == PAGE_READWRITE || mbi.Protect == PAGE_EXECUTE_READWRITE) &&
                !(mbi.Protect & PAGE_GUARD)) {

                byte* ptr = (byte*)mbi.BaseAddress;
                size_t regionSize = mbi.RegionSize;

                if (regionSize >= 16) {
                    for (size_t offset = 0; offset + 16 <= regionSize; offset += 4) {
                        volatile byte* candidate = ptr + offset;
                        __try {
                            if (*(volatile unsigned int*)candidate == 0x4F493353 &&
                                *(volatile unsigned int*)(candidate + 4) == 0x4350495F &&
                                (candidate[8] == STATUS_CS_WRITING || candidate[8] == STATUS_IDLE || candidate[8] == STATUS_READY || candidate[8] == STATUS_DONE)) {
                                LogDebug("FindSharedBuffer: Found C# buffer signature 'S3IO_IPC' at address %p (Region Base: %p, Region Size: %zu)", candidate, mbi.BaseAddress, mbi.RegionSize);
                                return candidate;
                            }
                        } __except (EXCEPTION_EXECUTE_HANDLER) {
                            // Skip inaccessible memory safely
                        }
                    }
                }
            }
            addr = (byte*)mbi.BaseAddress + mbi.RegionSize;
        } else {
            addr += 4096;
        }
    }
    return NULL;
}

// List files or directories helper
void ListDirectory(const char* path, bool filesOnly, bool dirsOnly, char* responseBuffer, int maxResponseSize, int* outResponseSize) {
    char searchPath[MAX_PATH];
    sprintf(searchPath, "%s\\*", path);
    
    WIN32_FIND_DATAA findData;
    HANDLE hFind = FindFirstFileA(searchPath, &findData);
    if (hFind == INVALID_HANDLE_VALUE) {
        *(int*)responseBuffer = 0; // count = 0
        *outResponseSize = 4;
        return;
    }
    
    int count = 0;
    int offset = 4; // leave 4 bytes for count
    
    do {
        if (strcmp(findData.cFileName, ".") == 0 || strcmp(findData.cFileName, "..") == 0) {
            continue;
        }
        
        bool isDir = (findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
        if ((filesOnly && isDir) || (dirsOnly && !isDir)) {
            continue;
        }
        
        int nameLen = (int)strlen(findData.cFileName);
        if (offset + 4 + nameLen >= maxResponseSize) {
            break; // buffer full
        }
        
        *(int*)(responseBuffer + offset) = nameLen;
        offset += 4;
        memcpy(responseBuffer + offset, findData.cFileName, nameLen);
        offset += nameLen;
        
        count++;
    } while (FindNextFileA(hFind, &findData));
    
    FindClose(hFind);
    
    *(int*)responseBuffer = count;
    *outResponseSize = offset;
}

// Native IPC Thread
DWORD WINAPI IpcThread(LPVOID lpParam) {
    LogDebug("IpcThread: Started. Waiting for C# buffer...");
    
    volatile byte* buffer = NULL;
    int retries = 0;
    
    while (!buffer) {
        buffer = FindSharedBuffer();
        if (!buffer) {
            retries++;
            if (retries % 5 == 0) {
                LogDebug("IpcThread: Still scanning for C# buffer... (Attempt %d)", retries);
            }
            Sleep(1000);
        }
    }
    
    LogDebug("IpcThread: Buffer located at %p. Establishing connection.", buffer);
    buffer[8] = STATUS_IDLE; // Handshake complete

    while (true) {
        if (buffer[8] == STATUS_READY) {
            buffer[8] = STATUS_CPP_PROCESSING;
            
            unsigned short cmd = *(unsigned short*)(buffer + 9);
            int payloadSize = *(int*)(buffer + 11);
            char* payload = (char*)(buffer + PAYLOAD_OFFSET);
            int responseSize = 0;
            
            LogDebug("IpcThread: Received Command %d with payload size %d", cmd, payloadSize);
            bool success = false;
            
            switch (cmd) {
                case CMD_FILE_EXISTS: {
                    DWORD attr = GetFileAttributesA(payload);
                    payload[0] = (attr != INVALID_FILE_ATTRIBUTES && !(attr & FILE_ATTRIBUTE_DIRECTORY)) ? 1 : 0;
                    responseSize = 1;
                    success = true;
                    break;
                }
                case CMD_FILE_READ: {
                    HANDLE hFile = CreateFileA(payload, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
                    if (hFile != INVALID_HANDLE_VALUE) {
                        DWORD bytesRead = 0;
                        DWORD fileSize = GetFileSize(hFile, NULL);
                        if (fileSize < BUFFER_SIZE - PAYLOAD_OFFSET - 4) {
                            success = ReadFile(hFile, payload, fileSize, &bytesRead, NULL);
                            responseSize = (int)bytesRead;
                        }
                        CloseHandle(hFile);
                    }
                    break;
                }
                case CMD_FILE_WRITE: {
                    int pathLen = *(int*)payload;
                    const char* path = payload + 4;
                    const char* data = payload + 4 + pathLen;
                    int dataSize = payloadSize - 4 - pathLen;
                    
                    char parentDir[MAX_PATH] = {0};
                    strcpy(parentDir, path);
                    char* lastSlash = strrchr(parentDir, '\\');
                    if (!lastSlash) lastSlash = strrchr(parentDir, '/');
                    if (lastSlash) {
                        *lastSlash = '\0';
                        SHCreateDirectoryExA(NULL, parentDir, NULL);
                    }

                    HANDLE hFile = CreateFileA(path, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
                    if (hFile != INVALID_HANDLE_VALUE) {
                        DWORD bytesWritten = 0;
                        success = WriteFile(hFile, data, dataSize, &bytesWritten, NULL);
                        CloseHandle(hFile);
                    } else {
                        LogDebug("CMD_FILE_WRITE failed to open file '%s', error=%lu", path, GetLastError());
                    }
                    responseSize = 0;
                    break;
                }
                case CMD_FILE_APPEND: {
                    int pathLen = *(int*)payload;
                    const char* path = payload + 4;
                    const char* data = payload + 4 + pathLen;
                    int dataSize = payloadSize - 4 - pathLen;
                    
                    char parentDir[MAX_PATH] = {0};
                    strcpy(parentDir, path);
                    char* lastSlash = strrchr(parentDir, '\\');
                    if (!lastSlash) lastSlash = strrchr(parentDir, '/');
                    if (lastSlash) {
                        *lastSlash = '\0';
                        SHCreateDirectoryExA(NULL, parentDir, NULL);
                    }

                    HANDLE hFile = CreateFileA(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
                    if (hFile != INVALID_HANDLE_VALUE) {
                        SetFilePointer(hFile, 0, NULL, FILE_END);
                        DWORD bytesWritten = 0;
                        success = WriteFile(hFile, data, dataSize, &bytesWritten, NULL);
                        CloseHandle(hFile);
                    } else {
                        LogDebug("CMD_FILE_APPEND failed to open file '%s', error=%lu", path, GetLastError());
                    }
                    responseSize = 0;
                    break;
                }
                case CMD_FILE_DELETE: {
                    success = DeleteFileA(payload);
                    responseSize = 0;
                    break;
                }
                case CMD_DIR_EXISTS: {
                    DWORD attr = GetFileAttributesA(payload);
                    payload[0] = (attr != INVALID_FILE_ATTRIBUTES && (attr & FILE_ATTRIBUTE_DIRECTORY)) ? 1 : 0;
                    responseSize = 1;
                    success = true;
                    break;
                }
                case CMD_DIR_CREATE: {
                    success = CreateDirectoryA(payload, NULL);
                    responseSize = 0;
                    break;
                }
                case CMD_DIR_DELETE: {
                    bool recursive = payload[0] != 0;
                    const char* path = payload + 1;
                    
                    if (recursive) {
                        SHFILEOPSTRUCTA fileOp = {0};
                        fileOp.wFunc = FO_DELETE;
                        char doubleNullPath[MAX_PATH + 2] = {0};
                        strcpy(doubleNullPath, path);
                        fileOp.pFrom = doubleNullPath;
                        fileOp.fFlags = FOF_NO_UI;
                        success = (SHFileOperationA(&fileOp) == 0);
                    } else {
                        success = RemoveDirectoryA(path);
                    }
                    responseSize = 0;
                    break;
                }
                case CMD_DIR_LISTFILES: {
                    ListDirectory(payload, true, false, payload, BUFFER_SIZE - PAYLOAD_OFFSET, &responseSize);
                    success = true;
                    break;
                }
                case CMD_DIR_LISTDIRS: {
                    ListDirectory(payload, false, true, payload, BUFFER_SIZE - PAYLOAD_OFFSET, &responseSize);
                    success = true;
                    break;
                }
                case CMD_GET_DOCUMENTS_PATH: {
                    char docPath[MAX_PATH] = {0};
                    if (SHGetFolderPathA(NULL, CSIDL_PERSONAL, NULL, 0, docPath) == S_OK) {
                        int len = (int)strlen(docPath);
                        memcpy((void*)(buffer + 15), docPath, len + 1);
                        responseSize = len + 1;
                        success = true;
                    }
                    break;
                }
            }
            
            *(int*)(buffer + 11) = responseSize;
            buffer[8] = success ? STATUS_DONE : STATUS_IDLE;
            LogDebug("IpcThread: Command %d finished. Success: %d, Response Size: %d", cmd, success, responseSize);
        } else {
            Sleep(5);
        }
    }
    return 0;
}

// DLL Entrypoint
BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved) {
    switch (ul_reason_for_call) {
        case DLL_PROCESS_ATTACH:
            DisableThreadLibraryCalls(hModule);
            {
                FILE* f = fopen("S3IO.log", "w");
                if (f) {
                    fprintf(f, "--- S3IO ASI Loaded (Pure File I/O) ---\n");
                    fclose(f);
                }
            }
            LogDebug("DllMain: DLL_PROCESS_ATTACH. Starting IPC Thread.");
            CreateThread(NULL, 0, IpcThread, NULL, 0, NULL);
            break;
        case DLL_PROCESS_DETACH:
            break;
    }
    return TRUE;
}
