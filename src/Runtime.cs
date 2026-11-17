using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Win32;

namespace X1Control;
public static class Files
{
    public static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"X1Control");
    public static void Log(string message) { try { Directory.CreateDirectory(Root); File.AppendAllText(Path.Combine(Root,"events.log"),$"{DateTime.Now:O} {message}\n"); } catch {} }
}
public record Lease(bool Active,DateTime Expires,long Deadline=0);
public sealed class Guard
{
    public static bool ShouldRestore(Lease? lease,bool parentAlive,DateTime now,long ticks=-1)=>lease?.Active==true&&(!parentAlive||(ticks>=0&&lease.Deadline>0?ticks>lease.Deadline:now>lease.Expires));
    readonly string path=Path.Combine(Files.Root,"lease-"+Environment.ProcessId+".json");
    Process? process;
    public bool Ready=>process!=null&&!process.HasExited&&File.Exists(path+".ready");
    public bool Active { get; private set; }
    public void Start()
    {
        Directory.CreateDirectory(Files.Root);
        var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        info.ArgumentList.Add("--watchdog"); info.ArgumentList.Add(Environment.ProcessId.ToString()); info.ArgumentList.Add(Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString()); info.ArgumentList.Add(path);
        process=Process.Start(info);
    }
    public void Write(bool active)
    {
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(new Lease(active,DateTime.UtcNow.AddSeconds(5),Environment.TickCount64+5000)));
        File.Move(path+".tmp",path,true); Active=active;
    }
    public static void Run(string[] args)
    {
        string path=args[3]; bool parentAlive=true; bool tripped=false; Lease? lastLease=null;
        try {
            Machine.Detect(); using var fan=new X1Fan(); // Load module now; no EC write.
            File.WriteAllText(path+".ready","ready");
            while(true) {
                try { using var parent=Process.GetProcessById(int.Parse(args[1])); parentAlive=!parent.HasExited&&parent.StartTime.ToUniversalTime().Ticks==long.Parse(args[2]); } catch { parentAlive=false; }
                Lease? lease=lastLease; bool readError=false;try { if(File.Exists(path)) {lease=JsonSerializer.Deserialize<Lease>(File.ReadAllText(path));lastLease=lease;} } catch {readError=true;}
                if(ShouldRestore(lease,parentAlive,DateTime.UtcNow,Environment.TickCount64)) {
                    tripped=true;
                    try { fan.Auto(); Files.Log("Watchdog: firmware fan restored."); File.WriteAllText(path+".tripped","복귀됨"); break; }
                    catch(Exception ex) { Files.Log("Watchdog restore retry: "+ex.Message); }
                }
                if(!parentAlive && lease?.Active!=true && !readError) break;
                Thread.Sleep(1000);
            }
        } catch(Exception ex) { Files.Log("Watchdog failed: "+ex.Message); }
        finally { try { File.Delete(path+".ready"); if(!tripped) File.Delete(path); } catch {} }
    }
}

