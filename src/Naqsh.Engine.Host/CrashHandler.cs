using System.Diagnostics;
using System.Runtime.InteropServices;
using Naqsh.Core.Diagnostics;
using Serilog;

namespace Naqsh.Engine.Host;

/// <summary>Writes a minidump and a support bundle when the process crashes.</summary>
internal static partial class CrashHandler
{
    private const int MiniDumpWithDataSegs = 0x1;
    private const int MiniDumpWithHandleData = 0x4;
    private const int MiniDumpWithThreadInfo = 0x1000;

    public static void Install(ILogger log, string processName)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            log.Fatal(e.ExceptionObject as Exception, "Unhandled exception; writing crash dump");
            try
            {
                Directory.CreateDirectory(NaqshPaths.Crashes);
                var dump = Path.Combine(NaqshPaths.Crashes, $"{processName}-{DateTime.Now:yyyyMMdd-HHmmss}.dmp");
                WriteDump(dump);
                log.Fatal("Crash dump written: {Path}", dump);
                Log.CloseAndFlush();
                SupportBundle.Create(NaqshPaths.Crashes, null, new Dictionary<string, string> { ["Crash dump"] = dump });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Error(ex, "Could not write crash dump");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }

    public static void WriteDump(string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var self = Process.GetCurrentProcess();
        MiniDumpWriteDump(
            self.Handle,
            (uint)self.Id,
            fs.SafeFileHandle.DangerousGetHandle(),
            MiniDumpWithDataSegs | MiniDumpWithHandleData | MiniDumpWithThreadInfo,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    [LibraryImport("dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MiniDumpWriteDump(IntPtr process, uint processId, IntPtr file, int dumpType, IntPtr exceptionParam, IntPtr userStreamParam, IntPtr callbackParam);
}
