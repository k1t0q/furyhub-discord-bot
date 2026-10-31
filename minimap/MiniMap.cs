using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace ScumPositionProbe {
 public static class MapMath {
  public const double Left=619646.8573,Right=-905369.0266,Top=619659.7258,Bottom=-904357.527;
  public static double NormX(double x){return (Left-x)/(Left-Right);}public static double NormY(double y){return (Top-y)/(Top-Bottom);}
  public static double NormalizeAngle(double a){return (a%360+360)%360;}
  public static double DeltaAngle(double a,double b){return (b-a+540)%360-180;}
  public static string Direction(double a){string[] names={"Север","СВ","Восток","ЮВ","Юг","ЮЗ","Запад","СЗ"};return names[(int)Math.Floor((NormalizeAngle(a)+22.5)/45)%8];}
  public static string Sector(double x,double y){if(x<0||x>=1||y<0||y>=1)return "ВНЕ КАРТЫ";return "DCBAZ"[(int)(y*5)].ToString()+"43210"[(int)(x*5)];}
 }
 public sealed class MiniMapForm:Form {
  public Position Current;public List<HubMarker> Markers=new List<HubMarker>();public DateTime LastPosition=DateTime.MinValue,LastMarkers=DateTime.MinValue;
  public double NorthYaw=90;public bool NorthCalibrated;public bool SyncEnabled;public bool ShowNames=true;
  public string SyncStatus="HUB не подключён",MotionStatus="F8 — начать";public bool Locked=true;
  Bitmap map;double renderedX=.5,renderedY=.5,renderedHeading;double radius=750;Point drag;bool dragging;
  readonly Timer animation=new Timer();
  [DllImport("user32.dll",EntryPoint="GetWindowLong")]static extern int GetWindowLong(IntPtr h,int n);
  [DllImport("user32.dll",EntryPoint="SetWindowLong")]static extern int SetWindowLong(IntPtr h,int n,int v);
  [DllImport("user32.dll")]static extern bool SetLayeredWindowAttributes(IntPtr window,uint color,byte alpha,uint flags);
  byte mapAlpha=255;
  public void SetMapOpacity(double value){mapAlpha=(byte)Math.Round(Math.Max(.25,Math.Min(1,value))*255);if(IsHandleCreated)SetLayeredWindowAttributes(Handle,0,mapAlpha,2);}
  protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);SetLayeredWindowAttributes(Handle,0,mapAlpha,2);}
  protected override CreateParams CreateParams{get{CreateParams cp=base.CreateParams;cp.ExStyle|=0x08000000|0x80000;if(Locked)cp.ExStyle|=0x20;return cp;}}
  protected override void WndProc(ref Message m){if(Locked&&m.Msg==0x0084){m.Result=new IntPtr(-1);return;}base.WndProc(ref m);}
  protected override bool ShowWithoutActivation{get{return true;}}
  public MiniMapForm(){Text="SCUM MINIMAP";FormBorderStyle=FormBorderStyle.None;ClientSize=new Size(350,350);StartPosition=FormStartPosition.Manual;Location=new Point(25,120);TopMost=true;ShowInTaskbar=false;BackColor=Color.FromArgb(10,16,20);DoubleBuffered=true;
   using(Stream embedded=typeof(MiniMapForm).Assembly.GetManifestResourceStream("ScumPositionProbe.map.jpg")){if(embedded!=null){using(Image image=Image.FromStream(embedded))map=new Bitmap(image);}else{string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"map.jpg");if(File.Exists(path)){using(Image image=Image.FromFile(path))map=new Bitmap(image);}}}
   animation.Interval=50;animation.Tick+=delegate{if(!dragging)Invalidate();};animation.Start();VisibleChanged+=delegate{if(Visible)animation.Start();else animation.Stop();};
   MouseWheel+=delegate(object sender,MouseEventArgs e){radius=Math.Max(150,Math.Min(7600,radius*(e.Delta>0 ? .8 : 1.25)));Invalidate();};
   MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button!=MouseButtons.Left||Locked)return;drag=e.Location;dragging=true;Capture=true;};
   MouseMove+=delegate(object sender,MouseEventArgs e){if(dragging&&e.Button==MouseButtons.Left)Location=new Point(Left+e.X-drag.X,Top+e.Y-drag.Y);};MouseUp+=delegate{dragging=false;Capture=false;};MouseCaptureChanged+=delegate{if(!Capture)dragging=false;};
   FormClosing+=delegate(object sender,FormClosingEventArgs e){if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();return;}animation.Stop();if(map!=null)map.Dispose();};
  }
  public void ToggleLock(){Locked=!Locked;if(IsHandleCreated){int ex=GetWindowLong(Handle,-20);SetWindowLong(Handle,-20,Locked?ex|0x20|0x08000000:(ex&~0x20)|0x08000000);}Invalidate();}
  public void SetPosition(Position p){Current=p;LastPosition=DateTime.UtcNow;{renderedX=MapMath.NormX(p.X);renderedY=MapMath.NormY(p.Y);renderedHeading=p.HasYaw?MapMath.NormalizeAngle(p.Yaw-NorthYaw):0;}Invalidate();if(Visible)Update();}
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBilinear;
   using(SolidBrush bg=new SolidBrush(Color.FromArgb(14,22,27)))g.FillRectangle(bg,0,0,Width,Height);
   int size=Math.Min(Width,Height);Rectangle field=new Rectangle(0,0,size,size);float midX=field.Left+size/2f,midY=field.Top+size/2f;
   double nx=Current==null ? .5:MapMath.NormX(Current.X),ny=Current==null ? .5:MapMath.NormY(Current.Y);double age=(DateTime.UtcNow-LastPosition).TotalSeconds;
   double span=radius*200/(MapMath.Left-MapMath.Right);if(Current==null)span=1;
   double left=renderedX-span/2,top=renderedY-span/2;
   GraphicsState clip=g.Save();g.SetClip(field);
   if(map!=null){
    double x0=Math.Max(0,left),y0=Math.Max(0,top),x1=Math.Min(1,left+span),y1=Math.Min(1,top+span);
    if(x1>x0&&y1>y0){RectangleF source=new RectangleF((float)(x0*map.Width),(float)(y0*map.Height),(float)((x1-x0)*map.Width),(float)((y1-y0)*map.Height));RectangleF dest=new RectangleF((float)(field.Left+(x0-left)/span*size),(float)(field.Top+(y0-top)/span*size),(float)((x1-x0)/span*size),(float)((y1-y0)/span*size));g.DrawImage(map,dest,source,GraphicsUnit.Pixel);}
   }
   else using(SolidBrush b=new SolidBrush(Color.FromArgb(24,42,48)))g.FillRectangle(b,field);
   using(SolidBrush dim=new SolidBrush(Color.FromArgb(30,0,0,0)))g.FillRectangle(dim,field);
   using(Pen grid=new Pen(Color.FromArgb(105,230,237,240),1))for(int k=0;k<=5;k++){float x=(float)(field.Left+(k/5.0-left)/span*size),y=(float)(field.Top+(k/5.0-top)/span*size);g.DrawLine(grid,x,field.Top,x,field.Bottom);g.DrawLine(grid,field.Left,y,field.Right,y);}
   foreach(HubMarker marker in Markers){float x=(float)(field.Left+(marker.X-left)/span*size),y=(float)(field.Top+(marker.Y-top)/span*size);if(x<field.Left-20||x>field.Right+20||y<field.Top-20||y>field.Bottom+20)continue;
    using(SolidBrush b=new SolidBrush(marker.Color))g.FillEllipse(b,x-4,y-4,8,8);using(Pen p=new Pen(Color.FromArgb(230,5,8,10),2))g.DrawEllipse(p,x-4,y-4,8,8);
    if(ShowNames){using(Font f=new Font("Segoe UI",8,FontStyle.Bold)){SizeF text=g.MeasureString(marker.Name,f);using(SolidBrush b=new SolidBrush(Color.FromArgb(190,5,10,14)))g.FillRectangle(b,x+7,y-7,text.Width+4,text.Height);g.DrawString(marker.Name,f,Brushes.White,x+9,y-7);}}
   }
   if(Current!=null){GraphicsState arrow=g.Save();g.TranslateTransform(midX,midY);g.RotateTransform((float)renderedHeading);PointF[] vertices={new PointF(0,-14),new PointF(10,10),new PointF(0,5),new PointF(-10,10)};
    using(SolidBrush b=new SolidBrush(age>3?Color.FromArgb(240,164,68):Color.FromArgb(97,235,244)))g.FillPolygon(b,vertices);using(Pen p=new Pen(Color.FromArgb(12,25,32),2))g.DrawPolygon(p,vertices);g.Restore(arrow);
   }
   g.Restore(clip);
   string heading=Current==null?"Нет позиции":Current.HasYaw?MapMath.Direction(renderedHeading)+" · "+MapMath.NormalizeAngle(renderedHeading).ToString("0")+"°":"Нет угла";
   string sector=Current==null?"":MapMath.Sector(nx,ny);
   string status=age>3&&Current!=null?" · "+age.ToString("0")+" с назад":!NorthCalibrated?" · настрой север":"";
   if(SyncEnabled&&(DateTime.UtcNow-LastMarkers).TotalSeconds>15)status+=" · HUB: нет связи";
   using(Font f=new Font("Segoe UI",9,FontStyle.Bold)){
    using(SolidBrush b=new SolidBrush(Color.FromArgb(175,5,10,14)))g.FillRectangle(b,0,Height-25,Width,25);
    TextRenderer.DrawText(g,heading+status,f,new Rectangle(6,Height-21,Math.Max(1,Width-53),20),age>3&&Current!=null?Color.Orange:Color.White,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding);
    SizeF label=g.MeasureString(sector,f);g.DrawString(sector,f,Brushes.White,Width-label.Width-6,Height-21);
   }
  }
  sealed class StopwatchAdapter {readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();double prev;public double Step(){double n=clock.Elapsed.TotalSeconds,d=n-prev;prev=n;return d;}}
 }
}
