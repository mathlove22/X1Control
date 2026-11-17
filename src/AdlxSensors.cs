using System;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;

namespace X1Control;
// Read-only bindings to AMD's published ADLX C ABI (ISystem.h,
// IPerformanceMonitoring.h). Uses the installed AMD driver, never ships a driver DLL.
public sealed class AdlxSensors : IDisposable
{
    IntPtr library,system,gpu,perf,support;
    bool initialized;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int QueryVersion(out ulong version);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Initialize(ulong version,out IntPtr system);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Terminate();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ObjectOut(IntPtr self,out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GpuObjectOut(IntPtr self,IntPtr gpu,out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int At(IntPtr self,uint index,out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint Count(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Release(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DoubleOut(IntPtr self,out double value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int IntOut(IntPtr self,out int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int BoolOut(IntPtr self,out byte value);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibraryEx(string file,IntPtr reserved,uint flags);
    T Export<T>(string name) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,name));
    static T Method<T>(IntPtr obj,int slot) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj),slot*IntPtr.Size));
    static void Check(int result) { if(result!=0 && result!=1 && result!=2) throw new InvalidOperationException("AMD ADLX 응답: "+result); }
    static void Drop(ref IntPtr obj) { if(obj!=IntPtr.Zero) { Method<Release>(obj,1)(obj);obj=IntPtr.Zero; } }
    public AdlxSensors()
    {
        try {
            string? path=null;
            using var query=new ManagementObjectSearcher("SELECT Name,InstalledDisplayDrivers FROM Win32_VideoController");
            foreach(ManagementObject o in query.Get()) {
                if(o["Name"]?.ToString()?.Contains("AMD")==true) foreach(var driver in (o["InstalledDisplayDrivers"]?.ToString()??"").Split(',')) {
                    var candidate=Path.Combine(Path.GetDirectoryName(driver.Trim())??"","amdadlx64.dll");
                    if(Path.IsPathFullyQualified(candidate)&&candidate.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&File.Exists(candidate)){path=candidate;break;}
                } o.Dispose();
            }
            if(path==null) throw new IOException("AMD ADLX 런타임 없음");
            library=LoadLibraryEx(path,IntPtr.Zero,0x1100);if(library==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            Check(Export<QueryVersion>("ADLXQueryFullVersion")(out ulong version));
            Check(Export<Initialize>("ADLXInitialize")(version,out system));initialized=true;
            Check(Method<ObjectOut>(system,1)(system,out IntPtr list));
            try {
                uint size=Method<Count>(list,3)(list);
                for(uint i=0;i<size;i++) {
                    Check(Method<At>(list,11)(list,i,out IntPtr item));
                    try { Check(Method<ObjectOut>(item,7)(item,out IntPtr name));if(Marshal.PtrToStringAnsi(name)?.Contains("780M")==true){gpu=item;item=IntPtr.Zero;break;} } finally {Drop(ref item);}
                }
            } finally {Drop(ref list);}
            if(gpu==IntPtr.Zero)throw new IOException("ADLX Radeon 780M 없음");
            Check(Method<ObjectOut>(system,9)(system,out perf));
            Check(Method<GpuObjectOut>(perf,21)(perf,gpu,out support));
        } catch {Dispose();throw;}
    }
    bool Supported(int slot)=>support!=IntPtr.Zero&&Method<BoolOut>(support,slot)(support,out byte value)==0&&value!=0;
    public void Read(Sample s)
    {
        IntPtr metrics=IntPtr.Zero;
        try {
            Check(Method<GpuObjectOut>(perf,18)(perf,gpu,out metrics));
            if(Supported(6)&&Method<DoubleOut>(metrics,7)(metrics,out double temp)==0&&double.IsFinite(temp)&&temp>0&&temp<120) { s.GpuTemp=temp; s.Sensors.Add(new("GpuAmd","GPU Core (ADLX)","Temperature",(float)temp,"/adlx/780m/temperature")); }
            if(Supported(4)&&Method<IntOut>(metrics,5)(metrics,out int clock)==0&&clock>=0&&clock<5000) { s.GpuClock=clock;s.Sensors.Add(new("GpuAmd","GPU Core (ADLX)","Clock",clock,"/adlx/780m/clock")); }
            if(Supported(3)&&Method<DoubleOut>(metrics,4)(metrics,out double load)==0&&double.IsFinite(load)&&load>=0&&load<=100) { s.GpuLoad=load; }
        } finally { Drop(ref metrics); }
    }
    public void Dispose()
    {
        Drop(ref support);Drop(ref perf);Drop(ref gpu);
        if(initialized) {Export<Terminate>("ADLXTerminate")();initialized=false;system=IntPtr.Zero;}
        if(library!=IntPtr.Zero){NativeLibrary.Free(library);library=IntPtr.Zero;}
    }
}
