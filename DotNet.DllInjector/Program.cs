using System.Runtime.InteropServices;
using System.Text;

class Program
{
    // File names for Frida Gadget and the original executable
    private const string FridaGadgetFileName = "frida-gadget.dll";
    private const string OriginalExeFileName = "app_original.exe";

    // --- P/Invoke Constants ---
    private const uint CREATE_SUSPENDED = 0x00000004;
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint PAGE_READWRITE = 0x04;
    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONERROR = 0x00000010;

    // --- P/Invoke Declarations ---
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(string? lpApplicationName, string lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out UIntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out IntPtr lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    // --- Structures ---
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public uint cb;
        public string lpReserved; public string lpDesktop; public string lpTitle;
        public uint dwX; public uint dwY; public uint dwXSize; public uint dwYSize;
        public uint dwXCountChars; public uint dwYCountChars; public uint dwFillAttribute;
        public uint dwFlags; public short wShowWindow; public short cbReserved2;
        public IntPtr lpReserved2; public IntPtr hStdInput; public IntPtr hStdOutput; public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess; public IntPtr hThread;
        public uint dwProcessId; public uint dwThreadId;
    }

    // Displays messages in console if not silent injection
    private static void Log(string message)
    {
        #if DEBUG
        Console.WriteLine($"[Frida Gadget Wrapper] {message}");
        #endif
    }

    // Displays errors via MessageBox (always) and console (if not silent)
    private static void HandleError(string message, int win32Error = 0)
    {
        string fullMessage = message;
        if (win32Error != 0) fullMessage += $" Error code: {win32Error}";
        
        MessageBox(IntPtr.Zero, fullMessage, "Frida Gadget Wrapper Error", MB_OK | MB_ICONERROR);
        #if DEBUG
        Console.Error.WriteLine($"[Frida Gadget Wrapper ERROR] {fullMessage}");
        #endif
        
    }

    static void Main(string[] args)
    {
        Log("Initializing...");
        string currentDir = AppContext.BaseDirectory;

        string fridaGadgetPath = Path.Combine(currentDir, FridaGadgetFileName);
        string originalExePath = Path.Combine(currentDir, OriginalExeFileName);

        STARTUPINFO si = new STARTUPINFO { cb = (uint)Marshal.SizeOf<STARTUPINFO>() };
        PROCESS_INFORMATION pi = new PROCESS_INFORMATION();

        StringBuilder argumentsBuilder = new StringBuilder($"\"{originalExePath}\"");
        foreach (string arg in args)
        {
            argumentsBuilder.Append(" ");
            argumentsBuilder.Append(arg.Contains(" ") || arg.Contains("\"") ? $"\"{arg.Replace("\"", "\\\"")}\"" : arg);
        }
        string commandLine = argumentsBuilder.ToString();

        Log($"Attempting to execute command line: {commandLine}");

        Log($"Launching '{OriginalExeFileName}' suspended...");
        if (!CreateProcess(null, argumentsBuilder.ToString(), IntPtr.Zero, IntPtr.Zero, false, CREATE_SUSPENDED, IntPtr.Zero, currentDir, ref si, out pi))
        {
            HandleError($"Failed to launch '{OriginalExeFileName}' suspended.", Marshal.GetLastWin32Error());
            Environment.Exit(1);
        }

        Log($"Suspended process launched. PID: {pi.dwProcessId}");
        IntPtr hRemoteThread = IntPtr.Zero;

        try
        {
            Log($"Attempting to inject '{FridaGadgetFileName}'...");
            IntPtr hKernel32 = GetModuleHandle("kernel32.dll");
            if (hKernel32 == IntPtr.Zero) throw new Exception($"Failed to get handle for kernel32.dll.");

            IntPtr procAddress = GetProcAddress(hKernel32, "LoadLibraryW");
            if (procAddress == IntPtr.Zero) throw new Exception($"Failed to get LoadLibraryW address.");

            byte[] pathBytes = Encoding.Unicode.GetBytes(fridaGadgetPath + "\0");
            IntPtr remotePathAddr = VirtualAllocEx(pi.hProcess, IntPtr.Zero, (uint)pathBytes.Length, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (remotePathAddr == IntPtr.Zero) throw new Exception($"Error allocating remote memory.");

            UIntPtr bytesWritten;
            if (!WriteProcessMemory(pi.hProcess, remotePathAddr, pathBytes, (uint)pathBytes.Length, out bytesWritten)) throw new Exception($"Error writing to remote memory.");

            hRemoteThread = CreateRemoteThread(pi.hProcess, IntPtr.Zero, 0, procAddress, remotePathAddr, 0, out IntPtr threadId);
            if (hRemoteThread == IntPtr.Zero) throw new Exception($"Error creating remote thread.");

            Log($"Remote thread created. Waiting for injection...");
            WaitForSingleObject(hRemoteThread, 10000); 
            
            Log($"'{FridaGadgetFileName}' successfully injected into PID: {pi.dwProcessId}.");
        }
        catch (Exception ex)
        {
            HandleError($"DLL injection failed: {ex.Message}", Marshal.GetLastWin32Error());
            Environment.Exit(1);
        }
        finally
        {
            Log("Resuming main process...");
            if (ResumeThread(pi.hThread) == uint.MaxValue)
            {
                HandleError($"Failed to resume process.", Marshal.GetLastWin32Error());
                Environment.Exit(1);
            }
            Log("Process resumed.");

            Log("Closing handles.");
            if (hRemoteThread != IntPtr.Zero) CloseHandle(hRemoteThread);
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
        }

        Log("Task completed successfully.");
        #if DEBUG
        Log("Closing terminal...");
        #endif
        Environment.Exit(0);
    }
}