using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Management;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using LibreHardwareMonitor.Hardware;

namespace X1Control;

public record SensorRow(string Device, string Name, string Type, float? Value, string Id);
public sealed class Sample
{
    public DateTime Time { get; set; } = DateTime.Now;
    public double? CpuTemp { get; set; } public double? GpuTemp { get; set; }
    public double? CpuClock { get; set; } public double? GpuClock { get; set; }
    public double? CpuLoad { get; set; } public double? GpuLoad { get; set; }
    public double? Power { get; set; } public double? Rpm { get; set; } public double? Duty { get; set; }
    public double? Memory { get; set; } public double? MemoryUsedGB { get; set; } public double? MemoryAvailableGB { get; set; } public double? Battery { get; set; }
    public string Status { get; set; } = "";
    public List<SensorRow> Sensors { get; set; } = new();
    public Sample Summary() { var copy=(Sample)MemberwiseClone(); copy.Sensors=new();return copy; }
}

public static class Machine
{
    public static string Vendor = "", Model = "", Cpu = "";
    public static double? InstalledRamGB;
    public static bool Admin => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public static bool Supported => Vendor.Trim().Equals("ONE-NETBOOK", StringComparison.OrdinalIgnoreCase) && Model.Trim().Equals("ONEXPLAYER X1 A", StringComparison.OrdinalIgnoreCase) && Cpu.Contains("8840U");
    public static void Detect()
    {
        using var cs = new ManagementObjectSearcher("SELECT Manufacturer,Model FROM Win32_ComputerSystem");
        foreach (ManagementObject o in cs.Get()) { Vendor = o["Manufacturer"]?.ToString() ?? ""; Model = o["Model"]?.ToString() ?? ""; o.Dispose(); }
        using var cpu = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
        foreach (ManagementObject o in cpu.Get()) { Cpu = o["Name"]?.ToString() ?? ""; o.Dispose(); }
        try {
            double bytes=0;
            using var ram = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
            foreach(ManagementObject o in ram.Get()) { bytes+=Convert.ToDouble(o["Capacity"]); o.Dispose(); }
            if(bytes>0) InstalledRamGB=Math.Round(bytes/1073741824.0,1);
        } catch { InstalledRamGB=null; }
    }
}

