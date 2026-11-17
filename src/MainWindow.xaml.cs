using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace X1Control;
public partial class MainWindow : Window
{
    readonly Engine engine=new();
    readonly List<Sample> history=new();
    readonly List<Sample> session=new();
    readonly DispatcherTimer pulse=new(){Interval=TimeSpan.FromMilliseconds(500)};
    readonly System.Windows.Forms.NotifyIcon tray=new();
    readonly List<Slider> editors=new();
    readonly List<TextBlock> editorValues=new();
    bool closing,closed,editSync;
    FanMode selectedMode=FanMode.Firmware;
    int draftManual=55;
    int[] draftCurve=FanPolicy.Curve(FanMode.Balanced);
    public static string ModeName(FanMode mode)=>mode switch { FanMode.Firmware=>"기본 자동",FanMode.Quiet=>"저소음",FanMode.Balanced=>"균형",FanMode.Boost=>"강력 냉각",FanMode.Manual=>"수동",_=>"사용자 커브" };
    public MainWindow()
    {
        InitializeComponent();
        FitToWorkArea();
        LoadSettings(); BuildEditors();
        foreach(var graph in new[]{CpuSpark,GpuSpark,FanSpark,PowerSpark,TemperatureChart}) graph.Samples=history;
        engine.Updated+=s=>Dispatcher.BeginInvoke(()=>Update(s));
        pulse.Tick+=(s,e)=>Interlocked.Exchange(ref engine.UiPulse,Environment.TickCount64); pulse.Start();
        tray.Icon=LoadTrayIcon(); tray.Text="X1 Control"; tray.Visible=true;
        tray.DoubleClick+=(s,e)=>Dispatcher.Invoke(()=>{Show();WindowState=WindowState.Normal;Activate();});
        var menu=new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("대시보드 열기",null,(s,e)=>Dispatcher.Invoke(()=>{Show();Activate();}));
        menu.Items.Add("기기 기본 자동으로 복귀",null,(s,e)=>{engine.Requested=FanMode.Firmware;engine.ReturnToFirmware=true;});
        menu.Items.Add("종료",null,(s,e)=>Dispatcher.Invoke(Close)); tray.ContextMenuStrip=menu;
        SystemEvents.PowerModeChanged+=PowerChanged;
        SystemEvents.SessionEnding+=SessionEnding;
        Closing+=OnClosing;
        Loaded+=(s,e)=>{engine.Start(); if(Environment.GetCommandLineArgs().Contains("--ui-test")) UiTest();};
    }
    static string Format(double? v,string unit,string fmt="0")=>v.HasValue&&double.IsFinite(v.Value)?v.Value.ToString(fmt,CultureInfo.CurrentCulture)+" "+unit:"— "+unit;
    void FitToWorkArea()
    {
        var area=SystemParameters.WorkArea;
        MaxWidth=area.Width; MaxHeight=area.Height;
        Width=Math.Min(1380,Math.Max(840,area.Width-24));
        Height=Math.Min(780,Math.Max(620,area.Height-18));
        Left=area.Left+(area.Width-Width)/2; Top=area.Top+(area.Height-Height)/2;
    }
    static System.Drawing.Icon LoadTrayIcon()
    {
        try { using var stream=System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/X1Control;component/Assets/X1Control.ico")).Stream; return new System.Drawing.Icon(stream); }
        catch { return System.Drawing.SystemIcons.Application; }
    }
    void Update(Sample s)
    {
        if(closed) return;
        var summary=s.Summary(); history.Add(summary); history.RemoveAll(x=>(s.Time-x.Time).TotalSeconds>600);
        session.Add(summary); if(session.Count>86400) session.RemoveAt(0);
        CpuTempText.Text=Format(s.CpuTemp,"°C","0.0"); GpuTempText.Text=Format(s.GpuTemp,"°C","0.0");
        RpmText.Text=Format(s.Rpm,"RPM","#,0"); PowerText.Text=Format(s.Power,"W","0.0");
        CpuSub.Text=s.CpuTemp.HasValue?"CPU 패키지 · 최고 "+Format(history.Max(x=>x.CpuTemp),"°C"):"CPU 온도 센서 연결 필요";
        DutyText.Text=s.Duty.HasValue?"팬 출력 "+Format(s.Duty,"%"):"EC 센서 연결 필요";
        BatteryText.Text="배터리 "+Format(s.Battery,"%");
        CpuClockText.Text=Format(s.CpuClock/1000,"GHz","0.00"); GpuClockText.Text=Format(s.GpuClock,"MHz","#,0");
        CpuLoadText.Text="최고 코어 클럭 · "+Format(s.CpuLoad,"%","0.0"); GpuLoadText.Text="GPU Core · "+Format(s.GpuLoad,"%","0.0");
        CpuBar.Value=s.CpuLoad??0; GpuBar.Value=s.GpuLoad??0;
        double? ramUsable=s.MemoryUsedGB+s.MemoryAvailableGB;
        MemoryTitle.Text=Machine.InstalledRamGB is double installedRam?$"RAM · {installedRam:0.#} GB 설치":"RAM 사용률";
        MemoryText.Text=s.MemoryUsedGB is double used && ramUsable is double total?$"{used:0.0} / {total:0.0} GB":"— / — GB";
        MemoryBar.Value=s.Memory??0;
        MemorySub.Text=s.Memory is double load && s.MemoryAvailableGB is double available?$"{load:0.0}% 사용 · 여유 {available:0.0} GB":"메모리 센서 연결 필요";
        LiveLabel.Text=$"LIVE  ·  {s.Time:HH:mm:ss}";
        HistoryLabel.Text="최대 10분 · 1초 갱신 · 자동 축";
        DeviceLabel.Text=$"{Machine.Model} · AMD Ryzen 7 8840U / Radeon 780M";
        FanStatusText.Text=engine.FanStatus; ModeBadge.Text=ModeName(engine.Requested);
        ManualSlider.IsEnabled=selectedMode==FanMode.Manual;
        CurveChart.Firmware=selectedMode==FanMode.Firmware;
        CurveChart.Curve=selectedMode==FanMode.Custom?draftCurve:selectedMode==FanMode.Manual?new[]{draftManual,draftManual,draftManual,draftManual,Math.Max(85,draftManual),100}:FanPolicy.Curve(selectedMode);
        CurveChart.CurrentTemp=s.CpuTemp;
        foreach(var graph in new[]{CpuSpark,GpuSpark,FanSpark,PowerSpark,TemperatureChart,CurveChart}) graph.InvalidateVisual();
        bool installed=LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;
        InstallButton.Visibility=installed?Visibility.Collapsed:Visibility.Visible;
        AdminButton.Visibility=Machine.Admin?Visibility.Collapsed:Visibility.Visible;
        SetupBanner.Visibility=installed&&Machine.Admin&&s.CpuTemp.HasValue?Visibility.Collapsed:Visibility.Visible;
        SetupText.Text=!installed?"최초 1회: 센서 드라이버를 설치한 뒤 관리자로 다시 실행하세요. 현재 읽을 수 있는 센서는 바로 표시합니다.":!Machine.Admin?"CPU 온도와 X1 팬 제어를 위해 관리자로 다시 실행하세요.":"일부 센서를 읽지 못했습니다. 아래 연결 진단에서 상태를 확인하세요.";
        DiagnosticsText.Text=$"기기: {Machine.Vendor} / {Machine.Model}\nX1 8840U 일치: {Machine.Supported} · 관리자: {Machine.Admin} · PawnIO: {installed}\n팬: {engine.FanStatus}\n센서 {s.Sensors.Count}개 · 읽기 오류: {(string.IsNullOrEmpty(s.Status)?"없음":s.Status)}\n표시되지 않는 센서의 값은 추정하지 않습니다. RPM과 PWM 출력률은 별개입니다. 재실행 시 팬 모드는 기본 자동입니다.";
        if(session.Count%3==1) SensorsGrid.ItemsSource=s.Sensors;
        tray.Text=$"X1 | CPU {Format(s.CpuTemp,"°C")} | {ModeName(engine.Requested)}";
        if(session.Count==15) CaptureIfRequested();
    }
    void SelectMode(object sender,RoutedEventArgs e)
    {
        selectedMode=Enum.Parse<FanMode>((string)((Button)sender).Tag);
        ApplyButton.Content=ModeName(selectedMode)+" 적용";
        CurveCaption.Text="커브 미리보기 · "+ModeName(selectedMode)+" (보호 제어 별도)";
        ManualSlider.IsEnabled=selectedMode==FanMode.Manual;
        if(selectedMode==FanMode.Manual)ManualSlider.Value=draftManual;
    }
    void ApplyMode(object sender,RoutedEventArgs e)
    {
        var mode=selectedMode;
        if(mode!=FanMode.Firmware && (!engine.Available || !engine.Latest.CpuTemp.HasValue)) { MessageBox.Show(this,"팬 제어를 시작할 수 없습니다.\n\n"+engine.FanStatus+"\nCPU 온도가 읽혀야 제어할 수 있습니다.","팬 연결 상태"); return; }
        engine.Manual=draftManual; engine.Custom=(int[])draftCurve.Clone();
        if(mode==FanMode.Firmware) engine.ReturnToFirmware=true;
        engine.Requested=mode;
        Files.Log("Requested fan mode: "+mode);
    }
    void ManualChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(ManualText==null) return; draftManual=(int)e.NewValue; ManualText.Text=$"{draftManual} %";
        SaveSettings();
    }
    void BuildEditors()
    {
        for(int i=0;i<6;i++) {
            int index=i; var grid=new Grid(); grid.ColumnDefinitions.Add(new(){Width=new GridLength(42)});grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=new GridLength(48)});
            var label=new TextBlock{Text=$"{FanPolicy.Temperatures[i]}°",VerticalAlignment=VerticalAlignment.Center}; grid.Children.Add(label);
            var slider=new Slider{Value=draftCurve[i],IsEnabled=i<5,Margin=new Thickness(5,5,8,5)}; Grid.SetColumn(slider,1);grid.Children.Add(slider); editors.Add(slider);
            var value=new TextBlock{Text=$"{draftCurve[i]}%",VerticalAlignment=VerticalAlignment.Center}; Grid.SetColumn(value,2);grid.Children.Add(value);editorValues.Add(value);
            slider.ValueChanged+=(s,e)=> { if(editSync) return; var values=editors.Select(x=>(int)x.Value).ToArray(); if(values.Length!=6) return; draftCurve=FanPolicy.Validate(values); editSync=true; for(int n=0;n<6;n++){editors[n].Value=draftCurve[n];editorValues[n].Text=$"{draftCurve[n]}%";} editSync=false;SaveSettings(); };
            CurveEditors.Children.Add(grid);
        }
    }
    void LoadSettings()
    {
        try { var saved=JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(Files.Root,"settings.json"))); if(saved!=null) { draftManual=Math.Clamp(saved.Manual,35,100); draftCurve=FanPolicy.Validate(saved.Curve); ManualSlider.Value=draftManual; } } catch {}
    }
    void SaveSettings() { if(!IsLoaded) return; try { Directory.CreateDirectory(Files.Root); File.WriteAllText(Path.Combine(Files.Root,"settings.json"),JsonSerializer.Serialize(new Settings(draftManual,draftCurve))); } catch(Exception ex) { Files.Log(ex.Message); } }
    record Settings(int Manual,int[] Curve);
    void TopChecked(object sender,RoutedEventArgs e)=>Topmost=((CheckBox)sender).IsChecked==true;
    void ToTray(object sender,RoutedEventArgs e)=>Hide();
    void ExportCsv(object sender,RoutedEventArgs e)
    {
        var dialog=new SaveFileDialog{Filter="CSV 파일|*.csv",FileName=$"X1-{DateTime.Now:yyyyMMdd-HHmmss}.csv"}; if(dialog.ShowDialog(this)!=true)return;
        try { WriteCsv(dialog.FileName); MessageBox.Show(this,$"{session.Count:N0}개 측정값을 저장했습니다.","CSV 저장"); } catch(Exception ex) {MessageBox.Show(this,ex.Message);}
    }
    void WriteCsv(string path)
    {
        string V(double? v)=>v?.ToString("0.###",CultureInfo.InvariantCulture)??"";
        var lines=new List<string>{"Time,CPU_C,GPU_C,CPU_MHz,GPU_MHz,CPU_Percent,GPU_Percent,APU_W,Fan_RPM,Fan_Percent,Memory_Percent,Battery_Percent"};
        lines.AddRange(session.Select(s=>string.Join(",",s.Time.ToString("O"),V(s.CpuTemp),V(s.GpuTemp),V(s.CpuClock),V(s.GpuClock),V(s.CpuLoad),V(s.GpuLoad),V(s.Power),V(s.Rpm),V(s.Duty),V(s.Memory),V(s.Battery)))); File.WriteAllLines(path,lines,new UTF8Encoding(true));
    }
    void ExportDiagnostics(object sender,RoutedEventArgs e)
    {
        var dialog=new SaveFileDialog{Filter="JSON 파일|*.json",FileName="X1-diagnostics.json"}; if(dialog.ShowDialog(this)!=true)return;
        try { File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(new {Machine.Vendor,Machine.Model,Machine.Cpu,Machine.Admin,engine.FanStatus,Sample=engine.Latest},new JsonSerializerOptions{WriteIndented=true})); } catch(Exception ex){MessageBox.Show(this,ex.Message);}
    }
    void InstallDriver(object sender,RoutedEventArgs e)
    {
        string path=Path.Combine(AppContext.BaseDirectory,"PawnIO_setup.exe");
        try { if(!File.Exists(path)) throw new IOException("배포 폴더의 PawnIO_setup.exe를 찾지 못했습니다. ZIP 파일 전체를 압축 해제하세요."); Process.Start(new ProcessStartInfo(path){UseShellExecute=true,Verb="runas"}); SetupText.Text="설치 창에서 완료한 뒤 앱을 종료하고 다시 실행하세요."; } catch(Exception ex) { MessageBox.Show(this,ex.Message,"드라이버 설치"); }
    }
    void Elevate(object sender,RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!,"--wait "+Environment.ProcessId){UseShellExecute=true,Verb="runas"}); Close(); } catch(Exception ex){MessageBox.Show(this,ex.Message,"관리자 실행");}
    }
    void PowerChanged(object sender,PowerModeChangedEventArgs e) { if(e.Mode==PowerModes.Suspend) engine.Suspend(); else if(e.Mode==PowerModes.Resume) engine.Resume(); }
    void SessionEnding(object sender,SessionEndingEventArgs e) { engine.Suspend(); engine.Stop().GetAwaiter().GetResult(); }
    async void OnClosing(object? sender,System.ComponentModel.CancelEventArgs e)
    {
        if(closed)return; e.Cancel=true; if(closing)return; closing=true;
        SaveSettings(); await engine.Stop(); pulse.Stop(); tray.Dispose();
        SystemEvents.PowerModeChanged-=PowerChanged; SystemEvents.SessionEnding-=SessionEnding;
        closed=true; Close();
    }
    void CaptureIfRequested()
    {
        var args=Environment.GetCommandLineArgs(); int index=Array.IndexOf(args,"--capture"); if(index<0||index+1>=args.Length)return;
        var path=args[index+1];
        var bmp=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32); bmp.Render(this);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var file=File.Create(path))png.Save(file);
        File.WriteAllText(Path.ChangeExtension(path,"json"),JsonSerializer.Serialize(new{Machine.Model,Machine.Admin,engine.FanStatus,Sample=engine.Latest},new JsonSerializerOptions{WriteIndented=true}));
        WriteCsv(Path.ChangeExtension(path,"csv")); Close();
    }
    void UiTest()
    {
        try {
            foreach(var mode in Enum.GetValues<FanMode>()) {
                SelectMode(new Button{Tag=mode.ToString()},new RoutedEventArgs());
                if(selectedMode!=mode||engine.Requested!=FanMode.Firmware)throw new Exception("Mode preview changed hardware state");
            }
            SelectMode(new Button{Tag="Manual"},new RoutedEventArgs());
            ManualSlider.Value=65;
            if(draftManual!=65||engine.Manual!=55)throw new Exception("Manual preview committed without Apply");
            ManualSlider.Value=55;
            editors[0].Value=75;
            if(!draftCurve.SequenceEqual(FanPolicy.Validate(draftCurve))||engine.Custom[0]!=35)throw new Exception("Curve validation/preview failed");
            editSync=true;draftCurve=FanPolicy.Curve(FanMode.Balanced);for(int i=0;i<6;i++){editors[i].Value=draftCurve[i];editorValues[i].Text=$"{draftCurve[i]}%";}editSync=false;
            SelectMode(new Button{Tag="Balanced"},new RoutedEventArgs());
            File.WriteAllText(Path.Combine(Files.Root,"ui-tests.txt"),"PASS: six mode previews, manual draft, monotonic curve editing, no hardware control during preview\n");
        }catch(Exception ex){File.WriteAllText(Path.Combine(Files.Root,"ui-tests.txt"),"FAIL: "+ex);}
    }
}
