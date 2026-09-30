using System.Runtime.InteropServices;
using SignalAtlas.Core;

namespace SignalAtlas.Resources;

public sealed class WindowsResourceProbe : IResourceProbe
{
    private static readonly WindowsCpuProbe Cpu=new();
    public ResourceSnapshot Sample()
    {
        var memory=new MemoryStatus{Length=(uint)Marshal.SizeOf<MemoryStatus>()};
        if(!GlobalMemoryStatusEx(ref memory))throw new InvalidOperationException("Cannot read system memory");
        long disk=new DriveInfo(Path.GetPathRoot(AppPaths.Root)!).AvailableFreeSpace;
        long? budget=null,usage=null;
        if(DxgiBudget.TryRead(out var b,out var u)){budget=b;usage=u;}
        BatteryPercent();
        return new ResourceSnapshot((long)memory.AvailablePhysical,disk,budget,usage,Cpu.Sample(),_battery,_ac);
    }
    private int? _battery;private bool? _ac;
    private void BatteryPercent()
    {
        if(GetSystemPowerStatus(out var status)){
            _battery=status.BatteryLifePercent==255?null:status.BatteryLifePercent;
            _ac=status.ACLineStatus==255?null:status.ACLineStatus==1;
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus { public uint Length;public uint MemoryLoad;public ulong TotalPhysical;public ulong AvailablePhysical;public ulong TotalPageFile;public ulong AvailablePageFile;public ulong TotalVirtual;public ulong AvailableVirtual;public ulong AvailableExtendedVirtual; }
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus {public byte ACLineStatus;public byte BatteryFlag;public byte BatteryLifePercent;public byte SystemStatusFlag;public uint BatteryLifeTime;public uint BatteryFullLifeTime;}
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll")]private static extern bool GetSystemPowerStatus(out PowerStatus status);
}

public sealed class WindowsCpuProbe
{
    private readonly object _sync=new();
    private (ulong Idle,ulong Kernel,ulong User)? _previous;
    public double? Sample()
    {
        if(!GetSystemTimes(out var idle,out var kernel,out var user))return null;
        var current=(Idle:idle.Value,Kernel:kernel.Value,User:user.Value);
        lock(_sync)
        {
            var previous=_previous;_previous=current;
            if(previous is null)return null;
            ulong kernelDelta=current.Kernel-previous.Value.Kernel;
            ulong userDelta=current.User-previous.Value.User;
            ulong idleDelta=current.Idle-previous.Value.Idle;
            ulong total=kernelDelta+userDelta;
            return total==0?null:Math.Clamp(100d*(total-Math.Min(idleDelta,total))/total,0,100);
        }
    }
    [StructLayout(LayoutKind.Sequential)]private struct FileTime{public uint Low,High;public ulong Value=>((ulong)High<<32)|Low;}
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle,out FileTime kernel,out FileTime user);
}

internal static class DxgiBudget
{
    [StructLayout(LayoutKind.Sequential)]private struct VideoMemoryInfo{public ulong Budget;public ulong CurrentUsage;public ulong AvailableForReservation;public ulong CurrentReservation;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct AdapterDesc1{[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Description;public uint VendorId;public uint DeviceId;public uint SubSysId;public uint Revision;public ulong DedicatedVideoMemory;public ulong DedicatedSystemMemory;public ulong SharedSystemMemory;public long AdapterLuid;public uint Flags;}
    [DllImport("dxgi.dll",PreserveSig=true)]private static extern int CreateDXGIFactory1(ref Guid iid,out IntPtr factory);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int EnumAdapters1(IntPtr factory,uint index,out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int QueryInterface(IntPtr self,ref Guid iid,out IntPtr target);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int GetDesc1(IntPtr adapter,out AdapterDesc1 desc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int QueryVideoMemoryInfo(IntPtr adapter,uint node,uint segment,out VideoMemoryInfo info);
    private static T Method<T>(IntPtr self,int slot) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(self),slot*IntPtr.Size));
    public static bool TryRead(out long budget,out long usage)
    {
        budget=usage=0;IntPtr factory=IntPtr.Zero;
        try
        {
            var iidFactory=new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
            var iidAdapter3=new Guid("645967A4-1392-4310-A798-8053CE3E93FD"); // IDXGIAdapter3
            if(CreateDXGIFactory1(ref iidFactory,out factory)<0)return false;
            var enumerate=Method<EnumAdapters1>(factory,12);
            for(uint index=0;index<16;index++)
            {
                if(enumerate(factory,index,out var adapter)<0)break;
                try
                {
                    if(Method<GetDesc1>(adapter,10)(adapter,out var desc)<0 || desc.DedicatedVideoMemory<2UL*1024*1024*1024)continue;
                    if(Method<QueryInterface>(adapter,0)(adapter,ref iidAdapter3,out var adapter3)<0)continue;
                    try
                    {
                        if(Method<QueryVideoMemoryInfo>(adapter3,14)(adapter3,0,0,out var info)>=0)
                        {
                            ulong effective=Math.Min(Math.Min(info.Budget,desc.DedicatedVideoMemory),8UL*1024*1024*1024);
                            if(effective>(ulong)budget){budget=(long)effective;usage=(long)info.CurrentUsage;}
                        }
                    }
                    finally{Marshal.Release(adapter3);}
                }
                finally{Marshal.Release(adapter);}
            }
            return budget>0;
        }
        catch{return false;}
        finally{if(factory!=IntPtr.Zero)Marshal.Release(factory);}
    }
}

public enum ResourceDecision{Normal,Reduced,Defer}
public sealed record LoadConfiguration(int Context,string Gpu,ResourceDecision Decision,string Reason);
public static class ResourceGovernor
{
    private const long GiB=1024L*1024*1024;
    public static LoadConfiguration Decide(ResourceSnapshot sample,long estimateGpuBytes,int preferredContext=8192,int minimumContext=4096,string gpu="max",bool restrictBattery=true)
    {
        if(sample.FreeDiskBytes<2*GiB)return new(minimumContext,gpu,ResourceDecision.Defer,"Less than 2 GiB disk free");
        if(sample.AvailableRamBytes<4*GiB)return new(minimumContext,gpu,ResourceDecision.Defer,"Less than 4 GiB RAM available");
        if(restrictBattery && sample.OnAcPower==false && sample.BatteryPercent<25)return new(minimumContext,gpu,ResourceDecision.Defer,"Battery below 25%");
        if(sample.GpuBudgetBytes is null || sample.GpuUsageBytes is null)return new(minimumContext,gpu,ResourceDecision.Defer,"GPU budget unavailable");
        long projected=sample.GpuUsageBytes.Value+estimateGpuBytes;
        long headroom=sample.GpuBudgetBytes.Value-projected;
        if(headroom<1.5*GiB)return new(minimumContext,gpu,ResourceDecision.Defer,"Projected GPU headroom below 1.5 GiB");
        bool reduced=headroom<2*GiB || projected>sample.GpuBudgetBytes.Value*.75 || sample.AvailableRamBytes<6*GiB || sample.FreeDiskBytes<5*GiB || sample.CpuPercent>85 || restrictBattery && sample.OnAcPower==false && sample.BatteryPercent<50;
        return new(reduced?minimumContext:preferredContext,gpu,reduced?ResourceDecision.Reduced:ResourceDecision.Normal,reduced?"Reduced resource mode":"Safe");
    }
    public static bool Emergency(ResourceSnapshot s)=>s.AvailableRamBytes<2*GiB || s.GpuBudgetBytes is >0 && s.GpuUsageBytes>=s.GpuBudgetBytes*.92;
}
