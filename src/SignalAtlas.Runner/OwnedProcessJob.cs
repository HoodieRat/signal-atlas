using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SignalAtlas.Runner;

// The runner is the only holder of this job. Its worker and any child processes
// created for the run are terminated if the runner exits unexpectedly.
internal sealed class OwnedProcessJob : IDisposable
{
    private const uint KillOnClose=0x00002000;
    private IntPtr _handle;
    private OwnedProcessJob(IntPtr handle)=>_handle=handle;

    public static OwnedProcessJob Attach(Process process)
    {
        IntPtr handle=CreateJobObjectW(IntPtr.Zero,null);
        if(handle==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not create run job object");
        var job=new OwnedProcessJob(handle);
        try
        {
            var limits=new ExtendedLimitInformation();
            limits.Basic.LimitFlags=KillOnClose;
            if(!SetInformationJobObject(handle,9,ref limits,(uint)Marshal.SizeOf<ExtendedLimitInformation>()))
                throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not configure run job object");
            if(!AssignProcessToJobObject(handle,process.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not assign worker to run job object");
            return job;
        }
        catch
        {
            job.Dispose();
            try{if(!process.HasExited)process.Kill(entireProcessTree:true);}catch{}
            throw;
        }
    }

    public void Dispose()
    {
        IntPtr handle=Interlocked.Exchange(ref _handle,IntPtr.Zero);
        if(handle!=IntPtr.Zero)CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit,PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize,MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass,SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount,WriteOperationCount,OtherOperationCount;
        public ulong ReadTransferCount,WriteTransferCount,OtherTransferCount;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemoryUsed,PeakJobMemoryUsed;
    }
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)]
    private static extern IntPtr CreateJobObjectW(IntPtr securityAttributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job,int informationClass,ref ExtendedLimitInformation info,uint length);
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
