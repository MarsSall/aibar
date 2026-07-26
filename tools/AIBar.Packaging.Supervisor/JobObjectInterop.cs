using System.ComponentModel;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

namespace AIBar.Packaging.Supervisor;

public abstract class SupervisorSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    protected SupervisorSafeHandle() : base(true) { }
    protected SupervisorSafeHandle(IntPtr handle, bool ownsHandle) : base(ownsHandle) => SetHandle(handle);
    protected override bool ReleaseHandle() => CloseHandle(handle);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
public class SafeJobHandle : SupervisorSafeHandle { public SafeJobHandle() { } public SafeJobHandle(IntPtr handle, bool ownsHandle) : base(handle, ownsHandle) { } }
public class SafeCompletionPortHandle : SupervisorSafeHandle { public SafeCompletionPortHandle() { } public SafeCompletionPortHandle(IntPtr handle, bool ownsHandle) : base(handle, ownsHandle) { } }
public class SafePipeHandle : SupervisorSafeHandle { public SafePipeHandle() { } public SafePipeHandle(IntPtr handle, bool ownsHandle) : base(handle, ownsHandle) { } }
public class SafeProcessHandle : SupervisorSafeHandle { public SafeProcessHandle() { } public SafeProcessHandle(IntPtr handle, bool ownsHandle) : base(handle, ownsHandle) { } }
public class SafeThreadHandle : SupervisorSafeHandle { public SafeThreadHandle() { } public SafeThreadHandle(IntPtr handle, bool ownsHandle) : base(handle, ownsHandle) { } }

public sealed class WindowsProcessSupervisorInterop : IProcessSupervisorInterop
{
    public SafeJobHandle? CreateJob() => Native.CreateJobObjectW(IntPtr.Zero, null);
    public bool ConfigureJob(SafeJobHandle job) => Set(job, 9, new Native.ExtendedLimit { Basic = new() { LimitFlags = 0x2000 } });
    public SafeCompletionPortHandle? CreateCompletionPort() => Native.CreateIoCompletionPort(new IntPtr(-1), IntPtr.Zero, UIntPtr.Zero, 1);
    public bool AssociateCompletionPort(SafeJobHandle job, SafeCompletionPortHandle port) => Set(job, 7, new Native.AssociateCompletionPort { Port = port.DangerousGetHandle() });
    public SupervisorPipes? CreatePipes()
    {
        var security = new Native.SecurityAttributes { Length = Marshal.SizeOf<Native.SecurityAttributes>(), InheritHandle = true };
        if (!Native.CreatePipe(out var childIn, out var parentIn, ref security, 0)) return null;
        if (!Native.CreatePipe(out var parentOut, out var childOut, ref security, 0)) { childIn.Dispose(); parentIn.Dispose(); return null; }
        if (!Native.CreatePipe(out var parentErr, out var childErr, ref security, 0)) { childIn.Dispose(); parentIn.Dispose(); parentOut.Dispose(); childOut.Dispose(); return null; }
        if (!Native.SetHandleInformation(parentIn, 1, 0) || !Native.SetHandleInformation(parentOut, 1, 0) || !Native.SetHandleInformation(parentErr, 1, 0)) { childIn.Dispose(); parentIn.Dispose(); parentOut.Dispose(); childOut.Dispose(); parentErr.Dispose(); childErr.Dispose(); return null; }
        return new(parentIn, parentOut, parentErr, childIn, childOut, childErr);
    }
    public SupervisorProcess? CreateSuspended(ProcessLaunchRequest request, SupervisorPipes pipes, IReadOnlyList<SafePipeHandle> inheritedHandles)
    {
        using var attributes = new AttributeList(inheritedHandles);
        var startup = new Native.StartupInfoEx { Startup = new() { Size = Marshal.SizeOf<Native.StartupInfoEx>(), Flags = 0x100, StdInput = pipes.ChildStdin.DangerousGetHandle(), StdOutput = pipes.ChildStdout.DangerousGetHandle(), StdError = pipes.ChildStderr.DangerousGetHandle() }, AttributeList = attributes.Pointer };
        if (!Native.CreateProcessW(request.ApplicationName, new StringBuilder(request.CommandLine), IntPtr.Zero, IntPtr.Zero, true, 0x80004, IntPtr.Zero, null, ref startup, out var information)) return null;
        var process = new SafeProcessHandle(information.Process, true); var thread = new SafeThreadHandle(information.Thread, true);
        if (process.IsInvalid || thread.IsInvalid) { process.Dispose(); thread.Dispose(); return null; }
        return new(process, thread);
    }
    public bool AssignProcessToJob(SafeJobHandle job, SafeProcessHandle process) => Native.AssignProcessToJobObject(job, process);
    public bool ResumeThread(SafeThreadHandle thread) => Native.ResumeThread(thread) != uint.MaxValue;
    public void TerminateProcess(SafeProcessHandle process) => Native.TerminateProcess(process, 1);
    public bool TryGetExitCode(SafeProcessHandle process, out int exitCode) => Native.GetExitCodeProcess(process, out exitCode);
    public bool IsProcessSignaled(SafeProcessHandle process) => Native.WaitForSingleObject(process, 0) == 0;
    public bool TryGetActiveProcesses(SafeJobHandle job, out uint activeProcesses)
    {
        activeProcesses = 0; var size = Marshal.SizeOf<Native.BasicAccounting>(); var memory = Marshal.AllocHGlobal(size);
        try { if (!Native.QueryInformationJobObject(job, 1, memory, (uint)size, IntPtr.Zero)) return false; activeProcesses = Marshal.PtrToStructure<Native.BasicAccounting>(memory).ActiveProcesses; return true; }
        finally { Marshal.FreeHGlobal(memory); }
    }
    public bool ObserveCompletionPacket(SafeCompletionPortHandle port) => Native.GetQueuedCompletionStatus(port, out _, out _, out _, 0);
    public Stream OpenReadPipe(SafePipeHandle pipe) => new FileStream(new SafeFileHandle(pipe.DangerousGetHandle(), false), FileAccess.Read, 4096, false);
    private static bool Set<T>(SafeJobHandle job, int type, T value) where T : struct { var size = Marshal.SizeOf<T>(); var memory = Marshal.AllocHGlobal(size); try { Marshal.StructureToPtr(value, memory, false); return Native.SetInformationJobObject(job, type, memory, (uint)size); } finally { Marshal.FreeHGlobal(memory); } }

