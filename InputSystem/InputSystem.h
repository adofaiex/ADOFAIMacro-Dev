#pragma once
#ifdef INPUTSYSTEM_EXPORTS
#define INPUT_API __declspec(dllexport)
#else
#define INPUT_API __declspec(dllimport)
#endif
#include <windows.h>

// 输入模式枚举
enum class InputMode : int {
    Auto = 0,  // 自动选择最底层可用方式
    NtUserInjectKeyboard = 1,  // NtUserInjectKeyboardInput (最底层)
    NtUserSendInput = 2,  // NtUserSendInput
    SendInput = 3,  // 标准 SendInput (兜底)
};

extern "C" {
    INPUT_API int  __stdcall Initialize(int maxQueueSize);
    INPUT_API int  __stdcall PushKeyEvent(BYTE keyCode, BOOL isDown, DWORD delayMs);
    INPUT_API int  __stdcall SendKeyDirect(BYTE keyCode, BOOL isDown);
    INPUT_API int  __stdcall SendKeyCombination(BYTE* keys, int keyCount, DWORD delayMs);
    INPUT_API int  __stdcall SendText(const char* text);
    INPUT_API int  __stdcall StartProcessing();
    INPUT_API int  __stdcall StopProcessing();
    INPUT_API void __stdcall ClearQueue();
    INPUT_API int  __stdcall GetInputQueueStatus(int* queueSize, int* processedCount);
    INPUT_API void __stdcall Shutdown();
    INPUT_API void __stdcall EmergencyStop();
    INPUT_API BOOL __stdcall IsUsingNtFunctions();
    INPUT_API int  __stdcall GetPressedKeysCount();

    // ── 模式控制 ──────────────────────────────
    INPUT_API int  __stdcall SetInputMode(int mode);   // 设置模式，返回实际生效的模式
    INPUT_API int  __stdcall GetInputMode();           // 获取当前模式
    INPUT_API int  __stdcall GetAvailableModes();      // 返回可用模式的位掩码
}