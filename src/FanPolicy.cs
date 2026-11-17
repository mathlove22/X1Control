using System;
using System.Linq;

namespace X1Control;
public enum FanMode { Firmware, Quiet, Balanced, Boost, Manual, Custom }
public sealed class FanSession
{
    readonly IFan fan; readonly Action<bool> lease;
    public bool Active {get;private set;}
    public FanSession(IFan fan,Action<bool> lease) {this.fan=fan;this.lease=lease;}
    public void Set(int percent) {lease(true);Active=true;fan.Set(percent);}
    public void Restore() {if(!Active)return;fan.Auto();lease(false);Active=false;}
}
public sealed class FanPolicy
{
    public static readonly int[] Temperatures={40,50,60,70,80,90};
    public static int[] Curve(FanMode mode)=>mode switch { FanMode.Quiet=>new[]{35,35,42,55,78,100},FanMode.Boost=>new[]{45,55,70,85,100,100},_=>new[]{35,40,52,68,88,100} };
    public static int[] Validate(int[] values)
    {
        if(values.Length!=6) throw new ArgumentException("6개 커브 값이 필요합니다.");
        var v=values.Select(x=>Math.Clamp(x,35,100)).ToArray();
        for(int i=1;i<v.Length;i++) v[i]=Math.Max(v[i-1],v[i]); v[5]=100; return v;
    }
    public static double Interpolate(double temp,int[] curve)
    {
        if(temp<=40) return curve[0]; if(temp>=90) return 100;
        int index=(int)((temp-40)/10); double fraction=(temp-Temperatures[index])/10;
        return curve[index]+fraction*(curve[index+1]-curve[index]);
    }
    int? previous; int coolSamples;
    public void Reset() { previous=null; coolSamples=0; }
    public int? Next(FanMode mode,double? cpu,double? gpu,double ageSeconds,int manual,int[] custom)
    {
        if(mode==FanMode.Firmware || !cpu.HasValue || !double.IsFinite(cpu.Value) || cpu<=0 || cpu>115 || ageSeconds>4 || ageSeconds<0) { Reset(); return null; }
        double temp=Math.Max(cpu.Value,gpu.HasValue&&double.IsFinite(gpu.Value)?gpu.Value:0);
        int target=temp>=90?100:mode==FanMode.Manual?Math.Clamp(manual,35,100):(int)Math.Round(Interpolate(temp,mode==FanMode.Custom?Validate(custom):Curve(mode)));
        // Manual still has a thermal floor, then 100% emergency override.
        if(temp>=80) target=Math.Max(target,85);
        if(previous.HasValue && target<previous.Value) { coolSamples++; if(coolSamples<5) return previous; target=Math.Max(target,previous.Value-4); } else coolSamples=0;
        previous=target; return target;
    }
}