public sealed class Engine
{
    public Sample Latest { get; private set; } =new();
    public event Action<Sample>? Updated;
    public volatile FanMode Requested=FanMode.Firmware;
    public volatile bool ReturnToFirmware;
    public volatile int Manual=55;
    public int[] Custom=FanPolicy.Curve(FanMode.Balanced);
    public volatile bool Available;
    public string FanStatus="센서를 연결하고 있습니다…";
    public long UiPulse=Environment.TickCount64;
    readonly CancellationTokenSource cancel=new();
    Task? loop;
    X1Fan? fan; Sensors? sensors; Guard? guard;
    readonly FanPolicy policy=new();
    FanSession? control; readonly object controlLock=new();
    volatile bool suspended;
    bool fault; FanMode last=FanMode.Firmware;
    public void Start()=>loop=Task.Run(Run);
    static bool Conflict()=>Process.GetProcesses().Any(p=> { try { return new[]{"OneXConsole","HandheldCompanion","HandheldCompanionService","CompatLayerCT"}.Contains(p.ProcessName,StringComparer.OrdinalIgnoreCase); } catch {return false;} finally { p.Dispose(); } });
    async Task Run()
    {
        try {
            Machine.Detect(); sensors=new();
            try { fan=new(); guard=new(); guard.Start(); control=new(fan,guard.Write); } catch(Exception ex) { FanStatus=ex.Message; }
            while(!cancel.IsCancellationRequested) {
                var sw=Stopwatch.StartNew();
                Sample s;
                try { s=sensors.Read(); } catch(Exception ex) { s=new(){Status=ex.Message}; }
                s.Time=DateTime.Now;
                bool conflict=Conflict();
                lock(controlLock) try {
                    int? observedMode=null;
                    if(fan!=null && !conflict) { var state=fan.ReadState(); s.Duty=state.Duty; s.Rpm=state.Rpm; observedMode=state.Mode; }
                    if(ReturnToFirmware && fan!=null && !conflict) { fan.Auto(); ReturnToFirmware=false; observedMode=0; }
                    Available=fan!=null && guard?.Ready==true && !conflict && !fault;
                    if(last!=Requested) { policy.Reset(); last=Requested; }
                    double age=sw.Elapsed.TotalSeconds;
                    int? desired=policy.Next(Requested,s.CpuTemp,s.GpuTemp,age,Manual,Custom);
                    if(Environment.TickCount64-Interlocked.Read(ref UiPulse)>4000 || suspended || conflict || !Available || cancel.IsCancellationRequested) desired=null;
                    if(desired.HasValue && fan!=null && guard!=null) {
                        // Lease is recorded BEFORE the first write so a partial write is recovered.
                        control!.Set(desired.Value);
                        s.Duty=desired.Value;
                        FanStatus=$"{MainWindow.ModeName(Requested)} · 적용 {desired.Value}%";
                    } else {
                        Restore();
                        if(Requested!=FanMode.Firmware) Requested=FanMode.Firmware;
                        FanStatus=conflict?"OneXConsole / Handheld Companion을 종료하면 제어할 수 있습니다.":fan==null?"관리자 실행과 PawnIO 설치가 필요합니다.":fault?"제어 오류 · 기본 자동 복귀 확인 필요":!Available?"감시 프로세스 연결 대기 / 앱 재시작 필요":observedMode==1?"기존 EC 수동 설정 감지 · 기본 자동 적용으로 복귀하세요.":"기기 기본 자동 · 펌웨어가 팬을 조절합니다.";
                    }
                } catch(Exception ex) {
                    fault=true; Available=false; Requested=FanMode.Firmware; FanStatus="팬 연결 오류: "+ex.Message;
                    Files.Log(FanStatus); try { Restore(); } catch(Exception reset) { Files.Log("Restore failed: "+reset.Message); }
                }
                Latest=s; Updated?.Invoke(s);
                try { await Task.Delay(Math.Max(100,1000-(int)sw.ElapsedMilliseconds),cancel.Token); } catch(OperationCanceledException) { break; }
            }
        } catch(Exception ex) { FanStatus=ex.Message; Files.Log(ex.ToString()); }
        finally { lock(controlLock) {try { Restore(); } catch(Exception ex) { Files.Log("Exit restore failed; watchdog will retry: "+ex.Message); } fan?.Dispose();} sensors?.Dispose(); }
    }
    void Restore()
    {
        if(control?.Active==true) { control.Restore(); policy.Reset(); }
    }
    public async Task Stop() { cancel.Cancel(); if(loop!=null) await Task.WhenAny(loop,Task.Delay(2500)); }
    public void Suspend() { suspended=true; Requested=FanMode.Firmware; Interlocked.Exchange(ref UiPulse,0); if(Monitor.TryEnter(controlLock,600)){try{Restore();}catch(Exception ex){Files.Log("Suspend restore: "+ex.Message);}finally{Monitor.Exit(controlLock);}} }
    public void Resume() {suspended=false;Requested=FanMode.Firmware;}
}