    private sealed class AttributeList : IDisposable
    {
        private IntPtr _list;
        private IntPtr _handles;
        private bool _initialized;
        public IntPtr Pointer => _list;
        public AttributeList(IReadOnlyList<SafePipeHandle> handles)
        {
            try
            {
                nuint bytes = 0;
                if (Native.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref bytes) || bytes == 0 || Marshal.GetLastWin32Error() != 122) throw new Win32Exception();
                _list = Marshal.AllocHGlobal((int)bytes);
                if (!Native.InitializeProcThreadAttributeList(_list, 1, 0, ref bytes)) throw new Win32Exception();
                _initialized = true;
                _handles = Marshal.AllocHGlobal(IntPtr.Size * handles.Count);
                Marshal.Copy(handles.Select(x => x.DangerousGetHandle()).ToArray(), 0, _handles, handles.Count);
                if (!Native.UpdateProcThreadAttribute(_list, 0, new IntPtr(0x20002), _handles, (nuint)(IntPtr.Size * handles.Count), IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (_initialized) Native.DeleteProcThreadAttributeList(_list);
            if (_handles != IntPtr.Zero) Marshal.FreeHGlobal(_handles);
            if (_list != IntPtr.Zero) Marshal.FreeHGlobal(_list);
            _initialized = false; _handles = IntPtr.Zero; _list = IntPtr.Zero;
        }
    }
    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct SecurityAttributes { public int Length; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle; }
        [StructLayout(LayoutKind.Sequential)] internal struct BasicLimit { public long PerProcessUserTime, PerJobUserTime; public uint LimitFlags; public UIntPtr MinWorkingSet, MaxWorkingSet, ActiveProcessLimit; public IntPtr Affinity; public uint PriorityClass, SchedulingClass; }
        [StructLayout(LayoutKind.Sequential)] internal struct IoCounters { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
        [StructLayout(LayoutKind.Sequential)] internal struct BasicAccounting { public long TotalUserTime, TotalKernelTime, ThisPeriodUserTime, ThisPeriodKernelTime; public uint TotalPageFaultCount, TotalProcesses, ActiveProcesses, TotalTerminatedProcesses; }
        [StructLayout(LayoutKind.Sequential)] internal struct ExtendedLimit { public BasicLimit Basic; public IoCounters Io; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
        [StructLayout(LayoutKind.Sequential)] internal struct AssociateCompletionPort { public IntPtr CompletionKey; public IntPtr Port; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct StartupInfo { public int Size; public string? Reserved, Desktop, Title; public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags, ShowWindow; public short Reserved2; public IntPtr Reserved2Pointer, StdInput, StdOutput, StdError; }
        [StructLayout(LayoutKind.Sequential)] internal struct StartupInfoEx { public StartupInfo Startup; public IntPtr AttributeList; }
        [StructLayout(LayoutKind.Sequential)] internal struct ProcessInformation { public IntPtr Process, Thread; public int ProcessId, ThreadId; }
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] internal static extern SafeJobHandle? CreateJobObjectW(IntPtr attributes, string? name);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetInformationJobObject(SafeJobHandle job, int type, IntPtr data, uint length);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeCompletionPortHandle? CreateIoCompletionPort(IntPtr file, IntPtr existingPort, UIntPtr key, uint threads);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CreatePipe(out SafePipeHandle read, out SafePipeHandle write, ref SecurityAttributes attributes, uint size);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetHandleInformation(SafePipeHandle handle, uint mask, uint flags);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CreateProcessW(string app, StringBuilder command, IntPtr pa, IntPtr ta, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment, string? directory, ref StartupInfoEx startup, out ProcessInformation information);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool AssignProcessToJobObject(SafeJobHandle job, SafeProcessHandle process);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint ResumeThread(SafeThreadHandle thread);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetExitCodeProcess(SafeProcessHandle process, out int exitCode);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool QueryInformationJobObject(SafeJobHandle job, int informationClass, IntPtr data, uint length, IntPtr returnLength);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetQueuedCompletionStatus(SafeCompletionPortHandle port, out uint bytes, out UIntPtr key, out IntPtr overlapped, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref nuint size);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returnedSize);
        [DllImport("kernel32.dll")] internal static extern void DeleteProcThreadAttributeList(IntPtr list);
    }
}
