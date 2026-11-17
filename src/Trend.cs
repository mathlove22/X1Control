using System;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace X1Control;
public sealed class Trend : FrameworkElement
{
    public string Kind { get; set; }="Temperature";
    public bool Compact { get; set; }
    public List<Sample> Samples { get; set; }=new();
    public int[] Curve { get; set; }=FanPolicy.Curve(FanMode.Balanced);
    public bool Firmware { get; set; }=true;
    public double? CurrentTemp { get; set; }
    static Brush Brush(string c)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(c));
    void Label(DrawingContext dc,string text,double x,double y,string color="#718B9B",double size=10) { dc.DrawText(new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,Brush(color),VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y)); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); double w=ActualWidth,h=ActualHeight;
        if(w<30||h<24) return;
        double left=Compact?0:30,top=8,bottom=Compact?2:24,right=8,pw=w-left-right,ph=h-top-bottom;
        double max=Kind=="Fan"?6000:Kind=="Power"?45:100;
        double span=Samples.Count>0?Math.Clamp((Samples[^1].Time-Samples[0].Time).TotalSeconds,60,600):60;
        Point Pt(double x,double value)=>new(left+x*pw,top+(1-Math.Clamp(value/max,0,1))*ph);
        if(!Compact) {
            foreach(int tick in new[]{0,25,50,75,100}) { double y=Pt(0,tick).Y; dc.DrawLine(new Pen(Brush("#263A46"),1),new Point(left,y),new Point(w-right,y)); Label(dc,tick.ToString(),0,y-6); }
            if(Kind=="Temperature") { var pen=new Pen(Brush("#946B42"),1){DashStyle=DashStyles.Dash}; dc.DrawLine(pen,Pt(0,90),Pt(1,90)); Label(dc,$"−{Math.Ceiling(span/60)}분",left,h-15); Label(dc,"현재",w-34,h-15); }
            if(Kind=="Curve") { Label(dc,"40°C",left,h-15); Label(dc,"65°C",left+pw*.5-12,h-15); Label(dc,"90°C",w-36,h-15); }
        }
        void Line(List<Point?> points,string color,bool area)
        {
            var pen=new Pen(Brush(color),Compact?(Kind=="Memory"?2.5:1.7):2.5){LineJoin=PenLineJoin.Round};
            Point? previous=null;
            foreach(var point in points) { if(point.HasValue && previous.HasValue) dc.DrawLine(pen,previous.Value,point.Value); previous=point; }
            var end=points.LastOrDefault(); if(end.HasValue&&!Compact) dc.DrawEllipse(Brush(color),null,end.Value,3.5,3.5);
        }
        if(Kind=="Curve") {
            if(Firmware) { Label(dc,"기기 펌웨어가 자동 제어",left+16,top+ph*.45,"#A6B9C4",13); return; }
            var points=Curve.Select((v,i)=>(Point?)Pt(i/5.0,v)).ToList(); Line(points,"#D5B2FF",false);
            foreach(var p in points) dc.DrawEllipse(Brush("#D5B2FF"),BrushPen(),p!.Value,4,4);
            if(CurrentTemp.HasValue) { double x=Math.Clamp((CurrentTemp.Value-40)/50,0,1); var p=Pt(x,FanPolicy.Interpolate(CurrentTemp.Value,Curve)); dc.DrawEllipse(Brush("#5BE3C0"),null,p,5,5); }
            return;
        }
        var samples=Compact?Samples.TakeLast(60).ToList():Samples;
        void Series(Func<Sample,double?> get,string color) {
            var now=samples.Count>0?samples[^1].Time:DateTime.Now;
            var points=samples.Select((s,i)=>{ double? v=get(s); double x=1-(now-s.Time).TotalSeconds/(Compact?60:span); return v.HasValue&&double.IsFinite(v.Value)&&x>=0?(Point?)Pt(x,v.Value):null; }).ToList();
            Line(points,color,true);
        }
        switch(Kind) { case "Cpu":Series(s=>s.CpuTemp,"#5BE3C0");break; case "Gpu":Series(s=>s.GpuTemp,"#8FAEFF");break; case "Fan":Series(s=>s.Rpm,"#D5B2FF");break; case "Power":Series(s=>s.Power,"#FFCB75");break; case "Memory":Series(s=>s.Memory,"#FFCB75");break; default:Series(s=>s.CpuTemp,"#5BE3C0");Series(s=>s.GpuTemp,"#8FAEFF");break; }
        if(!Compact&&!samples.Any(s=>s.CpuTemp.HasValue||s.GpuTemp.HasValue)) Label(dc,"센서 연결 후 실시간 그래프가 표시됩니다",left+24,top+ph*.48,"#8199A9",13);
    }
    static Pen BrushPen()=>new(Brush("#14232E"),2);
}
