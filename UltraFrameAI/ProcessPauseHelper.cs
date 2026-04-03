using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UltraFrameAI;

internal static class ProcessPauseHelper
{
    private const uint ThreadSuspendResume = 0x0002;
    private static readonly IntPtr InvalidHandle = IntPtr.Zero;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SuspendThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    public static void Suspend(Process? process)
    {
        Apply(process, suspend: true);
    }

    public static void Resume(Process? process)
    {
        Apply(process, suspend: false);
    }

    private static void Apply(Process? process, bool suspend)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (process.HasExited)
            {
                return;
            }

            foreach (ProcessThread thread in process.Threads)
            {
                var handle = OpenThread(ThreadSuspendResume, false, (uint)thread.Id);
                if (handle == InvalidHandle)
                {
                    continue;
                }

                try
                {
                    if (suspend)
                    {
                        _ = SuspendThread(handle);
                    }
                    else
                    {
                        _ = ResumeThread(handle);
                    }
                }
                catch
                {
                }
                finally
                {
                    _ = CloseHandle(handle);
                }
            }
        }
        catch
        {
        }
    }
}
