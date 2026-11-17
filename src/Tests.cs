using System;
using System.IO;

namespace X1Control;
public static class Tests
{
    public static void Run()
    {
        int count=0; void Check(bool ok,string name) { if(!ok) throw new Exception(name); count++; }
        var p=new FanPolicy(); var c=FanPolicy.Curve(FanMode.Balanced);
        Check(p.Next(FanMode.Manual,91,40,0,35,c)==100,"Emergency override"); p.Reset();
        Check(p.Next(FanMode.Manual,82,40,0,35,c)==85,"Thermal floor");
        Check(p.Next(FanMode.Manual,null,50,0,55,c)==null,"Missing CPU fails closed");
        Check(p.Next(FanMode.Balanced,60,50,5,55,c)==null,"Stale sensor fails closed");
        Check(p.Next(FanMode.Manual,double.NaN,50,0,55,c)==null,"NaN fails closed");
        Check(p.Next(FanMode.Manual,0,50,0,55,c)==null,"Zero CPU fails closed");
        Check(p.Next(FanMode.Firmware,60,50,0,55,c)==null,"Firmware releases");
        p.Reset(); Check(p.Next(FanMode.Manual,40,95,0,35,c)==100,"GPU emergency");
        p.Reset(); Check(p.Next(FanMode.Manual,40,40,0,0,c)==35,"Minimum duty");
        p.Reset(); Check(p.Next(FanMode.Manual,40,40,0,200,c)==100,"Maximum duty");
        p.Reset(); p.Next(FanMode.Manual,91,40,0,35,c);
        for(int i=0;i<4;i++) Check(p.Next(FanMode.Manual,50,40,0,35,c)==100,"Cooldown hold");
        Check(p.Next(FanMode.Manual,50,40,0,35,c)==96,"Cooldown ramp");
        Check(FanPolicy.Interpolate(65,c)==60,"Interpolation");
        var valid=FanPolicy.Validate(new[]{10,90,40,20,99,45});
        Check(valid[0]==35&&valid[2]==90&&valid[5]==100,"Monotonic custom curve");
        for(int t=1;t<=110;t++) foreach(var mode in new[]{FanMode.Quiet,FanMode.Balanced,FanMode.Boost}) { p.Reset(); int? value=p.Next(mode,t,40,0,55,c); Check(value>=35&&value<=100,"Bounds "+mode+" "+t); }
        var fake=new FakeFan(); bool lease=false;
        var control=new FanSession(fake,active=>lease=active);
        fake.OnSet=()=>Check(lease,"Lease precedes hardware write");
        control.Set(55); Check(control.Active&&fake.Duty==55,"Applied control");
        fake.FailRestore=true;try{control.Restore();}catch{}
        Check(control.Active&&lease,"Restore failure retains watchdog lease");
        fake.FailRestore=false;control.Restore();Check(!control.Active&&!lease&&fake.Restores==1,"Restore retry clears lease");
        fake.FailSet=true;try{control.Set(60);}catch{}
        Check(control.Active&&lease,"Partial write retains lease"); control.Restore();
        var now=DateTime.UtcNow;
        Check(Guard.ShouldRestore(new(true,now.AddSeconds(-1)),true,now),"Stale heartbeat triggers watchdog");
        Check(Guard.ShouldRestore(new(true,now.AddSeconds(5)),false,now),"Parent exit triggers watchdog");
        Check(!Guard.ShouldRestore(new(false,now.AddSeconds(-1)),false,now),"Inactive lease does not write EC");
        Check(!Guard.ShouldRestore(new(true,now.AddSeconds(5)),true,now),"Fresh heartbeat no restore");
        Check(!Guard.ShouldRestore(null,false,now),"Missing lease no EC write");
        Check(Guard.ShouldRestore(new(true,now.AddHours(1),5000),true,now,6000),"Clock rollback cannot extend lease");
        using(var module=typeof(LibreHardwareMonitor.Hardware.Computer).Assembly.GetManifestResourceStream("LibreHardwareMonitor.Resources.PawnIo.LpcIO.bin")) Check(module!=null&&module.Length>1000,"Signed LpcIO resource packaged");
        File.WriteAllText(Path.Combine(Files.Root,"tests.txt"),$"PASS: {count} assertions (policy, write failures, restore retry, watchdog)\n");
    }
    sealed class FakeFan:IFan
    {
        public int Duty,Restores;public bool FailSet,FailRestore;public Action? OnSet;
        public void Set(int duty){OnSet?.Invoke();Duty=duty;if(FailSet)throw new IOException("write failed");}
        public void Auto(){if(FailRestore)throw new IOException("restore failed");Restores++;}
    }
}
