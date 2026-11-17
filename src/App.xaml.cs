using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace X1Control;
public partial class App : Application
{
    Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Directory.CreateDirectory(Files.Root);
        if(e.Args.Length==2 && e.Args[0]=="--wait") { try { System.Diagnostics.Process.GetProcessById(int.Parse(e.Args[1])).WaitForExit(8000); } catch {} }
        if(e.Args.Length>0 && e.Args[0]=="--watchdog") { Guard.Run(e.Args); Shutdown(); return; }
        if(e.Args.Length>0 && e.Args[0]=="--self-test") { try { Tests.Run(); Shutdown(0); } catch(Exception ex) { File.WriteAllText(Path.Combine(Files.Root,"tests.txt"),ex.ToString()); Shutdown(1); } return; }
        bool capture=e.Args.Length>0 && e.Args[0]=="--capture";
        if(!capture) {
            instance=new Mutex(true,@"Global\X1Control.Instance",out bool created);
            if(!created) { MessageBox.Show("X1 Control이 이미 실행 중입니다. 트레이 아이콘을 확인하세요."); Shutdown(); return; }
        }
        DispatcherUnhandledException+=(s,args)=> { Files.Log(args.Exception.ToString()); };
        var window=new MainWindow(); MainWindow=window; window.Show();
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