// Calls only the signed LpcIO module bundled in LibreHardwareMonitor 0.9.6.
// No arbitrary driver installation, raw physical memory access, or register scanning.
public sealed class PawnModule : IDisposable
{
    readonly SafeFileHandle handle;
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr sa,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool DeviceIoControl(SafeFileHandle handle,uint code,byte[] input,int inLen,byte[] output,int outLen,out int returned,IntPtr ov);
    public PawnModule()
    {
        handle=CreateFile(@"\\?\GLOBALROOT\Device\PawnIO",0xC0000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
        if(handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(),"PawnIO 드라이버 또는 관리자 권한이 필요합니다.");
        try {
            using var stream=typeof(Computer).Assembly.GetManifestResourceStream("LibreHardwareMonitor.Resources.PawnIo.LpcIO.bin") ?? throw new IOException("LpcIO 모듈 없음");
            using var ms=new MemoryStream(); stream.CopyTo(ms);
            Call(0xA1B22084,ms.ToArray(),0);
            Exec("ioctl_select_slot",new long[]{1},0);
        } catch { handle.Dispose(); throw; }
    }
    byte[] Call(uint code,byte[] input,int size)
    {
        var output=new byte[size];
        if(!DeviceIoControl(handle,code,input,input.Length,output,size,out int returned,IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if(returned!=size) throw new IOException("드라이버 응답 길이 불일치");
        return output;
    }
    long[] Exec(string fn,long[] args,int size)
    {
        var input=new byte[32+args.Length*8]; Encoding.ASCII.GetBytes(fn).CopyTo(input,0); Buffer.BlockCopy(args,0,input,32,args.Length*8);
        var bytes=Call(0xA1B22104,input,size*8); var output=new long[size]; Buffer.BlockCopy(bytes,0,output,0,bytes.Length); return output;
    }
    public void Out(int port,int value)=>Exec("ioctl_pio_outb",new long[]{port,value},0);
    public byte In(int port)=>(byte)Exec("ioctl_pio_inb",new long[]{port},1)[0];
    public void Dispose()=>handle.Dispose();
}

public interface IFan { void Set(int percent); void Auto(); }
public sealed class X1Fan : IFan, IDisposable
{
    readonly PawnModule module;
    readonly Mutex bus=new(false,@"Global\Access_ISABUS.HTP.Method");
    public X1Fan() {try {if(!Machine.Supported || !Machine.Admin) throw new InvalidOperationException("X1 8840U와 관리자 권한을 확인하세요."); module=new();}catch{bus.Dispose();throw;} }
    T Locked<T>(Func<T> action)
    {
        bool held=false;
        try { try { held=bus.WaitOne(500); } catch(AbandonedMutexException) { held=true; } if(!held) throw new IOException("센서 버스 사용 중");
            module.Out(0x4E,0x87); module.Out(0x4E,0x01); module.Out(0x4E,0x55); module.Out(0x4E,0xAA);
            return action();
        } finally { if(held) bus.ReleaseMutex(); }
    }
    void Address(int reg)
    {
        module.Out(0x4E,0x2E); module.Out(0x4F,0x11); module.Out(0x4E,0x2F); module.Out(0x4F,reg>>8);
        module.Out(0x4E,0x2E); module.Out(0x4F,0x10); module.Out(0x4E,0x2F); module.Out(0x4F,reg&255);
        module.Out(0x4E,0x2E); module.Out(0x4F,0x12); module.Out(0x4E,0x2F);
    }
    byte Read(int reg) { Address(reg); return module.In(0x4F); }
    void Write(int reg,byte value) { if(reg!=0x44A && reg!=0x44B) throw new InvalidOperationException(); Address(reg); module.Out(0x4F,value); if(Read(reg)!=value) throw new IOException("팬 명령 확인 실패"); }
    public (double Duty,double? Rpm,int Mode) ReadState()=>Locked(()=> {
        int mode=Read(0x44A), duty=Read(0x44B);
        if(mode>1 || duty>184) throw new IOException("X1 EC 응답이 예상 범위를 벗어났습니다.");
        // RPM mapping: Linux oxp-sensors X1 ACPI 0x58/0x59 -> direct EC 0x458/0x459.
        int high=Read(0x458), low=Read(0x459); if(Read(0x458)!=high) { high=Read(0x458); low=Read(0x459); }
        int rpm=(high<<8)|low;
        return (duty*100.0/184,rpm<=10000?(double?)rpm:null,mode);
    });
    public void Set(int percent)=>Locked(()=> { byte duty=(byte)Math.Round(Math.Clamp(percent,35,100)*184.0/100); Write(0x44B,duty); Write(0x44A,1); return true; });
    public void Auto()=>Locked(()=> { Write(0x44A,0); return true; });
    public void Dispose() { module.Dispose(); bus.Dispose(); }
}

public sealed class Sensors : IDisposable
{
    AdlxSensors? adlx;
    readonly Computer computer=new(){IsCpuEnabled=true,IsGpuEnabled=true,IsMemoryEnabled=true,IsBatteryEnabled=true,IsStorageEnabled=true};
    public string Error="";
    public Sensors() { try { computer.Open(); } catch(Exception ex) { Error=ex.Message; } try {adlx=new();Files.Log("ADLX initialized.");}catch(Exception ex){Files.Log("ADLX unavailable: "+ex.Message);} }
    void Visit(IHardware hw,List<SensorRow> rows)
    {
        try { hw.Update(); foreach(var s in hw.Sensors) rows.Add(new(hw.HardwareType.ToString(),s.Name,s.SensorType.ToString(),s.Value,s.Identifier.ToString())); }
        catch(Exception ex) { Error=ex.Message; }
        foreach(var h in hw.SubHardware) Visit(h,rows);
    }
    public Sample Read()
    {
        var rows=new List<SensorRow>(); Error=""; foreach(var h in computer.Hardware) Visit(h,rows);
        // LHM may return zero when its privileged backend is unavailable. Zero is
        // not a valid CPU temperature/clock/package-power reading on this device.
        rows=rows.Select(r=>r.Device=="Cpu" && (r.Type=="Temperature"||r.Type=="Clock"||r.Type=="Power") && (!Machine.Admin || !LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled || r.Value<=0) ? r with {Value=null}:r).ToList();
        double? Pick(string device,string type,string name)=>rows.Where(r=>r.Device==device&&r.Type==type&&r.Name.Contains(name,StringComparison.OrdinalIgnoreCase)&&r.Value.HasValue&&float.IsFinite(r.Value.Value)).Select(r=>(double?)r.Value).FirstOrDefault();
        double? Max(string dev,string type,string name)=>rows.Where(r=>r.Device==dev&&r.Type==type&&r.Name.Contains(name,StringComparison.OrdinalIgnoreCase)&&r.Value.HasValue&&float.IsFinite(r.Value.Value)).Select(r=>(double?)r.Value).DefaultIfEmpty().Max();
        var gpu=rows.Any(r=>r.Device=="GpuAmd")?"GpuAmd":"GpuNvidia";
        double? RamSensor(string type,string name)=>rows.Where(r=>r.Device=="Memory"&&r.Id.StartsWith("/ram/",StringComparison.OrdinalIgnoreCase)&&r.Type==type&&r.Name.Contains(name,StringComparison.OrdinalIgnoreCase)&&r.Value.HasValue&&float.IsFinite(r.Value.Value)).Select(r=>(double?)r.Value).FirstOrDefault();
        var s=new Sample { Sensors=rows,CpuTemp=Pick("Cpu","Temperature","Tctl")??Pick("Cpu","Temperature","Package")??Max("Cpu","Temperature",""),GpuTemp=Pick(gpu,"Temperature","Core")??Pick("Cpu","Temperature","GFX"),CpuClock=Max("Cpu","Clock","Core"),GpuClock=Pick(gpu,"Clock","Core")??Pick("Cpu","Clock","GFX"),CpuLoad=Pick("Cpu","Load","Total"),GpuLoad=Pick(gpu,"Load","Core")??Pick(gpu,"Load","D3D 3D"),Power=Pick("Cpu","Power","Package"),Memory=RamSensor("Load","Memory"),MemoryUsedGB=RamSensor("Data","Memory Used"),MemoryAvailableGB=RamSensor("Data","Memory Available"),Status=Error };
        var p=System.Windows.Forms.SystemInformation.PowerStatus; if(p.BatteryLifePercent>=0 && p.BatteryLifePercent<=1) s.Battery=p.BatteryLifePercent*100;
        if(adlx!=null) {try {adlx.Read(s);}catch(Exception ex){s.Status+=" ADLX: "+ex.Message;}}
        return s;
    }
    public void Dispose(){adlx?.Dispose();computer.Close();}
}
