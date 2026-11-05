using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ScumPositionProbe {
 public static class RoundedUi {
  public static GraphicsPath Path(RectangleF rect,float radius){GraphicsPath path=new GraphicsPath();float diameter=Math.Max(1,Math.Min(radius*2,Math.Min(rect.Width,rect.Height)));path.AddArc(rect.Left,rect.Top,diameter,diameter,180,90);path.AddArc(rect.Right-diameter,rect.Top,diameter,diameter,270,90);path.AddArc(rect.Right-diameter,rect.Bottom-diameter,diameter,diameter,0,90);path.AddArc(rect.Left,rect.Bottom-diameter,diameter,diameter,90,90);path.CloseFigure();return path;}
  public static void Clip(Control control,int radius=8){if(control.Width<2||control.Height<2)return;using(GraphicsPath path=Path(new RectangleF(0,0,control.Width,control.Height),radius)){Region old=control.Region;control.Region=new Region(path);if(old!=null)old.Dispose();}}
  [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int length);
  public static void DarkTitle(Form form){if(ProbeForm.UiTest||Environment.OSVersion.Platform!=PlatformID.Win32NT)return;try{int enabled=1;if(DwmSetWindowAttribute(form.Handle,20,ref enabled,4)!=0)DwmSetWindowAttribute(form.Handle,19,ref enabled,4);int caption=0x002f2820,text=0x00f3f1ed,corners=2;DwmSetWindowAttribute(form.Handle,35,ref caption,4);DwmSetWindowAttribute(form.Handle,36,ref text,4);DwmSetWindowAttribute(form.Handle,33,ref corners,4);}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}}
 }
 public sealed class RoundedButton:Button {
  bool hover,pressed;
  public RoundedButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
  protected override void OnResize(EventArgs e){base.OnResize(e);RoundedUi.Clip(this);}
  protected override void OnMouseEnter(EventArgs e){hover=true;base.OnMouseEnter(e);Invalidate();}
  protected override void OnMouseLeave(EventArgs e){hover=pressed=false;base.OnMouseLeave(e);Invalidate();}
  protected override void OnMouseDown(MouseEventArgs e){pressed=true;base.OnMouseDown(e);Invalidate();}
  protected override void OnMouseUp(MouseEventArgs e){pressed=false;base.OnMouseUp(e);Invalidate();}
  protected override void OnPaint(PaintEventArgs e){
   e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(Parent==null?BackColor:Parent.BackColor);Color fill=BackColor;if(Enabled&&hover)fill=FlatAppearance.MouseOverBackColor;if(Enabled&&pressed)fill=ControlPaint.Dark(fill,.12f);
   using(GraphicsPath path=RoundedUi.Path(new RectangleF(.5f,.5f,Math.Max(1,Width-1),Math.Max(1,Height-1)),8)){using(SolidBrush brush=new SolidBrush(fill))e.Graphics.FillPath(brush,path);if(FlatAppearance.BorderSize>0)using(Pen pen=new Pen(FlatAppearance.BorderColor))e.Graphics.DrawPath(pen,path);}
   TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Enabled?ForeColor:Color.FromArgb(105,119,129),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
   if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-5,-5),ForeColor,fill);
  }
 }
 public sealed class RoundedPanel:Panel {
  public RoundedPanel(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
  protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(Parent==null?BackColor:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(GraphicsPath path=RoundedUi.Path(new RectangleF(0,0,Width,Height),9))using(SolidBrush brush=new SolidBrush(BackColor))e.Graphics.FillPath(brush,path);}
 }
 // Fully painted selector: no native DarkComboBox chrome or native list scrollbar.
 public sealed class DarkComboBox:Control {
  public sealed class ItemCollection:System.Collections.ArrayList {
   readonly DarkComboBox owner;public ItemCollection(DarkComboBox owner){this.owner=owner;}
   public override void Clear(){base.Clear();owner.SelectedIndex=-1;owner.Invalidate();}
  }
  public readonly ItemCollection Items;int selected=-1;ToolStripDropDown popup;SelectorList list;bool ownerPressDismissed;long ownerDismissedAt;
  public string EmptyText="Выбери значение";
  public Predicate<object> IsSelectable=item=>true;
  public ComboBoxStyle DropDownStyle{get;set;}public DrawMode DrawMode{get;set;}public FlatStyle FlatStyle{get;set;}
  public bool IntegralHeight{get;set;}public int MaxDropDownItems{get;set;}public int ItemHeight{get;set;}public int DropDownHeight{get;set;}
  public int DropDownWidth{get{return Width;}set{}}
  public int SelectedIndex{get{return selected;}set{if(value< -1||value>=Items.Count)throw new ArgumentOutOfRangeException("value");if(selected==value)return;selected=value;Invalidate();if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);}}
  public object SelectedItem{get{return selected<0||selected>=Items.Count?null:Items[selected];}}
  public bool DroppedDown{get{return popup!=null&&popup.Visible;}set{if(value)Open();else if(popup!=null)popup.Close();}}
  public event EventHandler SelectedIndexChanged,DropDown;public event DrawItemEventHandler DrawItem;
  public DarkComboBox(){Items=new ItemCollection(this);SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);TabStop=true;Cursor=Cursors.Hand;BackColor=Color.FromArgb(17,25,30);ForeColor=Color.FromArgb(237,241,243);ItemHeight=25;DropDownHeight=240;MaxDropDownItems=10;}
  public void RefreshRows(){Invalidate();if(list!=null&&!list.IsDisposed&&DroppedDown)list.Invalidate();}
  public void BeginUpdate(){SuspendLayout();}public void EndUpdate(){ResumeLayout();Invalidate();}
  protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);RoundedUi.Clip(this,8);}
  protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);if(!Enabled)DroppedDown=false;Invalidate();}
  protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
  protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(Enabled&&e.Button==MouseButtons.Left){if(ownerPressDismissed){ownerPressDismissed=false;if(Stopwatch.GetTimestamp()-ownerDismissedAt<Stopwatch.Frequency/2)return;}Focus();DroppedDown=!DroppedDown;}}
  protected override bool IsInputKey(Keys key){return key==Keys.Up||key==Keys.Down||base.IsInputKey(key);}
  protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.F4||e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter||(e.Alt&&e.KeyCode==Keys.Down)){DroppedDown=!DroppedDown;e.Handled=true;}else if(e.KeyCode==Keys.Up||e.KeyCode==Keys.Down){Step(e.KeyCode==Keys.Down?1:-1);e.Handled=true;}}
  void Step(int direction){int i=SelectedIndex;do{i+=direction;}while(i>=0&&i<Items.Count&&!IsSelectable(Items[i]));if(i>=0&&i<Items.Count)SelectedIndex=i;}
  internal void PaintRow(Graphics graphics,Rectangle bounds,int index,bool hot,bool closed){
   Color background=hot?Color.FromArgb(43,59,69):BackColor;using(SolidBrush brush=new SolidBrush(background))graphics.FillRectangle(brush,bounds);
   DrawItemState state=closed?DrawItemState.ComboBoxEdit:hot?DrawItemState.Selected:DrawItemState.None;
   DrawItemEventArgs args=new DrawItemEventArgs(graphics,Font,bounds,index,state,ForeColor,background);
   if(DrawItem!=null)DrawItem(this,args);else TextRenderer.DrawText(graphics,Items[index].ToString(),Font,Rectangle.Inflate(bounds,-8,0),Enabled?ForeColor:Color.FromArgb(128,143,151),TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
  }
  protected override void OnPaint(PaintEventArgs e){
   e.Graphics.Clear(BackColor);Rectangle text=new Rectangle(8,1,Math.Max(1,Width-38),Height-2);Color ink=Enabled?ForeColor:Color.FromArgb(128,143,151);
   if(SelectedIndex>=0)PaintRow(e.Graphics,text,SelectedIndex,false,true);else TextRenderer.DrawText(e.Graphics,EmptyText,Font,text,ink,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
   e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;float x=Width-17,y=Height/2f;using(Pen pen=new Pen(ink,1.5f))e.Graphics.DrawLines(pen,new PointF[]{new PointF(x-4,y-2),new PointF(x,y+2),new PointF(x+4,y-2)});
   using(GraphicsPath path=RoundedUi.Path(new RectangleF(.5f,.5f,Width-1,Height-1),8))using(Pen pen=new Pen(Focused?Color.FromArgb(100,128,145):Color.FromArgb(43,54,63)))e.Graphics.DrawPath(pen,path);
  }
  void Open(){
   if(!Enabled||Items.Count==0)return;if(DropDown!=null)DropDown(this,EventArgs.Empty);if(!Enabled||Items.Count==0)return;
   if(popup!=null)popup.Dispose();popup=new RoundedPopup{AutoSize=false,Padding=Padding.Empty,Margin=Padding.Empty,BackColor=BackColor,DropShadowEnabled=false};
   int height=Math.Min(DropDownHeight,Items.Count*ItemHeight+12);list=new SelectorList(this);list.Size=new Size(Width,height);
   ToolStripControlHost host=new ToolStripControlHost(list){AutoSize=false,Size=list.Size,Margin=Padding.Empty,Padding=Padding.Empty};popup.Items.Add(host);popup.Size=list.Size;RoundedUi.Clip(popup,9);
   popup.Closing+=delegate(object sender,ToolStripDropDownClosingEventArgs e){if(e.CloseReason==ToolStripDropDownCloseReason.AppClicked&&RectangleToScreen(ClientRectangle).Contains(Cursor.Position)){ownerPressDismissed=true;ownerDismissedAt=Stopwatch.GetTimestamp();}};popup.Closed+=delegate{Invalidate();};popup.Show(this,new Point(0,Height+4));list.Focus();list.Invalidate();
  }
  internal void TestScrollInput(){if(list==null||!DroppedDown)throw new Exception("Scroll test needs an open list");list.TestScrollBurst();}
  internal void RenderOpenPopup(Bitmap target){if(popup==null||!popup.Visible)return;using(Bitmap image=new Bitmap(popup.Width,popup.Height)){popup.DrawToBitmap(image,popup.ClientRectangle);using(Graphics g=Graphics.FromImage(target)){Form form=FindForm();Point location=new Point(popup.Left-form.Left,popup.Top-form.Top);g.DrawImageUnscaled(image,location);}}}
  protected override void Dispose(bool disposing){if(disposing&&popup!=null){popup.Dispose();popup=null;}base.Dispose(disposing);}
  sealed class RoundedPopup:ToolStripDropDown {
   protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(BackColor);}
   protected override void OnPaint(PaintEventArgs e){}
  }
  sealed class SelectorList:Control {
   readonly DarkComboBox owner;int offset,hot=-1;bool dragging;int dragY,dragOffset;readonly Dictionary<int,Bitmap> normalRows=new Dictionary<int,Bitmap>(),hotRows=new Dictionary<int,Bitmap>();
   Bitmap RowImage(int index,bool selected){Dictionary<int,Bitmap> cache=selected?hotRows:normalRows;Bitmap image;if(cache.TryGetValue(index,out image))return image;image=new Bitmap(Width-(Scrolls?18:8),owner.ItemHeight);using(Graphics graphics=Graphics.FromImage(image))owner.PaintRow(graphics,new Rectangle(0,0,image.Width,image.Height),index,selected,false);cache[index]=image;return image;}
   void RedrawNow(){Invalidate();}
   protected override void Dispose(bool disposing){if(disposing){foreach(Bitmap image in normalRows.Values)image.Dispose();foreach(Bitmap image in hotRows.Values)image.Dispose();}base.Dispose(disposing);}
   int ContentHeight{get{return owner.Items.Count*owner.ItemHeight;}}int ViewHeight{get{return Height-12;}}bool Scrolls{get{return ContentHeight>ViewHeight;}}
   public SelectorList(DarkComboBox owner){this.owner=owner;BackColor=owner.BackColor;ForeColor=owner.ForeColor;Font=owner.Font;TabStop=true;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable,true);}
   Rectangle Thumb(){int track=Height-16;int h=Math.Max(24,track*ViewHeight/Math.Max(1,ContentHeight));return new Rectangle(Width-9,8+(track-h)*offset/Math.Max(1,ContentHeight-ViewHeight),4,h);}
   int Row(int y){return (y-6+offset)/owner.ItemHeight;}
   void Scroll(int amount){int next=Math.Max(0,Math.Min(Math.Max(0,ContentHeight-ViewHeight),offset+amount));if(next==offset)return;offset=next;Point mouse=PointToClient(Cursor.Position);hot=ClientRectangle.Contains(mouse)?Row(mouse.Y):-1;RedrawNow();}
   protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(BackColor);for(int i=Math.Max(0,offset/owner.ItemHeight);i<owner.Items.Count;i++){int y=6+i*owner.ItemHeight-offset;if(y>=Height-6)break;if(y+owner.ItemHeight>=e.ClipRectangle.Top&&y<=e.ClipRectangle.Bottom)e.Graphics.DrawImageUnscaled(RowImage(i,i==hot),4,y);}e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;if(Scrolls)using(SolidBrush brush=new SolidBrush(Color.FromArgb(90,108,119)))e.Graphics.FillRectangle(brush,Thumb());using(GraphicsPath path=RoundedUi.Path(new RectangleF(.5f,.5f,Width-1,Height-1),9))using(Pen pen=new Pen(Color.FromArgb(65,82,94)))e.Graphics.DrawPath(pen,path);}
   internal void TestScrollBurst(){
    int expected=Math.Min(ContentHeight-ViewHeight,owner.ItemHeight*3);OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,20,30,-120));if(offset!=expected)throw new Exception("Wheel input waited for a timer instead of updating immediately");
    for(int i=0;i<100;i++)OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,20,30,-120));if(offset!=ContentHeight-ViewHeight)throw new Exception("Fast scroll burst lost input or overshot the bottom");
    for(int i=0;i<100;i++)OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,20,30,120));if(offset!=0)throw new Exception("Reverse scroll burst did not return to the top");
    OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,20,30,-30));if(offset<=0)throw new Exception("High-resolution wheel delta was discarded");Scroll(-offset);
   }
   protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);Scroll((int)Math.Round(-e.Delta*owner.ItemHeight*3/120.0));}
   protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging){int track=Math.Max(1,Height-16-Thumb().Height);int next=Math.Max(0,Math.Min(ContentHeight-ViewHeight,dragOffset+(e.Y-dragY)*(ContentHeight-ViewHeight)/track));if(next!=offset){offset=next;RedrawNow();}}else{int next=e.Y<6||e.Y>=Height-6?-1:Row(e.Y);if(next!=hot){int previous=hot;hot=next;if(previous>=0)Invalidate(new Rectangle(4,6+previous*owner.ItemHeight-offset,Width-18,owner.ItemHeight));if(hot>=0)Invalidate(new Rectangle(4,6+hot*owner.ItemHeight-offset,Width-18,owner.ItemHeight));}}}
   protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hot=-1;Invalidate();}
   protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;if(Scrolls&&e.X>=Width-16){if(Thumb().Contains(e.Location)){dragging=true;dragY=e.Y;dragOffset=offset;Capture=true;}else Scroll(e.Y<Thumb().Top?-ViewHeight:ViewHeight);return;}int i=Row(e.Y);if(e.Y>=6&&i>=0&&i<owner.Items.Count&&owner.IsSelectable(owner.Items[i])){owner.SelectedIndex=i;owner.DroppedDown=false;owner.Focus();}}
   protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);dragging=false;Capture=false;}
   protected override bool IsInputKey(Keys key){return key==Keys.Up||key==Keys.Down||key==Keys.Home||key==Keys.End||base.IsInputKey(key);}
   protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Escape||e.KeyCode==Keys.Enter){owner.DroppedDown=false;owner.Focus();e.Handled=true;return;}if(e.KeyCode==Keys.Up||e.KeyCode==Keys.Down){owner.Step(e.KeyCode==Keys.Down?1:-1);int top=owner.SelectedIndex*owner.ItemHeight;if(top<offset)offset=top;if(top+owner.ItemHeight>offset+ViewHeight)offset=top+owner.ItemHeight-ViewHeight;hot=owner.SelectedIndex;RedrawNow();e.Handled=true;}}
  }
 }
 public sealed class DarkNumericButtons:NativeWindow {
  readonly Control child;readonly NumericUpDown number;
  public DarkNumericButtons(Control child,NumericUpDown number){this.child=child;this.number=number;child.HandleCreated+=delegate{AssignHandle(child.Handle);};child.HandleDestroyed+=delegate{ReleaseHandle();};if(child.IsHandleCreated)AssignHandle(child.Handle);}
  protected override void WndProc(ref Message m){base.WndProc(ref m);if(m.Msg!=0x000f&&m.Msg!=0x0317&&m.Msg!=0x0318)return;if(child.Width<1||child.Height<1)return;using(Graphics g=m.Msg==0x000f?Graphics.FromHwnd(Handle):Graphics.FromHdc(m.WParam)){using(SolidBrush fill=new SolidBrush(number.BackColor))g.FillRectangle(fill,child.ClientRectangle);g.SmoothingMode=SmoothingMode.AntiAlias;using(Pen pen=new Pen(Color.FromArgb(187,201,210),1.3f)){float x=child.Width/2f,top=child.Height*.28f,bottom=child.Height*.72f;g.DrawLines(pen,new PointF[]{new PointF(x-3,top+1),new PointF(x,top-2),new PointF(x+3,top+1)});g.DrawLines(pen,new PointF[]{new PointF(x-3,bottom-1),new PointF(x,bottom+2),new PointF(x+3,bottom-1)});}}}
 }
 public sealed class RoundedFieldFrame:Panel {
  readonly Control field;readonly List<DarkNumericButtons> steppers=new List<DarkNumericButtons>();
  public RoundedFieldFrame(Control input){field=input;BackColor=Color.FromArgb(17,25,30);SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);TextBox text=input as TextBox;if(text!=null)text.BorderStyle=BorderStyle.None;NumericUpDown number=input as NumericUpDown;if(number!=null)number.BorderStyle=BorderStyle.None;Controls.Add(field);if(number!=null)foreach(Control child in number.Controls)if(!(child is TextBox))steppers.Add(new DarkNumericButtons(child,number));field.Enter+=delegate{Invalidate();};field.Leave+=delegate{Invalidate();};}
  protected override void OnResize(EventArgs e){base.OnResize(e);RoundedUi.Clip(this,7);if(field==null)return;field.SetBounds(8,0,Math.Max(1,Width-16),Height);field.Top=Math.Max(0,(Height-field.Height)/2);}
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(GraphicsPath path=RoundedUi.Path(new RectangleF(.5f,.5f,Math.Max(1,Width-1),Math.Max(1,Height-1)),7))using(Pen pen=new Pen(field.ContainsFocus?Color.FromArgb(100,128,145):Color.FromArgb(43,54,63)))e.Graphics.DrawPath(pen,path);}
 }
 public sealed class ZoomKeys {
  bool held;long repeatAt;
  public int Update(bool allowed,bool up,bool down,long now){if(!allowed||up==down){held=false;return 0;}if(held&&now<repeatAt)return 0;repeatAt=now+(held?150:400);held=true;return up?1:-1;}
 }
 public static class MapVisibility {public static bool ShouldShow(bool wanted,bool menuHidden,bool allowedFocus){return wanted&&!menuHidden&&allowedFocus;}}
 public static class UserSettings {
  public static string PathFor(string name){string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SCUM HUB");Directory.CreateDirectory(root);string path=Path.Combine(root,name);string old=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name);if(!File.Exists(path)&&File.Exists(old))File.Copy(old,path,false);return path;}
 }
 public sealed class Position {
  public double X,Y,Z,Yaw;public bool HasYaw;
  public static readonly Regex Pattern=new Regex(@"^\s*\{?\s*X\s*=\s*([+\-]?(?:\d+(?:[.,]\d*)?|[.,]\d+)(?:[eE][+\-]?\d+)?)\s+Y\s*=\s*([+\-]?(?:\d+(?:[.,]\d*)?|[.,]\d+)(?:[eE][+\-]?\d+)?)\s+Z\s*=\s*([+\-]?(?:\d+(?:[.,]\d*)?|[.,]\d+)(?:[eE][+\-]?\d+)?)(?:\s*\|[^\r\n{}]*)?\s*\}?\s*$",RegexOptions.CultureInvariant);
  public static bool TryParse(string input,out Position position) {
   position=null;if(String.IsNullOrEmpty(input)||input.Length>1024)return false;
   Match m=Pattern.Match(input);if(!m.Success)return false;
   double[] values=new double[3];
   for(int i=0;i<3;i++)if(!Double.TryParse(m.Groups[i+1].Value.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out values[i])||Double.IsNaN(values[i])||Double.IsInfinity(values[i])||Math.Abs(values[i])>2000000)return false;
   position=new Position{X=values[0],Y=values[1],Z=values[2]};
   Match rotation=Regex.Match(input,@"\|[^\r\n{}]*?\bY\s*=\s*([+\-]?(?:\d+(?:[.,]\d*)?|[.,]\d+)(?:[eE][+\-]?\d+)?)",RegexOptions.CultureInvariant);
   double yaw;if(rotation.Success&&Double.TryParse(rotation.Groups[1].Value.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out yaw)&&!Double.IsInfinity(yaw)&&!Double.IsNaN(yaw)&&Math.Abs(yaw)<10000){position.Yaw=MapMath.NormalizeAngle(yaw);position.HasYaw=true;}return true;
  }
  public string Number(double n){return n.ToString("0.###",CultureInfo.InvariantCulture);}
  public string Display(){return "X  "+Number(X)+"\r\nY  "+Number(Y)+"\r\nZ  "+Number(Z);}
 }
 public static class Native {
  [DllImport("user32.dll",CharSet=CharSet.Auto)] public static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr wParam,IntPtr lParam);
  [StructLayout(LayoutKind.Sequential)] public struct CURSORINFO {public int cbSize;public int flags;public IntPtr hCursor;public int x,y;}
  [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO info);
  public static bool CursorVisible(){CURSORINFO info=new CURSORINFO();info.cbSize=Marshal.SizeOf(typeof(CURSORINFO));return GetCursorInfo(ref info)&&(info.flags&1)!=0;}
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public INPUTUNION u; }
  [StructLayout(LayoutKind.Explicit)] public struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public HARDWAREINPUT hi; }
  [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT {public ushort wVk,wScan;public uint dwFlags,time;public UIntPtr dwExtraInfo;}
  [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT {public int dx,dy;public uint mouseData,dwFlags,time;public UIntPtr dwExtraInfo;}
  [StructLayout(LayoutKind.Sequential)] public struct HARDWAREINPUT {public uint uMsg;public ushort wParamL,wParamH;}
  [DllImport("user32.dll",SetLastError=true)] public static extern uint SendInput(uint count,INPUT[] inputs,int size);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
  [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
  [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
  [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr handle);
  [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr handle);
  [DllImport("user32.dll",SetLastError=true)] public static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
  [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr window,int id);
  public static bool IsScumName(string name){return String.Equals(name,"SCUM",StringComparison.OrdinalIgnoreCase)||String.Equals(name,"SCUM-Win64-Shipping",StringComparison.OrdinalIgnoreCase);}
  public static bool Down(int key){return (GetAsyncKeyState(key)&0x8000)!=0;}
  public static INPUT Key(ushort key,bool up){return new INPUT{type=1,u=new INPUTUNION{ki=new KEYBDINPUT{wVk=key,dwFlags=up?2u:0u}}};}
  public static bool Send(INPUT[] keys){return SendInput((uint)keys.Length,keys,Marshal.SizeOf(typeof(INPUT)))==(uint)keys.Length;}
 }
 public sealed class ProbeForm:Form {
  internal static bool UiTest;
  readonly AllyInbox allyInbox=new AllyInbox();AllyLive live;readonly CheckBox sharePosition=new CheckBox();
  readonly MiniMapForm mini=new MiniMapForm();HubClient hub;int hubGeneration;bool syncing;readonly Button refreshMarkers=new RoundedButton();
  readonly TextBox hubNick=new TextBox(),hubCode=new TextBox();readonly DarkComboBox hubRole=new DarkComboBox();readonly CheckBox adminLogin=new CheckBox();readonly Label hubState=new Label();readonly Button hubLogin=new RoundedButton();
  void BuildHubUI(){
   MakeLabel("МЕТКИ SCUM HUB · обновление по кнопке",20,562,550,25,10,true);
   MakeLabel("Ник",20,593,130,20,9,false);MakeLabel("Роль",170,593,130,20,9,false);MakeLabel("Код входа",325,593,240,20,9,false);
   hubNick.SetBounds(20,615,135,27);hubNick.Text="k1t0";Controls.Add(hubNick);
   hubRole.SetBounds(170,615,140,27);hubRole.DropDownStyle=ComboBoxStyle.DropDownList;hubRole.Items.AddRange(new object[]{"Rifler","Sniper","Bomber","Locker","Builder","Helper"});hubRole.SelectedIndex=0;Controls.Add(hubRole);
   hubCode.SetBounds(325,615,245,27);hubCode.UseSystemPasswordChar=false;Controls.Add(hubCode);
   adminLogin.SetBounds(20,649,550,24);adminLogin.Text="Личный вход администратора (код тот же, что на сайте)";adminLogin.Checked=true;Controls.Add(adminLogin);
   hubLogin.SetBounds(20,682,170,34);hubLogin.Text="Подключить HUB";StyleButton(hubLogin);hubLogin.Click+=async delegate{await LoginHub();};Controls.Add(hubLogin);
   Button logout=new RoundedButton();logout.SetBounds(200,682,110,34);logout.Text="Отключить";StyleButton(logout);logout.Click+=delegate{DisconnectHub();};Controls.Add(logout);
   hubState.SetBounds(20,722,550,40);hubState.Text="Вводи код здесь, не присылай его в чат. Код не сохраняется.";Controls.Add(hubState);
   Button show=new RoundedButton();show.SetBounds(20,770,170,34);show.Text="Карта · F7";StyleButton(show);show.Click+=delegate{ToggleMini();};Controls.Add(show);
   Button locked=new RoundedButton();locked.SetBounds(200,770,170,34);locked.Text="Сквозь клики · F7";StyleButton(locked);locked.Click+=delegate{mini.ToggleLock();};Controls.Add(locked);
   Button north=new RoundedButton();north.SetBounds(380,770,190,34);north.Text="Смотрю на север · F11";StyleButton(north);north.Click+=delegate{CalibrateNorth();};Controls.Add(north);
   refreshMarkers.Text="Обновить метки";StyleButton(refreshMarkers);refreshMarkers.Enabled=false;refreshMarkers.Click+=async delegate{await RefreshHub();};Controls.Add(refreshMarkers);
   LoadCalibration();Shown+=delegate{if(UiTest)return;uint pid;IntPtr win;bool game=IsGame(out pid,out win);focusAllowed=game||pid==appPid;ApplyVisibility();};
  }
  void LoadCalibration(){try{string f=UserSettings.PathFor("north.txt");double v;if(File.Exists(f)&&Double.TryParse(File.ReadAllText(f),NumberStyles.Float,CultureInfo.InvariantCulture,out v)&&!Double.IsInfinity(v)&&!Double.IsNaN(v)){mini.NorthYaw=MapMath.NormalizeAngle(v);mini.NorthCalibrated=true;}}catch{}}
  void CalibrateNorth(){if(previous==null||!previous.HasYaw||lastSuccess<0||clock.ElapsedMilliseconds-lastSuccess>4000){state.Text="Для калибровки нужны свежие координаты с углом Yaw. Смотри на север и нажми F11 в игре.";return;}mini.NorthYaw=previous.Yaw;mini.NorthCalibrated=true;try{File.WriteAllText(UserSettings.PathFor("north.txt"),previous.Yaw.ToString(CultureInfo.InvariantCulture));}catch{}state.Text="Север откалиброван по текущему направлению.";}
  bool mapWanted=true,menuCursor,autoHidden,focusAllowed;readonly uint appPid=(uint)Process.GetCurrentProcess().Id;long cursorChangedAt;bool lastCursor;readonly CheckBox autoHide=new CheckBox();
  void ToggleMini(){mapWanted=!mapWanted;ApplyVisibility();}
  void ApplyVisibility(){bool show=MapVisibility.ShouldShow(mapWanted,autoHidden,focusAllowed);if(show&&!mini.Visible)mini.Show();else if(!show&&mini.Visible)mini.Hide();}
  void UpdateMenuVisibility(bool active,long now){focusAllowed=active||cachedPid==appPid;ApplyVisibility();bool visible=active&&autoHide.Checked&&Native.CursorVisible();menuCursor=visible;if(visible!=lastCursor){lastCursor=visible;cursorChangedAt=now;}if(visible){ReleaseKeys();phase=0;nextAttempt=now+200;}if(now-cursorChangedAt>=(visible?100:180)){autoHidden=visible;ApplyVisibility();}}
  async System.Threading.Tasks.Task LoginHub(){
   if(String.IsNullOrWhiteSpace(hubCode.Text)){hubState.Text="Введи код входа HUB.";return;}
   int version=++hubGeneration;HubClient attempt=new HubClient();hubLogin.Enabled=false;hubState.Text="Подключаюсь к scum-hub.ru…";
   try{await attempt.Login(hubNick.Text.Trim(),null,hubCode.Text,String.Equals(hubNick.Text.Trim(),"k1t0",StringComparison.OrdinalIgnoreCase));HubSnapshot snapshot=await attempt.Snapshot();
    if(IsDisposed||version!=hubGeneration){attempt.Dispose();return;}if(hub!=null)hub.Dispose();hub=attempt;refreshMarkers.Enabled=true;StartAllies();mini.Markers=snapshot.Markers;UpdateNavigationMarkers();mini.LastMarkers=DateTime.UtcNow;mini.SyncEnabled=true;mini.SyncStatus="HUB · "+snapshot.Markers.Count+" меток";hubState.Text="Подключён: "+snapshot.Nick+" · "+snapshot.Markers.Count+" меток";
   }catch(Exception ex){attempt.Dispose();if(!IsDisposed&&version==hubGeneration)hubState.Text=ex.Message;}finally{if(!IsDisposed){hubCode.Clear();hubLogin.Enabled=true;UpdateConnectionLayout();}}
  }
  async System.Threading.Tasks.Task RefreshHub(){if(hub==null||syncing)return;syncing=true;refreshMarkers.Enabled=false;refreshMarkers.Text="Обновляю…";HubClient current=hub;int version=hubGeneration;
   try{HubSnapshot snapshot=await current.Snapshot();if(IsDisposed||version!=hubGeneration||current!=hub)return;mini.Markers=snapshot.Markers;UpdateNavigationMarkers();mini.LastMarkers=DateTime.UtcNow;mini.SyncEnabled=true;mini.SyncStatus="HUB · "+snapshot.Markers.Count+" меток";hubState.Text="HUB: "+snapshot.Nick+" · обновлено "+DateTime.Now.ToString("HH:mm:ss");}
   catch(HubExpiredException){if(!IsDisposed&&version==hubGeneration){DisconnectHub();hubState.Text="Сессия истекла или доступ отозван. Войди снова.";}}
   catch(Exception){if(!IsDisposed&&version==hubGeneration){mini.SyncStatus="HUB: нет связи · метки устарели";hubState.Text="Нет связи с HUB. Нажми «Обновить метки», чтобы повторить.";}}
   finally{syncing=false;if(!IsDisposed){refreshMarkers.Enabled=hub!=null;refreshMarkers.Text="Обновить метки";}}
  }
  void DisconnectHub(){if(mini.Navigation.GoalId!=null&&!mini.Navigation.GoalId.StartsWith("poi-",StringComparison.Ordinal))mini.Navigation.Cancel("Маршрут отменён: HUB отключён.");refreshMarkers.Enabled=false;if(live!=null){live.Dispose();live=null;}mini.Allies=new List<AllyPosition>();hubGeneration++;if(hub!=null){hub.Dispose();hub=null;}mini.Markers=new List<HubMarker>();UpdateNavigationMarkers();mini.SyncEnabled=false;mini.SyncStatus="HUB не подключён";hubCode.Clear();hubState.Text="HUB отключён. Объекты SCUM доступны.";UpdateConnectionLayout();}

  readonly Timer timer=new Timer();readonly Stopwatch clock=Stopwatch.StartNew();
  readonly Label state=new Label(),positionLabel=new Label(),metrics=new Label(),foregroundLabel=new Label();
  readonly Button toggle=new RoundedButton();readonly NumericUpDown interval=new NumericUpDown();readonly DarkComboBox mode=new DarkComboBox();
  readonly CheckBox top=new CheckBox();
  ushort copyKey=0x79;readonly Button keyPicker=new RoundedButton();bool movingWindow;
  bool running,ctrlOwned,keyOwned,hotkeysOk;long nextUi,nextFocus;uint cachedPid;IntPtr cachedWindow;bool cachedActive;ushort ownedKey;int phase;uint oldSequence,gamePid;
  IntPtr gameWindow=IntPtr.Zero;long nextAttempt,keyUpAt,clipboardUntil,lastSuccess=-1,focusSince=-1;
  int attempts,successes,changes,timeouts,blocked;Position previous;StreamWriter log;string logPath="";
  public ProbeForm(){
   Text="SCUM HUB";ClientSize=new Size(1010,820);MinimumSize=new Size(1026,859);AutoScroll=false;AutoScaleMode=AutoScaleMode.Dpi;
   DoubleBuffered=true;SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);
   if(!UiTest)using(Stream icon=typeof(ProbeForm).Assembly.GetManifestResourceStream("ScumPositionProbe.SH.ico")){if(icon!=null)Icon=new Icon(icon);}
   BackColor=Color.FromArgb(12,17,20);ForeColor=Color.FromArgb(235,239,239);Font=new Font("Segoe UI",10);
   MakeLabel("SCUM HUB · MINIMAP",20,15,550,35,17,true);
   MakeLabel("Без Ctrl · позиция из буфера / метки HUB: 5 секунд",20,54,550,25,10,false);
   state.SetBounds(20,90,550,45);state.ForeColor=Color.FromArgb(242,170,100);state.Text="Остановлено. F8 — включить / выключить.";Controls.Add(state);
   positionLabel.SetBounds(20,142,550,115);positionLabel.Font=new Font("Consolas",22,FontStyle.Bold);positionLabel.Text="X  —\r\nY  —\r\nZ  —";Controls.Add(positionLabel);
   foregroundLabel.SetBounds(20,267,550,26);Controls.Add(foregroundLabel);
   metrics.SetBounds(20,297,550,58);Controls.Add(metrics);
   MakeLabel("Интервал, мс",20,365,125,25,10,false);interval.SetBounds(147,362,100,28);interval.Minimum=100;interval.Maximum=5000;interval.Increment=100;interval.Value=100;Controls.Add(interval);
   MakeLabel("Копирование",265,365,100,25,10,false);mode.SetBounds(365,362,205,28);mode.DropDownStyle=ComboBoxStyle.DropDownList;mode.Items.Add("Без нажатий (буфер)");mode.Items.Add("F10 (если назначена в игре)");mode.SelectedIndex=0;Controls.Add(mode);
   toggle.SetBounds(20,408,170,42);toggle.Text="Старт · F8";StyleButton(toggle);toggle.Click+=delegate{Toggle();};Controls.Add(toggle);
   Button stop=new RoundedButton();stop.SetBounds(200,408,170,42);stop.Text="Стоп · F9";StyleButton(stop);stop.Click+=delegate{Stop("Остановлено.");};Controls.Add(stop);
   Button logs=new RoundedButton();logs.SetBounds(380,408,190,42);logs.Text="Открыть журнал";StyleButton(logs);logs.Click+=delegate{OpenLogs();};Controls.Add(logs);
   top.SetBounds(20,466,550,25);top.Text="Поверх остальных окон";top.Checked=false;TopMost=false;top.CheckedChanged+=delegate{TopMost=top.Checked;};Controls.Add(top);
   MakeLabel("Координаты остаются на ПК. HUB подключается только для чтения меток.\r\nБуфер заменяется координатами; закрой чат и меню игры.",20,504,550,50,9,false);
   BuildHubUI();int before=Controls.Count;BuildMapSettings();for(int i=before;i<Controls.Count;i++){Control c=Controls[i];c.Left=610+(c.Left-20)*380/550;c.Top-=732;c.Width=c.Width*380/550;}BuildOpacity();MinimalLayout();BuildKeyPicker();BuildAutoHide();BuildZoomUI();BuildSharing();BuildNavigationUI();BuildArrowSizes();BuildQuickMarkers();BuildCompactTabs();
   timer.Interval=25;timer.Tick+=delegate{Tick();};if(!UiTest)timer.Start();FormClosing+=delegate{Stop("Закрытие");timer.Stop();layoutSave.Stop();SaveMapSettings();DisconnectHub();mini.Dispose();if(!UiTest){Native.RemoveClipboardFormatListener(Handle);for(int id=1;id<=5;id++)Native.UnregisterHotKey(Handle,id);}};
  }
  Panel homePanel,settingsPanel,objectsPanel,loginPanel,connectedPanel;Button homeTab,settingsTab,objectsTab;Label connectionNick;
  void MoveTo(Control c,Control parent,int x,int y,int width,int height){parent.Controls.Add(c);c.SetBounds(x,y,width,height);c.Visible=true;c.TabStop=!(c is Label);}
  Label CompactLabel(Control parent,string text,int x,int y,int width,int height,bool bold=false){Label l=new Label{Text=text,Font=new Font("Segoe UI",bold?10:9,bold?FontStyle.Bold:FontStyle.Regular),ForeColor=bold?Color.FromArgb(145,160,171):ForeColor};MoveTo(l,parent,x,y,width,height);return l;}
  void CompactButton(Button button,bool primary=false){StyleButton(button);button.FlatAppearance.BorderSize=primary?0:1;button.FlatAppearance.BorderColor=Color.FromArgb(43,54,63);button.BackColor=primary?Color.FromArgb(231,71,80):Color.FromArgb(21,29,35);button.Font=new Font("Segoe UI",10,primary?FontStyle.Bold:FontStyle.Regular);button.FlatAppearance.MouseOverBackColor=primary?Color.FromArgb(245,85,94):Color.FromArgb(40,51,60);button.UseVisualStyleBackColor=false;}
  void SelectCompactTab(int selected){
   Panel[] panels={homePanel,settingsPanel,objectsPanel};Button[] tabs={homeTab,settingsTab,objectsTab};
   for(int i=0;i<panels.Length;i++){bool active=i==selected;panels[i].Visible=active;tabs[i].BackColor=active?Color.FromArgb(42,52,61):Color.FromArgb(21,29,35);Font old=tabs[i].Font;tabs[i].Font=new Font("Segoe UI",10,active?FontStyle.Bold:FontStyle.Regular);old.Dispose();}
  }
  void UpdateConnectionLayout(){if(loginPanel==null)return;bool connected=hub!=null;loginPanel.Visible=!connected;connectedPanel.Visible=connected;connectionNick.Text=hubNick.Text.Trim()+" · HUB подключён";hubState.SetBounds(connected?0:198,61,connected?472:274,34);hubState.BringToFront();}
  void BuildCompactTabs(){
   List<Control> old=new List<Control>();foreach(Control c in Controls){old.Add(c);c.Visible=false;}
   Func<string,Control> named=name=>old.Find(c=>c.Name==name);
   Func<string,Button> button=text=>old.Find(c=>c is Button&&c.Text==text) as Button;
   MinimumSize=Size.Empty;MaximumSize=Size.Empty;ClientSize=new Size(520,800);MinimumSize=MaximumSize=Size;MaximizeBox=false;AutoScroll=false;
   CompactLabel(this,"SCUM HUB",24,20,472,36).Font=new Font("Segoe UI",22,FontStyle.Bold);CompactLabel(this,"Миникарта отряда",25,60,470,24).ForeColor=Color.FromArgb(145,160,171);
   homeTab=new RoundedButton{Text="Главная"};settingsTab=new RoundedButton{Text="Настройки"};objectsTab=new RoundedButton{Text="Объекты"};CompactButton(homeTab);CompactButton(settingsTab);CompactButton(objectsTab);MoveTo(homeTab,this,24,100,149,38);MoveTo(settingsTab,this,185,100,149,38);MoveTo(objectsTab,this,346,100,150,38);
   homePanel=new Panel();settingsPanel=new Panel();objectsPanel=new Panel();MoveTo(homePanel,this,24,150,472,576);MoveTo(settingsPanel,this,24,150,472,576);MoveTo(objectsPanel,this,24,150,472,576);
   homeTab.Click+=delegate{SelectCompactTab(0);};settingsTab.Click+=delegate{SelectCompactTab(1);};objectsTab.Click+=delegate{SelectCompactTab(2);};
   loginPanel=new Panel();connectedPanel=new Panel{BackColor=BackColor};MoveTo(loginPanel,homePanel,0,0,472,96);MoveTo(connectedPanel,homePanel,0,0,472,96);
   CompactLabel(loginPanel,"Ник",0,0,180,20);CompactLabel(loginPanel,"Код HUB",208,0,264,20);MoveTo(hubNick,loginPanel,0,22,192,27);MoveTo(hubCode,loginPanel,208,22,264,27);MoveTo(hubLogin,loginPanel,0,59,180,32);CompactButton(hubLogin);
   connectionNick=CompactLabel(connectedPanel,"",14,15,320,24);connectionNick.Font=new Font("Segoe UI",10,FontStyle.Bold);Button logout=button("Отключить");MoveTo(logout,connectedPanel,346,12,112,32);CompactButton(logout);
   // One live status control remains visible in either connection state.
   MoveTo(hubState,homePanel,0,61,472,34);hubState.Font=new Font("Segoe UI",8);hubState.BringToFront();
   MoveTo(toggle,homePanel,0,110,472,44);CompactButton(toggle,true);MoveTo(sharePosition,homePanel,0,164,472,25);
   CompactLabel(homePanel,"НАВИГАТОР",0,200,220,22,true);MoveTo(navigationMode,homePanel,278,191,194,28);MoveTo(navigationGoal,homePanel,0,229,472,31);MoveTo(navigationStart,homePanel,0,272,298,36);MoveTo(navigationCancel,homePanel,310,272,162,36);
   MoveTo(button("−"),homePanel,0,322,40,32);MoveTo(named("zoomValue"),homePanel,48,327,140,25);MoveTo(button("+"),homePanel,196,322,40,32);MoveTo(refreshMarkers,homePanel,260,322,212,32);
   CompactLabel(homePanel,"ДОБАВИТЬ В ТЕКУЩЕЙ ПОЗИЦИИ",0,375,472,22,true);string[] categories={"stash","car","base","other"};for(int i=0;i<4;i++)MoveTo(named("quick-"+categories[i]),homePanel,i%2*242,405+i/2*44,230,36);
   MoveTo(stashRefresh,homePanel,0,501,472,34);MoveTo(navigationState,homePanel,0,543,472,31);navigationState.Font=new Font("Segoe UI",8);
   CompactLabel(settingsPanel,"ВНЕШНИЙ ВИД КАРТЫ",0,7,472,22,true);
   Control[] visual={mapSize,named("mapOpacity"),named("selfArrowSize"),named("allyArrowSize")};string[] names={"Размер карты, пикс.","Непрозрачность","Своя стрелка, %","Стрелки союзников, %"};for(int i=0;i<4;i++){CompactLabel(settingsPanel,names[i],0,45+i*48,280,24);MoveTo(visual[i],settingsPanel,300,40+i*48,172,29);}
   CompactLabel(settingsPanel,"ПОЛОЖЕНИЕ НА ЭКРАНЕ",0,246,472,22,true);CompactLabel(settingsPanel,"X",0,287,24,24);CompactLabel(settingsPanel,"Y",245,287,24,24);MoveTo(mapX,settingsPanel,28,280,192,29);MoveTo(mapY,settingsPanel,273,280,199,29);MoveTo(button("Сбросить положение"),settingsPanel,0,325,472,34);
   CompactLabel(settingsPanel,"ОТСЛЕЖИВАНИЕ",0,385,472,22,true);CompactLabel(settingsPanel,"Режим",0,427,180,24);MoveTo(mode,settingsPanel,210,420,262,29);CompactLabel(settingsPanel,"Копирование координат",0,474,290,25);MoveTo(keyPicker,settingsPanel,316,466,156,32);MoveTo(autoHide,settingsPanel,0,519,250,28);MoveTo(button("Настроить север · F11"),settingsPanel,258,514,214,34);
   Button networkReport=new RoundedButton{Text="Сохранить отчёт связи"};CompactButton(networkReport);MoveTo(networkReport,settingsPanel,0,554,230,22);networkReport.Click+=delegate{if(live==null){MessageBox.Show(this,"Сначала подключись к HUB.");return;}using(SaveFileDialog save=new SaveFileDialog{FileName="SCUM_HUB_connection.txt",Filter="Текстовый отчёт|*.txt"}){if(save.ShowDialog(this)==DialogResult.OK){try{File.WriteAllText(save.FileName,live.DiagnosticReport(),new UTF8Encoding(true));MessageBox.Show(this,"Отчёт сохранён. Пришли этот файл для проверки связи.");}catch(Exception ex){MessageBox.Show(this,"Не удалось сохранить отчёт: "+ex.Message);}}}};
   BuildObjectsTab();
   MoveTo(state,this,24,735,472,32);state.Font=new Font("Segoe UI",8);CompactLabel(this,"↑↓ масштаб   F7 карта   F8 пауза   F9 стоп",24,777,472,20).Font=new Font("Segoe UI",8);
   foreach(Control panel in new Control[]{homePanel,settingsPanel,objectsPanel})foreach(Control c in panel.Controls){Button b=c as Button;if(b!=null)CompactButton(b,b==toggle);DarkComboBox combo=c as DarkComboBox;if(combo!=null){combo.BackColor=Color.FromArgb(21,29,35);combo.ForeColor=ForeColor;combo.FlatStyle=FlatStyle.Flat;}if(c is NumericUpDown){c.BackColor=Color.FromArgb(17,25,30);c.ForeColor=ForeColor;}}
   hubNick.BackColor=hubCode.BackColor=Color.FromArgb(17,25,30);hubNick.ForeColor=hubCode.ForeColor=ForeColor;
   foreach(Control parent in new Control[]{homePanel,settingsPanel,loginPanel}){List<Control> fields=new List<Control>();foreach(Control c in parent.Controls)if(c is TextBox||c is NumericUpDown)fields.Add(c);foreach(Control c in fields){Rectangle bounds=c.Bounds;RoundedFieldFrame frame=new RoundedFieldFrame(c);parent.Controls.Add(frame);frame.Bounds=bounds;frame.PerformLayout();}}
   RoundedUi.Clip(connectedPanel,10);connectedPanel.SizeChanged+=delegate{RoundedUi.Clip(connectedPanel,10);};
   SelectCompactTab(0);UpdateConnectionLayout();
  }
  void BuildObjectsTab(){
   CompactLabel(objectsPanel,"ОБЪЕКТЫ НА МИНИКАРТЕ",0,7,472,24,true);
   CompactLabel(objectsPanel,"Выбери, какие категории показывать.",0,40,472,25);
   for(int i=0;i<StaticPoiLayer.Categories.Length;i++){
    string key=StaticPoiLayer.Categories[i];int y=88+i*66;
    Panel row=new RoundedPanel{BackColor=Color.FromArgb(21,29,35)};MoveTo(row,objectsPanel,0,y,472,54);
    CheckBox box=new CheckBox{Name="poi-"+key,Text=StaticPoiLayer.Labels[i],Checked=mini.Objects.IsEnabled(key),AutoSize=false,Font=new Font("Segoe UI",10),ForeColor=ForeColor,BackColor=row.BackColor,Padding=new Padding(2,0,0,0)};MoveTo(box,row,14,10,354,34);
    Label count=CompactLabel(row,mini.Objects.Count(key).ToString(),386,14,68,26);count.TextAlign=ContentAlignment.MiddleRight;count.ForeColor=Color.FromArgb(145,160,171);
    box.CheckedChanged+=delegate{mini.Objects.SetEnabled(key,box.Checked);UpdateNavigationMarkers();mini.Invalidate();};
   }
   CompactLabel(objectsPanel,"По умолчанию включены все категории.\r\nТвой выбор сохраняется после закрытия программы.",0,433,472,48).ForeColor=Color.FromArgb(145,160,171);
   CompactLabel(objectsPanel,"В навигаторе расстояния указаны по прямой\r\nот последней полученной позиции.",0,500,472,48).ForeColor=Color.FromArgb(145,160,171);
  }
  void SaveUiPreview(string name,DarkComboBox open=null){if(Environment.OSVersion.Platform!=PlatformID.Win32NT)return;using(Bitmap image=new Bitmap(Width,Height)){DrawToBitmap(image,new Rectangle(0,0,Width,Height));if(open!=null)open.RenderOpenPopup(image);image.Save(name,System.Drawing.Imaging.ImageFormat.Png);}}
  static void PumpUi(int milliseconds){Stopwatch wait=Stopwatch.StartNew();while(wait.ElapsedMilliseconds<milliseconds){Application.DoEvents();System.Threading.Thread.Sleep(1);}}
  static void TestMouseClick(Control control){Cursor.Position=control.PointToScreen(new Point(control.Width/2,control.Height/2));if(!Native.Send(new Native.INPUT[]{new Native.INPUT{type=0,u=new Native.INPUTUNION{mi=new Native.MOUSEINPUT{dwFlags=2}}},new Native.INPUT{type=0,u=new Native.INPUTUNION{mi=new Native.MOUSEINPUT{dwFlags=4}}}}))throw new Exception("Test mouse injection failed");PumpUi(80);}
  internal void TestCompactUI(){
   Show();Application.DoEvents();
   // Windows hosted runners have a 1024x720 desktop and clamp top-level height.
   bool heightOK=ClientSize.Height==800||(Screen.PrimaryScreen.WorkingArea.Height<800&&ClientSize.Height>=homePanel.Bottom);
   if(ClientSize.Width!=520||!heightOK||!homePanel.Visible||settingsPanel.Visible||!loginPanel.Visible||connectedPanel.Visible)throw new Exception("Initial compact tab/login state incorrect: client="+ClientSize+", home="+homePanel.Visible+", settings="+settingsPanel.Visible+", login="+loginPanel.Visible+", connected="+connectedPanel.Visible+", desktop="+Screen.PrimaryScreen.WorkingArea);
   if(hubNick.Parent.Parent!=loginPanel||hubCode.Parent.Parent!=loginPanel||navigationGoal.Parent!=homePanel||stashRefresh.Parent!=homePanel||mapSize.Parent.Parent!=settingsPanel||autoHide.Parent!=settingsPanel)throw new Exception("Control on wrong tab");
   if(refreshMarkers.Top>=stashRefresh.Top||refreshMarkers.Top<=navigationStart.Top||stashRefresh.Top<=homePanel.Controls["quick-base"].Top)throw new Exception("Map controls and stash action order incorrect");
   foreach(Control panel in new Control[]{homePanel,settingsPanel,objectsPanel})foreach(Control c in panel.Controls){if(c.Left<0||c.Top<0||c.Right>panel.ClientSize.Width||c.Bottom>panel.ClientSize.Height)throw new Exception("Clipped compact control: "+c.Text);}
   if(settingsTab.Left-homeTab.Right!=12||objectsTab.Left-settingsTab.Right!=12||objectsTab.Right!=496)throw new Exception("Tab gap missing");
   if(!(toggle is RoundedButton)||toggle.Region==null||toggle.Region.IsVisible(0,0)||!toggle.Region.IsVisible(toggle.Width/2,toggle.Height/2)||!(mapSize.Parent is RoundedFieldFrame))throw new Exception("Rounded control rendering missing");
   if(navigationGoal.Width!=472||navigationMode.Width!=194||mode.Width!=262)throw new Exception("Selector width was lost during layout");
   if(TextRenderer.MeasureText("На машине",navigationMode.Font).Width>navigationMode.Width-38||TextRenderer.MeasureText("Автоматически",mode.Font).Width>mode.Width-38)throw new Exception("Mode text does not fit");
   if(connectedPanel.BackColor!=BackColor)throw new Exception("Connection background must match the form");
   if(!navigationGoal.Enabled||navigationGoal.DroppedDown)throw new Exception("Built-in destinations must be available offline");
   Rectangle loginBounds=homePanel.RectangleToClient(hubLogin.RectangleToScreen(hubLogin.ClientRectangle));if(hubState.Bounds.IntersectsWith(loginBounds)||!hubLogin.Visible||loginPanel.GetChildAtPoint(new Point(hubLogin.Left+hubLogin.Width/2,hubLogin.Top+hubLogin.Height/2))!=hubLogin)throw new Exception("Login button is covered");
   SaveUiPreview("ui-preview-login.png");
   if(Environment.OSVersion.Platform==PlatformID.Win32NT){Activate();PumpUi(80);for(int click=0;click<4;click++){TestMouseClick(navigationMode);if(navigationMode.DroppedDown!=(click%2==0))throw new Exception("Repeated selector click did not toggle: "+click);}Console.WriteLine("PASS real mouse repeated dropdown toggle");}
   settingsTab.PerformClick();Application.DoEvents();
   foreach(DarkComboBox combo in new DarkComboBox[]{mode,(DarkComboBox)settingsPanel.Controls.Find("mapOpacity",true)[0]}){if(!(combo is DarkComboBox)||combo.DropDownWidth!=combo.Width||combo.DropDownHeight>240)throw new Exception("Selector style/geometry incorrect");combo.DroppedDown=true;Application.DoEvents();if(!combo.DroppedDown)throw new Exception("Selector did not open");combo.DroppedDown=false;using(Bitmap image=new Bitmap(combo.Width,combo.Height)){combo.DrawToBitmap(image,combo.ClientRectangle);Color pixel=image.GetPixel(combo.Width-3,combo.Height/2);if(pixel.R>190&&pixel.G>190&&pixel.B>190)throw new Exception("White native selector chrome leaked");}}
   if(homePanel.Visible||!settingsPanel.Visible||!mapSize.Visible||navigationGoal.Visible)throw new Exception("Settings tab visibility incorrect");
   SaveUiPreview("ui-preview-settings.png");
   objectsTab.PerformClick();Application.DoEvents();
   if(!objectsPanel.Visible||homePanel.Visible||settingsPanel.Visible)throw new Exception("Objects tab visibility incorrect");
   for(int i=0;i<StaticPoiLayer.Categories.Length;i++){string key=StaticPoiLayer.Categories[i];CheckBox box=(CheckBox)objectsPanel.Controls.Find("poi-"+key,true)[0];if(!box.Checked)throw new Exception("All POI categories must default on");box.Checked=false;Application.DoEvents();if(mini.Objects.IsEnabled(key))throw new Exception("POI filter did not hide category");box.Checked=true;}
   foreach(Control row in objectsPanel.Controls)if(row is RoundedPanel)using(Bitmap image=new Bitmap(row.Width,row.Height)){row.DrawToBitmap(image,row.ClientRectangle);if(image.GetPixel(0,0).ToArgb()==image.GetPixel(row.Width-10,row.Height/2).ToArgb())throw new Exception("Object card corners must be rounded");}
   SaveUiPreview("ui-preview-objects.png");Console.WriteLine("PASS third objects tab, equal tab gaps, defaults and live filters");
   settingsTab.PerformClick();Application.DoEvents();

   foreach(Control input in new Control[]{mapSize,mapX,mapY,settingsPanel.Controls.Find("selfArrowSize",true)[0],settingsPanel.Controls.Find("allyArrowSize",true)[0]}){RoundedFieldFrame frame=(RoundedFieldFrame)input.Parent;using(Bitmap image=new Bitmap(frame.Width,frame.Height)){frame.DrawToBitmap(image,frame.ClientRectangle);Color pixel=image.GetPixel(frame.Width-15,frame.Height/2);if(pixel.R>190&&pixel.G>190&&pixel.B>190)throw new Exception("White numeric button leaked");}}
   hubNick.Text="k1t0";hubState.Text="Союзники: подключено · 150 мс";hub=new HubClient();UpdateConnectionLayout();homeTab.PerformClick();Application.DoEvents();if(loginPanel.Visible||!connectedPanel.Visible||hubCode.Visible||!hubState.Visible)throw new Exception("Connected login collapse incorrect");
   mini.SetPosition(new Position{X=-445230,Y=297444});
   for(int i=0;i<30;i++)mini.Markers.Add(new HubMarker{Id="ui-test-"+i,Name=i==0?"Схрон на депо, пока почти пустой":"Схрон с припасами "+(i+1).ToString("00"),Kind="stash",X=.5,Y=.5});RebuildNavigationChoices();SaveUiPreview("ui-preview-home.png");navigationGoal.DroppedDown=true;Application.DoEvents();if(!navigationGoal.Enabled||!navigationGoal.DroppedDown||navigationGoal.DropDownWidth!=navigationGoal.Width||navigationGoal.DropDownHeight>240)throw new Exception("Destination popup geometry/state incorrect");NavigationChoice stable=(NavigationChoice)navigationGoal.SelectedItem;int stableIndex=navigationGoal.SelectedIndex;double beforeDistance=stable.MetresAway.Value;mini.SetPosition(new Position{X=-440230,Y=297444});RefreshNavigationDistances();if(!navigationGoal.DroppedDown||navigationGoal.SelectedIndex!=stableIndex||!Object.ReferenceEquals(stable,navigationGoal.SelectedItem)||Math.Abs(stable.MetresAway.Value-beforeDistance)<1)throw new Exception("Distance refresh rebuilt or changed popup selection");mini.SetPosition(new Position{X=-445230,Y=297444});RefreshNavigationDistances();
   SaveUiPreview("ui-preview-list.png",navigationGoal);navigationGoal.TestScrollInput();navigationGoal.DroppedDown=false;mini.Markers.Clear();RebuildNavigationChoices();navigationGoal.DroppedDown=true;Application.DoEvents();SaveUiPreview("ui-preview-poi-navigation.png",navigationGoal);navigationGoal.DroppedDown=false;
   for(int i=0;i<navigationGoal.Items.Count;i++){NavigationChoice row=(NavigationChoice)navigationGoal.Items[i];if(row.Marker!=null&&row.Marker.Kind=="poi_secret_bunker"){navigationGoal.SelectedIndex=i;break;}}Activate();PumpUi(100);navigationGoal.DroppedDown=true;PumpUi(100);if(!navigationGoal.DroppedDown)throw new Exception("POI category popup did not reopen");SaveUiPreview("ui-preview-bunker-navigation.png",navigationGoal);navigationGoal.DroppedDown=false;
   DisconnectHub();Application.DoEvents();if(!loginPanel.Visible||connectedPanel.Visible||!hubCode.Visible)throw new Exception("Disconnect did not restore login");
   Close();
  }
  void Place(Control c,int x,int y,int w,int h){c.SetBounds(x,y,w,h);c.Visible=true;}
  void MinimalLayout(){
   ClientSize=new Size(700,600);MinimumSize=MaximumSize=new Size(716,639);AutoScroll=false;
   foreach(Control c in Controls)c.Visible=false;
   MakeLabel("SCUM HUB",24,20,400,38,21,true);
   MakeLabel("Миникарта",26,61,400,24,10,false);
   MakeLabel("ПОДКЛЮЧЕНИЕ",24,108,320,24,10,true);
   MakeLabel("Ник",24,143,145,20,9,false);
   Place(hubNick,24,167,311,29);
   MakeLabel("Код HUB",24,207,310,20,9,false);Place(hubCode,24,230,311,29);
   
   Place(refreshMarkers,24,269,311,30);
   Place(hubLogin,24,308,192,36);
   Place(hubState,24,355,311,32);hubState.Font=new Font("Segoe UI",9);hubState.Text="Введи код своего аккаунта HUB.";
   MakeLabel("ОТСЛЕЖИВАНИЕ",24,416,320,24,10,true);
   Place(mode,24,451,311,29);
   Place(toggle,24,496,150,40);Place(state,24,548,650,38);state.Font=new Font("Segoe UI",9);
   MakeLabel("КАРТА",385,108,290,24,10,true);
   MakeLabel("Положение X / Y",385,143,280,22,9,false);Place(mapX,385,167,130,29);Place(mapY,535,167,130,29);
   MakeLabel("Размер",385,211,280,22,9,false);Place(mapSize,385,235,280,29);
   foreach(Control c in Controls){if(c is DarkComboBox&&c!=mode&&c!=hubRole){Place(c,385,308,280,29);}}
   MakeLabel("Непрозрачность",385,284,280,22,9,false);
   foreach(Control c in Controls){if(!(c is Button))continue;switch(c.Text){
    case "Отключить":Place(c,228,308,107,36);break;
    case "Карта · F7":c.Text="Показать / скрыть карту · F7";Place(c,385,365,280,36);break;
    case "Вернуть карту на экран":c.Text="Сбросить положение";Place(c,385,414,280,36);break;
    case "Смотрю на север · F11":c.Text="Настроить север · F11";Place(c,385,463,280,36);break;
    case "Стоп · F9":Place(c,185,496,150,40);break;
   }}
  }
  readonly DarkComboBox navigationMode=new DarkComboBox();readonly DarkComboBox navigationGoal=new DarkComboBox();readonly Button navigationStart=new RoundedButton(),navigationCancel=new RoundedButton(),stashRefresh=new RoundedButton();bool refreshingStash;readonly Label navigationState=new Label();
  internal sealed class NavigationChoice {
   public HubMarker Marker;public string Header;public double? MetresAway;
   public string DisplayName{get{return Marker==null?Header:StaticPoiLayer.IsStatic(Marker)?Marker.Name:Marker.Name+" · "+MapMath.Sector(Marker.X,Marker.Y);}}
   public string DistanceText{get{return MetresAway.HasValue?Math.Ceiling(Math.Max(0,MetresAway.Value)).ToString("N0",CultureInfo.GetCultureInfo("ru-RU"))+" м":"—";}}
   public override string ToString(){return Marker==null?Header:DisplayName+" · "+DistanceText;}
  }
  bool navigationListUpdating;int lastNavigationIndex=-1;
  static int NavigationGroup(HubMarker marker){if(StaticPoiLayer.IsStatic(marker)){int index=Array.IndexOf(StaticPoiLayer.Categories,marker.Kind.Substring(4));return index<0?2:index+3;}return String.Equals(marker.Kind,"stash",StringComparison.OrdinalIgnoreCase)?0:String.Equals(marker.Kind,"base",StringComparison.OrdinalIgnoreCase)?1:2;}
  internal static List<NavigationChoice> GroupNavigationMarkers(List<HubMarker> markers,Position current,double radius,int width,int height){
   string[] names={"СХРОНЫ","БАЗЫ","ДРУГОЕ","ПОЛИЦАЙКИ","БРЕНОРЫ","СЕКРЕТКИ","TEC1","WW2"};List<NavigationChoice> rows=new List<NavigationChoice>();
   if(current!=null){PointF here=RoadGeometry.FromPosition(current);List<HubMarker> visible=markers.FindAll(m=>MapMath.MarkerInView(m,current,radius,width,height));
    visible.Sort((a,b)=>{int order=RoadGeometry.Distance(here,new PointF((float)a.X,(float)a.Y)).CompareTo(RoadGeometry.Distance(here,new PointF((float)b.X,(float)b.Y)));return order!=0?order:StringComparer.CurrentCultureIgnoreCase.Compare(a.Name,b.Name);});
    if(visible.Count>0){rows.Add(new NavigationChoice{Header="НА МИНИКАРТЕ"});foreach(HubMarker marker in visible)rows.Add(new NavigationChoice{Marker=marker,MetresAway=RoadGeometry.Distance(here,new PointF((float)marker.X,(float)marker.Y))});}
   }
   for(int group=0;group<8;group++){List<HubMarker> sorted=markers.FindAll(m=>NavigationGroup(m)==group);if(sorted.Count==0)continue;sorted.Sort((a,b)=>{int n=StringComparer.CurrentCultureIgnoreCase.Compare(a.Name,b.Name);return n!=0?n:StringComparer.Ordinal.Compare(a.Id,b.Id);});rows.Add(new NavigationChoice{Header=names[group]});foreach(HubMarker marker in sorted)rows.Add(new NavigationChoice{Marker=marker,MetresAway=current==null?(double?)null:RoadGeometry.Distance(RoadGeometry.FromPosition(current),new PointF((float)marker.X,(float)marker.Y))});}
   return rows;
  }
  void NavigationSelectionChanged(){
   if(navigationListUpdating)return;int index=navigationGoal.SelectedIndex;NavigationChoice row=navigationGoal.SelectedItem as NavigationChoice;
   if(row!=null&&row.Marker==null){int step=index<lastNavigationIndex?-1:1;int next=index+step;while(next>=0&&next<navigationGoal.Items.Count&&((NavigationChoice)navigationGoal.Items[next]).Marker==null)next+=step;
    if(next<0||next>=navigationGoal.Items.Count){next=index-step;while(next>=0&&next<navigationGoal.Items.Count&&((NavigationChoice)navigationGoal.Items[next]).Marker==null)next-=step;}
    navigationGoal.SelectedIndex=next;return;
   }lastNavigationIndex=index;UpdateNavigationState();
  }
  void DrawNavigationChoice(object sender,DrawItemEventArgs e){
   if(e.Index<0)return;NavigationChoice row=(NavigationChoice)navigationGoal.Items[e.Index];bool header=row.Marker==null;using(SolidBrush fill=new SolidBrush((e.State&DrawItemState.Selected)!=0?Color.FromArgb(43,59,69):navigationGoal.BackColor))e.Graphics.FillRectangle(fill,e.Bounds);
   if(header){using(SolidBrush b=new SolidBrush(Color.FromArgb(24,33,38)))e.Graphics.FillRectangle(b,e.Bounds);}
   bool closed=(e.State&DrawItemState.ComboBoxEdit)!=0;Rectangle bounds=e.Bounds;bounds.X+=header||closed?6:16;bounds.Width-=header||closed?10:20;
   using(Font font=new Font(navigationGoal.Font,header?FontStyle.Bold:FontStyle.Regular)){
    Color color=header?Color.FromArgb(245,132,136):((e.State&DrawItemState.Selected)!=0?SystemColors.HighlightText:navigationGoal.ForeColor);
    Rectangle title=bounds;
    if(!header){int distanceWidth=Math.Max(65,TextRenderer.MeasureText(row.DistanceText,font).Width+12);title.Width=Math.Max(1,bounds.Width-distanceWidth-10);Rectangle distance=new Rectangle(title.Right+10,bounds.Y,distanceWidth,bounds.Height);TextRenderer.DrawText(e.Graphics,row.DistanceText,font,distance,color,TextFormatFlags.VerticalCenter|TextFormatFlags.Right|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);}
    TextRenderer.DrawText(e.Graphics,row.DisplayName,font,title,color,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
   }
   if(!header)e.DrawFocusRectangle();
  }
  void BuildNavigationUI(){
   ClientSize=new Size(700,720);MinimumSize=MaximumSize=new Size(716,759);state.Top=674;
   MakeLabel("НАВИГАТОР",24,549,145,22,10,true);
   navigationMode.SetBounds(172,546,163,25);navigationMode.DropDownStyle=ComboBoxStyle.DropDownList;navigationMode.Items.AddRange(new object[]{"На машине","Пешком"});navigationMode.SelectedIndex=0;Controls.Add(navigationMode);
   navigationMode.SelectedIndexChanged+=async delegate{mini.Navigation.Mode=navigationMode.SelectedIndex==1?TravelMode.Foot:TravelMode.Car;string id=mini.Navigation.GoalId;HubMarker goal=mini.FindMarker(id);if(goal!=null&&mini.Current!=null)await mini.Navigation.Plan(mini.Current,goal);};
   navigationGoal.SetBounds(24,575,311,30);navigationGoal.DropDownStyle=ComboBoxStyle.DropDownList;navigationGoal.BackColor=Color.FromArgb(34,46,52);navigationGoal.ForeColor=ForeColor;navigationGoal.DrawMode=DrawMode.OwnerDrawFixed;navigationGoal.ItemHeight=23;navigationGoal.IsSelectable=item=>((NavigationChoice)item).Marker!=null;navigationGoal.DrawItem+=DrawNavigationChoice;navigationGoal.SelectedIndexChanged+=delegate{NavigationSelectionChanged();};Controls.Add(navigationGoal);
   navigationGoal.DropDown+=delegate{RebuildNavigationChoices();if(!navigationGoal.Enabled){navigationGoal.DroppedDown=false;return;}};
   mini.ZoomChanged+=delegate{if(!navigationGoal.DroppedDown)RebuildNavigationChoices();};
   navigationStart.Text="Проложить маршрут";navigationStart.SetBounds(24,617,192,34);StyleButton(navigationStart);Controls.Add(navigationStart);
   navigationCancel.Text="Отменить";navigationCancel.SetBounds(228,617,107,34);StyleButton(navigationCancel);Controls.Add(navigationCancel);
   navigationState.SetBounds(385,617,280,50);navigationState.Font=new Font("Segoe UI",9);Controls.Add(navigationState);
   navigationStart.Click+=async delegate{NavigationChoice choice=navigationGoal.SelectedItem as NavigationChoice;if(choice==null||choice.Marker==null)return;if(mini.Current==null){navigationState.Text="Сначала включи отслеживание и получи позицию в игре.";return;}await mini.Navigation.Plan(mini.Current,choice.Marker);mini.Navigation.Update(mini.Current);};
   navigationCancel.Click+=delegate{mini.Navigation.Cancel("Маршрут отменён.");};
   stashRefresh.Text="Обновить схрон · 14 дней";stashRefresh.SetBounds(385,658,280,30);StyleButton(stashRefresh);Controls.Add(stashRefresh);stashRefresh.Click+=async delegate{await RefreshSelectedStash();};
   mini.Navigation.Changed+=delegate{if(!IsDisposed){UpdateNavigationState();mini.Invalidate();}};
   RebuildNavigationChoices();
  }
  internal static List<NavigationChoice> VisibleStashes(List<HubMarker> markers,Position current,double radius,int width,int height){
   List<NavigationChoice> result=new List<NavigationChoice>();if(current==null)return result;PointF origin=RoadGeometry.FromPosition(current);
   foreach(HubMarker marker in markers)if(HubClient.CanRefreshStash(marker)&&MapMath.MarkerInView(marker,current,radius,width,height))result.Add(new NavigationChoice{Marker=marker,MetresAway=RoadGeometry.Distance(origin,new PointF((float)marker.X,(float)marker.Y))});
   result.Sort((a,b)=>{int order=a.MetresAway.Value.CompareTo(b.MetresAway.Value);return order!=0?order:StringComparer.CurrentCultureIgnoreCase.Compare(a.Marker.Name,b.Marker.Name);});return result;
  }
  async System.Threading.Tasks.Task RefreshSelectedStash(){
   if(refreshingStash||hub==null)return;if(mini.Current==null){ShowActionNotice("Сначала получи позицию в игре, чтобы найти схроны на миникарте.");return;}
   List<NavigationChoice> choices=VisibleStashes(mini.Markers,mini.Current,mini.ViewRadius,mini.ClientSize.Width,mini.ClientSize.Height);
   if(choices.Count==0){ShowActionNotice("В видимой области миникарты нет схронов с таймером. Увеличь обзор или подойди ближе.");return;}
   HubClient current=hub;int generation=hubGeneration;HubMarker marker=null;refreshingStash=true;UpdateNavigationState();
   try{
    using(Form dialog=new Form()){
     dialog.HandleCreated+=delegate{RoundedUi.DarkTitle(dialog);};dialog.Text="Обновить схрон";dialog.ClientSize=new Size(460,315);dialog.FormBorderStyle=FormBorderStyle.FixedDialog;dialog.MaximizeBox=dialog.MinimizeBox=false;dialog.StartPosition=FormStartPosition.CenterParent;dialog.BackColor=BackColor;dialog.ForeColor=ForeColor;dialog.Font=new Font("Segoe UI",9);
     dialog.Controls.Add(new Label{Text="Схроны в видимой области миникарты · ближайшие сверху",Location=new Point(16,16),Size=new Size(428,36)});
     ListBox list=new ListBox{Location=new Point(16,56),Size=new Size(428,174),BackColor=Color.FromArgb(34,46,52),ForeColor=ForeColor,HorizontalScrollbar=true};foreach(NavigationChoice choice in choices)list.Items.Add(choice);list.SelectedIndex=0;dialog.Controls.Add(list);
     Button update=new RoundedButton{Text="Обновить · 14 дней",DialogResult=DialogResult.OK,Location=new Point(16,257),Size=new Size(206,36)},cancel=new RoundedButton{Text="Отмена",DialogResult=DialogResult.Cancel,Location=new Point(238,257),Size=new Size(206,36)};StyleButton(update);StyleButton(cancel);dialog.Controls.Add(update);dialog.Controls.Add(cancel);dialog.AcceptButton=update;dialog.CancelButton=cancel;
     if(dialog.ShowDialog(this)!=DialogResult.OK)return;NavigationChoice selected=list.SelectedItem as NavigationChoice;if(selected==null)return;marker=selected.Marker;
    }
    if(IsDisposed||hub!=current||generation!=hubGeneration)return;stashRefresh.Text="Обновляю…";
    await current.RefreshStash(marker);if(IsDisposed||hub!=current||generation!=hubGeneration)return;await RefreshHub();if(!IsDisposed)ShowActionNotice("Схрон «"+marker.Name+"» обновлён на 14 дней.");
   }
   catch(Exception ex){if(!IsDisposed&&hub==current&&generation==hubGeneration)ShowActionNotice(ex.Message);}
   finally{refreshingStash=false;if(!IsDisposed){stashRefresh.Text="Обновить схрон · 14 дней";UpdateNavigationState();}}
  }
  void UpdateNavigationState(){navigationGoal.Enabled=navigationGoal.Items.Count>0;((DarkComboBox)navigationGoal).EmptyText=hub==null?"Подключись к HUB, чтобы выбрать метку":"Нет доступных меток";navigationGoal.Invalidate();navigationState.Text=mini.Navigation.Status;NavigationChoice selected=navigationGoal.SelectedItem as NavigationChoice;navigationStart.Enabled=selected!=null&&selected.Marker!=null&&!mini.Navigation.Busy;navigationCancel.Enabled=mini.Navigation.Route!=null||mini.Navigation.Busy;stashRefresh.Enabled=hub!=null&&!refreshingStash;}
  void RebuildNavigationChoices(){
   NavigationChoice previousChoice=navigationGoal.SelectedItem as NavigationChoice;string selected=previousChoice==null||previousChoice.Marker==null?null:previousChoice.Marker.Id;
   navigationListUpdating=true;navigationGoal.BeginUpdate();try{navigationGoal.Items.Clear();int first=-1;foreach(NavigationChoice row in GroupNavigationMarkers(mini.NavigationMarkers,mini.Current,mini.ViewRadius,mini.ClientSize.Width,mini.ClientSize.Height)){int i=navigationGoal.Items.Add(row);if(row.Marker!=null){if(first<0)first=i;if(row.Marker.Id==selected&&navigationGoal.SelectedIndex<0)navigationGoal.SelectedIndex=i;}}
    if(navigationGoal.SelectedIndex<0&&first>=0)navigationGoal.SelectedIndex=first;lastNavigationIndex=navigationGoal.SelectedIndex;
   }finally{navigationGoal.EndUpdate();navigationListUpdating=false;}
   UpdateNavigationState();
  }
  void RefreshNavigationDistances(){
   if(mini.Current==null)return;PointF here=RoadGeometry.FromPosition(mini.Current);
   foreach(NavigationChoice row in navigationGoal.Items)if(row.Marker!=null)row.MetresAway=RoadGeometry.Distance(here,new PointF((float)row.Marker.X,(float)row.Marker.Y));
   navigationGoal.RefreshRows();
  }
  void UpdateNavigationMarkers(){
   RebuildNavigationChoices();
   string activeId=mini.Navigation.GoalId;if(activeId!=null){HubMarker active=mini.FindMarker(activeId);if(active==null)mini.Navigation.Cancel("Метка удалена из HUB. Маршрут отменён.");else mini.Navigation.RefreshTarget(active,mini.Current);}
   UpdateNavigationState();
  }
  bool creatingMarker;
  void BuildQuickMarkers(){
   ClientSize=new Size(700,900);MinimumSize=MaximumSize=new Size(716,939);state.Top=854;
   MakeLabel("ДОБАВИТЬ В ТЕКУЩЕЙ ПОЗИЦИИ",24,772,650,22,9,true);
   string[] captions={"Схрон","Машина","Вражеская база","Метка"},categories={"stash","car","base","other"};
   for(int i=0;i<4;i++){string category=categories[i],caption=captions[i];Button add=new RoundedButton();add.Text=caption;add.Name="quick-"+category;add.SetBounds(24+i*164,800,149,34);StyleButton(add);Controls.Add(add);add.Click+=async delegate{await AddQuickMarker(category,caption);};}
  }
  async System.Threading.Tasks.Task AddQuickMarker(string category,string caption){
   if(creatingMarker)return;if(hub==null){state.Text="Сначала подключись к HUB.";return;}
   if(previous==null||lastSuccess<0){state.Text="Позиция ещё не получена. Включи отслеживание и один раз вернись в игру.";return;}
   Position fixedPosition=new Position{X=previous.X,Y=previous.Y};HubClient current=hub;int generation=hubGeneration;creatingMarker=true;
   try{using(Form dialog=new Form()){
    dialog.HandleCreated+=delegate{RoundedUi.DarkTitle(dialog);};dialog.Text="Добавить: "+caption;dialog.ClientSize=new Size(420,205);dialog.FormBorderStyle=FormBorderStyle.FixedDialog;dialog.MaximizeBox=dialog.MinimizeBox=false;dialog.StartPosition=FormStartPosition.CenterParent;dialog.BackColor=BackColor;dialog.ForeColor=ForeColor;dialog.Font=new Font("Segoe UI",9);
    Label location=new Label{Text="Последняя известная позиция · "+MapMath.Sector(MapMath.NormX(fixedPosition.X),MapMath.NormY(fixedPosition.Y))+"\r\nX="+fixedPosition.X.ToString("0",CultureInfo.InvariantCulture)+"  Y="+fixedPosition.Y.ToString("0",CultureInfo.InvariantCulture),Location=new Point(18,14),Size=new Size(384,42)};dialog.Controls.Add(location);
    dialog.Controls.Add(new Label{Text="Название",Location=new Point(18,65),Size=new Size(384,20)});
    TextBox name=new TextBox{Text=caption,MaxLength=70,Location=new Point(18,90),Size=new Size(384,28)};dialog.Controls.Add(name);
    Button save=new RoundedButton{Text="Сохранить",Location=new Point(18,145),Size=new Size(184,36)},cancel=new RoundedButton{Text="Отмена",DialogResult=DialogResult.Cancel,Location=new Point(218,145),Size=new Size(184,36)};StyleButton(save);StyleButton(cancel);dialog.Controls.Add(save);dialog.Controls.Add(cancel);dialog.AcceptButton=save;dialog.CancelButton=cancel;
    save.Click+=delegate{if(String.IsNullOrWhiteSpace(name.Text)){name.Focus();return;}dialog.DialogResult=DialogResult.OK;};dialog.Shown+=delegate{name.SelectAll();name.Focus();};
    if(dialog.ShowDialog(this)!=DialogResult.OK)return;if(current!=hub||generation!=hubGeneration){state.Text="Подключение HUB изменилось. Открой добавление заново.";return;}
    state.Text="Сохраняю точку…";
    try{await current.CreateMarker(category,name.Text,fixedPosition);}catch(HubExpiredException){throw;}catch(Exception ex){state.Text="Связь с HUB: "+ex.Message+" Обнови метки перед повторным добавлением.";return;}
    if(IsDisposed||current!=hub||generation!=hubGeneration)return;
    // The post is the sole source of the marker, so a failed refresh cannot duplicate it.
    await RefreshHub();state.Text="Сохранено: "+name.Text.Trim()+". "+(mini.SyncStatus.StartsWith("HUB: нет связи")?"Нажми «Обновить метки», когда появится связь.":"Точка добавлена в HUB.");
   }}catch(Exception ex){if(!IsDisposed)state.Text=ex.Message;}finally{creatingMarker=false;}
  }
  void BuildArrowSizes(){
   ClientSize=new Size(700,800);MinimumSize=MaximumSize=new Size(716,839);state.Top=754;
   MakeLabel("Размер своей стрелки, %",24,700,311,22,9,false);MakeLabel("Размер стрелок союзников, %",385,700,280,22,9,false);
   AddArrowSize(24,311,"self-arrow-size.txt",true);AddArrowSize(385,280,"ally-arrow-size.txt",false);
  }
  void AddArrowSize(int x,int width,string file,bool self){
   NumericUpDown size=new NumericUpDown();size.Name=self?"selfArrowSize":"allyArrowSize";size.SetBounds(x,726,width,29);size.Minimum=50;size.Maximum=200;size.Increment=10;int saved=100;try{int parsed;if(Int32.TryParse(File.ReadAllText(UserSettings.PathFor(file)),out parsed))saved=parsed;}catch{}size.Value=Math.Max(50,Math.Min(200,saved));
   Action apply=delegate{float scale=(float)size.Value/100f;if(self)mini.SelfArrowScale=scale;else mini.AllyArrowScale=scale;mini.Invalidate();};apply();size.ValueChanged+=delegate{apply();try{File.WriteAllText(UserSettings.PathFor(file),size.Value.ToString(CultureInfo.InvariantCulture));}catch{}};Controls.Add(size);
  }
  void BuildSharing(){sharePosition.SetBounds(24,390,320,24);sharePosition.Text="Делиться позицией с отрядом";sharePosition.Font=new Font("Segoe UI",9);sharePosition.Checked=true;sharePosition.CheckedChanged+=delegate{if(live!=null)live.Sharing=sharePosition.Checked;};Controls.Add(sharePosition);}
  void StartAllies(){if(live!=null)live.Dispose();AllyLive current=new AllyLive(hub);live=current;current.Sharing=sharePosition.Checked;
   current.Updated=delegate(List<AllyPosition> peers){allyInbox.Publish(current,peers);};
   current.Status=delegate(string status){try{if(!IsDisposed&&IsHandleCreated)BeginInvoke((Action)delegate{if(live==current)hubState.Text=status;});}catch{}};current.Start();
  }
  void BuildZoomUI(){
   ClientSize=new Size(700,650);MinimumSize=MaximumSize=new Size(716,689);
   foreach(Control c in Controls){if(c is Button&&(c.Text=="Показать / скрыть карту · F7"||c.Text=="Сбросить положение"||c.Text=="Настроить север · F11"))c.Top+=72;}
   autoHide.Top+=72;state.Top=608;
   MakeLabel("Масштаб карты · ↑ / ↓",385,352,280,22,9,false);
   Button less=new RoundedButton(),more=new RoundedButton();less.Text="−";more.Text="+";less.SetBounds(385,380,58,34);more.SetBounds(607,380,58,34);StyleButton(less);StyleButton(more);Controls.Add(less);Controls.Add(more);
   Label value=MakeLabel("",452,386,146,25,10,false);value.Name="zoomValue";value.TextAlign=ContentAlignment.TopCenter;
   try{double saved;if(Double.TryParse(File.ReadAllText(UserSettings.PathFor("zoom.txt")),NumberStyles.Float,CultureInfo.InvariantCulture,out saved))mini.SetZoomRadius(saved);}catch{}
   Action update=delegate{value.Text="Радиус "+mini.ViewRadius.ToString("0")+" м";};update();
   mini.ZoomChanged+=delegate{update();try{File.WriteAllText(UserSettings.PathFor("zoom.txt"),mini.ViewRadius.ToString(CultureInfo.InvariantCulture));}catch{}};
   less.Click+=delegate{mini.ChangeZoom(false);};more.Click+=delegate{mini.ChangeZoom(true);};
  }
  void BuildAutoHide(){autoHide.SetBounds(385,511,290,27);autoHide.Text="Скрывать карту при курсоре";autoHide.Font=new Font("Segoe UI",9);autoHide.Checked=true;try{autoHide.Checked=File.ReadAllText(UserSettings.PathFor("auto-hide.txt")).Trim()!="0";}catch{}autoHide.CheckedChanged+=delegate{try{File.WriteAllText(UserSettings.PathFor("auto-hide.txt"),autoHide.Checked?"1":"0");}catch{}if(!autoHide.Checked){menuCursor=autoHidden=lastCursor=false;ApplyVisibility();}};Controls.Add(autoHide);}
  void BuildKeyPicker(){
   try{int key;if(Int32.TryParse(File.ReadAllText(UserSettings.PathFor("copy-key.txt")),out key)&&KeyCapture.Allowed((Keys)key))copyKey=(ushort)key;}catch{}
   mode.Items[0]="Вручную";mode.Items[1]="Автоматически";mode.SelectedIndex=1;mode.Width=155;
   keyPicker.SetBounds(190,451,145,29);keyPicker.Text="Клавиша: "+((Keys)copyKey).ToString();StyleButton(keyPicker);
   keyPicker.Click+=delegate{using(KeyCapture dialog=new KeyCapture()){if(dialog.ShowDialog(this)!=DialogResult.OK)return;copyKey=(ushort)dialog.Selected;keyPicker.Text="Клавиша: "+dialog.Selected;try{File.WriteAllText(UserSettings.PathFor("copy-key.txt"),copyKey.ToString());}catch{}}};Controls.Add(keyPicker);
  }
  Label MakeLabel(string text,int x,int y,int w,int h,int size,bool bold){Label l=new Label();l.Text=text;l.SetBounds(x,y,w,h);l.Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular);Controls.Add(l);return l;}
  void StyleButton(Button b){b.FlatStyle=FlatStyle.Flat;b.BackColor=Color.FromArgb(34,46,52);b.ForeColor=ForeColor;}
  protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);if(UiTest)return;RoundedUi.DarkTitle(this);Native.AddClipboardFormatListener(Handle);bool a=Native.RegisterHotKey(Handle,1,0x4000,0x77),b=Native.RegisterHotKey(Handle,2,0x4000,0x78);Native.RegisterHotKey(Handle,3,0x4000,0x76);Native.RegisterHotKey(Handle,5,0x4000,0x7A);hotkeysOk=a&&b;if(!hotkeysOk){Native.UnregisterHotKey(Handle,1);Native.UnregisterHotKey(Handle,2);state.Text="F8/F9 заняты. Закрой программу, освободившую клавиши, и запусти снова.";}}
  protected override void WndProc(ref Message m){if(m.Msg==0x031D&&running&&!movingWindow){uint pid;IntPtr win;if(IsGame(out pid,out win)){ReadClipboard();}}if(m.Msg==0x0231){movingWindow=true;if(live!=null)live.SendingAllowed=false;mini.PausePainting(true);ReleaseKeys();phase=0;timer.Stop();}if(m.Msg==0x0232){movingWindow=false;mini.PausePainting(false);timer.Start();}if(m.Msg==0x0312){if(m.WParam.ToInt32()==1)Toggle();if(m.WParam.ToInt32()==2)Stop("Остановлено по F9.");if(m.WParam.ToInt32()==3)ToggleMini();if(m.WParam.ToInt32()==5)CalibrateNorth();}base.WndProc(ref m);}
  void Toggle(){if(running){Stop("Остановлено по F8.");return;}if(!hotkeysOk){MessageBox.Show(this,"Не удалось зарегистрировать F8/F9. Автоматический режим не включён.");return;}
   if(mode.SelectedIndex==1&&MessageBox.Show(this,"Ты уже назначил в SCUM действие «Копировать местоположение» на выбранную клавишу? Иначе этот режим не получит координаты.","Проверка назначения",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
   try{string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"logs");Directory.CreateDirectory(dir);logPath=Path.Combine(dir,"positions_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+".csv");log=new StreamWriter(logPath,false,new UTF8Encoding(true));log.WriteLine("timestamp_utc,x,y,z,yaw,changed");log.Flush();}catch(Exception ex){MessageBox.Show(this,"Не удалось создать журнал: "+ex.Message);return;}
   attempts=successes=changes=timeouts=blocked=0;previous=null;lastSuccess=-1;focusSince=-1;phase=0;oldSequence=Native.GetClipboardSequenceNumber();running=true;keyPicker.Enabled=interval.Enabled=mode.Enabled=false;toggle.Text="Пауза · F8";state.Text=mode.SelectedIndex==0?"Без нажатий: жду новые координаты в буфере из SCUM.":"Переключись в SCUM; запросы начнутся через 2 секунды.";nextAttempt=clock.ElapsedMilliseconds+2000;
  }
  void Stop(string reason){if(live!=null)live.SendingAllowed=false;mini.MotionStatus="Остановлено";running=false;ReleaseKeys();phase=0;focusSince=-1;keyPicker.Enabled=interval.Enabled=mode.Enabled=true;toggle.Text="Старт · F8";if(log!=null){log.Dispose();log=null;}state.Text=reason;}
  bool IsGame(out uint pid,out IntPtr window){window=Native.GetForegroundWindow();Native.GetWindowThreadProcessId(window,out pid);if(pid==0)return false;try{using(Process p=Process.GetProcessById((int)pid)){return Native.IsScumName(p.ProcessName);}}catch{return false;}}
  bool InputBusy(){int[] keys={0x10,0x11,0x12,0x5B,0x5C,0x01,0x02,copyKey};foreach(int k in keys)if(Native.Down(k))return true;return false;}
  void ReleaseKeys(){List<Native.INPUT> keys=new List<Native.INPUT>();if(keyOwned)keys.Add(Native.Key(ownedKey,true));if(ctrlOwned)keys.Add(Native.Key(0x11,true));if(keys.Count>0)Native.Send(keys.ToArray());keyOwned=ctrlOwned=false;}
  readonly ZoomKeys zoomKeys=new ZoomKeys();
  void UpdateZoomKeys(long now){uint pid;IntPtr window;bool allowed=!movingWindow&&mapWanted&&mini.Visible&&IsGame(out pid,out window)&&!Native.CursorVisible()&&!Native.Down(0x10)&&!Native.Down(0x11)&&!Native.Down(0x12);int direction=zoomKeys.Update(allowed,Native.Down(0x26),Native.Down(0x28),now);if(direction!=0)mini.ChangeZoom(direction>0);}
  string actionNotice;long actionNoticeUntil;
  void ShowActionNotice(string message){actionNotice=message;actionNoticeUntil=clock.ElapsedMilliseconds+8000;state.Text=message;}
  void Tick(){try{TickCore();}finally{if(actionNotice!=null&&clock.ElapsedMilliseconds<actionNoticeUntil&&!IsDisposed)state.Text=actionNotice;else actionNotice=null;}}
  void TickCore(){
   var newestAllies=allyInbox.Take(live);if(newestAllies!=null&&!mini.IsDisposed){mini.Allies=newestAllies;mini.Invalidate();}
   long now=clock.ElapsedMilliseconds;uint pid;IntPtr window;if(now>=nextFocus){cachedActive=IsGame(out cachedPid,out cachedWindow);nextFocus=now+150;}pid=cachedPid;window=cachedWindow;bool active=cachedActive;if(!movingWindow&&now>=nextUi){nextUi=now+250;foregroundLabel.Text=active?"SCUM активно · PID "+pid:"SCUM не в фокусе — команды не отправляются";
   metrics.Text="Попытки: "+attempts+"   Получено: "+successes+"   Изменений: "+changes+"   Нет ответа: "+timeouts+"\r\nПоследние координаты: "+(lastSuccess<0?"ещё не получены":((now-lastSuccess)/1000.0).ToString("0.0")+" с назад")+"   Пропуски ввода: "+blocked;
   }
   UpdateMenuVisibility(active,now);UpdateZoomKeys(now);if(live!=null)live.SendingAllowed=running&&active&&!menuCursor&&!movingWindow;
   if(!running)return;
   if(menuCursor){state.Text="Меню игры: карта скрыта, копирование на паузе.";return;}
   if(mode.SelectedIndex==0){uint seq=Native.GetClipboardSequenceNumber();if(active&&seq!=oldSequence){oldSequence=seq;try{if(Clipboard.ContainsText(TextDataFormat.UnicodeText)){Position copied;if(Position.TryParse(Clipboard.GetText(TextDataFormat.UnicodeText),out copied))Record(copied);}}catch(ExternalException){}}return;}
   if(phase==1){if(!active||pid!=gamePid||window!=gameWindow||now>=keyUpAt){ReleaseKeys();phase=active&&pid==gamePid&&window==gameWindow?2:0;clipboardUntil=now+650;}else return;}
   if(!active){focusSince=-1;phase=0;state.Text="Пауза: переключись в SCUM.";return;}
   if(focusSince<0){focusSince=now;nextAttempt=Math.Max(nextAttempt,now+2000);}
   if(phase==2){if(pid!=gamePid||window!=gameWindow){phase=0;return;}if(Native.GetClipboardSequenceNumber()!=oldSequence){
     try{if(Clipboard.ContainsText(TextDataFormat.UnicodeText)){string text=Clipboard.GetText(TextDataFormat.UnicodeText);Position pos;if(Position.TryParse(text,out pos)){Record(pos);phase=0;return;}}}catch(ExternalException){}
    }if(now>=clipboardUntil){timeouts++;phase=0;state.Text="Нет свежих координат. Проверь назначение клавиш и закрой чат/меню.";}return;}
   if(now<nextAttempt)return;if(InputBusy()){blocked++;nextAttempt=now+150;state.Text="Жду отпускания Shift / Ctrl / Alt / мыши.";return;}
   if(!IsGame(out pid,out window))return;
   gamePid=pid;gameWindow=window;oldSequence=Native.GetClipboardSequenceNumber();ownedKey=copyKey;
   List<Native.INPUT> down=new List<Native.INPUT>();down.Add(Native.Key(ownedKey,false));keyOwned=true;
   attempts++;nextAttempt=now+100;
   if(!Native.Send(down.ToArray())){ReleaseKeys();Stop("Windows отклонила ввод. Режим остановлен.");return;}
   phase=1;keyUpAt=now+45;state.Text="Запрос координат… F9 — остановить.";
  }
  void ReadClipboard(){uint seq=Native.GetClipboardSequenceNumber();if(seq==oldSequence)return;try{if(Clipboard.ContainsText(TextDataFormat.UnicodeText)){Position pos;if(Position.TryParse(Clipboard.GetText(TextDataFormat.UnicodeText),out pos)){oldSequence=seq;Record(pos);if(phase==1){ReleaseKeys();}phase=0;}}}catch(ExternalException){}}
  readonly NumericUpDown mapX=new NumericUpDown(),mapY=new NumericUpDown(),mapSize=new NumericUpDown();bool layoutBusy;
  readonly Timer layoutSave=new Timer();
  void QueueLayoutSave(){layoutSave.Stop();layoutSave.Start();}
  void BuildMapSettings(){
   layoutSave.Interval=300;layoutSave.Tick+=delegate{layoutSave.Stop();SaveMapSettings();};
   MakeLabel("ПОЛОЖЕНИЕ И РАЗМЕР МИНИКАРТЫ",20,822,550,25,10,true);
   MakeLabel("X на экране",20,853,150,22,9,false);MakeLabel("Y на экране",200,853,150,22,9,false);MakeLabel("Размер, пикс.",380,853,170,22,9,false);
   NumericUpDown[] fields={mapX,mapY,mapSize};int[] x={20,200,380};
   for(int i=0;i<3;i++){fields[i].SetBounds(x[i],880,165,28);fields[i].Minimum=i==2?160:-10000;fields[i].Maximum=i==2?1000:10000;fields[i].Increment=i==2?10:5;Controls.Add(fields[i]);}
   LoadMapSettings();SyncMapSettings();
   foreach(NumericUpDown n in fields)n.ValueChanged+=delegate{if(layoutBusy)return;mini.SetBounds((int)mapX.Value,(int)mapY.Value,(int)mapSize.Value,(int)mapSize.Value);mini.Invalidate();};
   mini.LocationChanged+=delegate{SyncMapSettings();QueueLayoutSave();};mini.SizeChanged+=delegate{SyncMapSettings();QueueLayoutSave();};
   Button move=new RoundedButton();move.SetBounds(20,923,260,35);move.Text="Переместить карту мышью · F7";StyleButton(move);move.Click+=delegate{if(mini.Locked)mini.ToggleLock();mini.Show();};Controls.Add(move);
   Button reset=new RoundedButton();reset.SetBounds(300,923,250,35);reset.Text="Вернуть карту на экран";StyleButton(reset);reset.Click+=delegate{Rectangle a=Screen.FromControl(this).WorkingArea;mini.SetBounds(a.Left+25,a.Top+120,350,350);SyncMapSettings();};Controls.Add(reset);
   MakeLabel("X/Y работают и при сквозных кликах. F7 — снова заблокировать мышь.\r\nВсе настройки доступны в одном окне.",20,971,550,50,9,false);
  }
  void BuildOpacity(){
   MakeLabel("Непрозрачность карты",610,320,350,25,11,true);
   DarkComboBox opacity=new DarkComboBox();opacity.Name="mapOpacity";opacity.SetBounds(610,357,180,30);opacity.DropDownStyle=ComboBoxStyle.DropDownList;opacity.Items.AddRange(new object[]{"100%","75%","50%","25%"});
   int selected=0;try{Int32.TryParse(File.ReadAllText(UserSettings.PathFor("opacity.txt")),out selected);}catch{}selected=Math.Max(0,Math.Min(3,selected));opacity.SelectedIndex=selected;mini.SetMapOpacity(1-selected*.25);
   opacity.SelectedIndexChanged+=delegate{mini.SetMapOpacity(1-opacity.SelectedIndex*.25);try{File.WriteAllText(UserSettings.PathFor("opacity.txt"),opacity.SelectedIndex.ToString());}catch{}};Controls.Add(opacity);
  }
  void SyncMapSettings(){layoutBusy=true;mapX.Value=Math.Max(mapX.Minimum,Math.Min(mapX.Maximum,mini.Left));mapY.Value=Math.Max(mapY.Minimum,Math.Min(mapY.Maximum,mini.Top));mapSize.Value=Math.Max(mapSize.Minimum,Math.Min(mapSize.Maximum,mini.Width));layoutBusy=false;}
  void LoadMapSettings(){try{string[] v=File.ReadAllText(UserSettings.PathFor("layout.txt")).Split(new char[]{','},StringSplitOptions.None);int x,y,size;if(v.Length==3&&Int32.TryParse(v[0],out x)&&Int32.TryParse(v[1],out y)&&Int32.TryParse(v[2],out size)){size=Math.Max(160,Math.Min(1000,size));Rectangle bounds=new Rectangle(x,y,size,size);bool visible=false;foreach(Screen screen in Screen.AllScreens)if(screen.WorkingArea.IntersectsWith(bounds))visible=true;if(visible)mini.SetBounds(x,y,size,size);}}catch{}}
  void SaveMapSettings(){try{File.WriteAllText(UserSettings.PathFor("layout.txt"),mini.Left+","+mini.Top+","+mini.Width);}catch{}}
  void Record(Position pos){bool changed=previous!=null&&(Math.Abs(pos.X-previous.X)>.01||Math.Abs(pos.Y-previous.Y)>.01||Math.Abs(pos.Z-previous.Z)>.01);successes++;if(changed)changes++;lastSuccess=clock.ElapsedMilliseconds;previous=pos;if(live!=null)live.Observe(pos,mini.NorthYaw);mini.SetPosition(pos);RefreshNavigationDistances();positionLabel.Text=pos.Display()+"   "+MapMath.Sector(MapMath.NormX(pos.X),MapMath.NormY(pos.Y));state.Text=changed?"Координаты обновились во время движения.":"Координаты получены. Пробеги несколько метров.";
   try{if(log!=null){log.WriteLine(DateTime.UtcNow.ToString("o")+","+pos.Number(pos.X)+","+pos.Number(pos.Y)+","+pos.Number(pos.Z)+","+(pos.HasYaw?pos.Number(pos.Yaw):"")+","+(changed?"1":"0"));log.Flush();}}catch(Exception){Stop("Ошибка записи журнала. Считывание остановлено.");}
  }
  public void Abort(string reason){Stop(reason);}
  void OpenLogs(){try{string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"logs");Directory.CreateDirectory(dir);Process.Start("explorer.exe",dir);}catch(Exception ex){MessageBox.Show(this,ex.Message);}}
 }
 public static class Program {
  static void Require(bool condition,string name){if(!condition)throw new Exception(name);}
  public static int SelfTest(){List<string> lines=new List<string>();try{Position p;
   Require(Position.TryParse("{X=286201.500 Y=-733498.500 Z=9776.545|P=341.77 Y=225.36 R=0.00}",out p)&&p.Y==-733498.5,"Full SCUM string and second Y");lines.Add("PASS full SCUM coordinates and rotation separation");
   Require(Position.TryParse("X=-1.25e3 Y=2.5E2 Z=0",out p)&&p.X==-1250&&p.Y==250,"Scientific format");lines.Add("PASS scientific format");
   Require(Position.TryParse("{X=1,5 Y=-2,25 Z=3}",out p)&&p.X==1.5,"Decimal comma");lines.Add("PASS decimal comma");
   foreach(string bad in new string[]{"","hello","X=1 Y=2","X=NaN Y=2 Z=3","X=1 Y=2 Z=Infinity","X=2000001 Y=0 Z=0","X=1 Y=2 Z=3\r\nsecret"})Require(!Position.TryParse(bad,out p),"Reject invalid coordinates");lines.Add("PASS malformed and out-of-range rejection");
   Require(Native.IsScumName("SCUM-Win64-Shipping")&&Native.IsScumName("SCUM")&&!Native.IsScumName("scum-map")&&!Native.IsScumName("notepad"),"Process allowlist");lines.Add("PASS exact process-name allowlist");
   int size=Marshal.SizeOf(typeof(Native.INPUT));Require(size==(IntPtr.Size==8?40:28),"Native INPUT layout");lines.Add("PASS native INPUT structure size");
   Require(Position.TryParse("{X=1 Y=2 Z=3|P=0 Y=270 R=0}",out p)&&p.HasYaw&&p.Yaw==270&&p.Y==2,"Yaw parser");lines.Add("PASS separate world Y / rotation Yaw");
   Require(Math.Abs(MapMath.NormX(MapMath.Left))<1e-9&&Math.Abs(MapMath.NormX(MapMath.Right)-1)<1e-9&&Math.Abs(MapMath.NormY(MapMath.Top))<1e-9,"Map bounds");
   Require(MapMath.Sector(.1,.1)=="D4"&&MapMath.Sector(.9,.9)=="Z0","Sector grid");
   Require(MapMath.DeltaAngle(350,10)==20&&MapMath.DeltaAngle(10,350)==-20,"Heading wraps");lines.Add("PASS map bounds, sectors and short rotation interpolation");
   HubSnapshot snapshot=HubClient.Parse("{\"member\":{\"nick\":\"tester\"},\"posts\":[],\"markers\":[{\"id\":1,\"x\":0.2,\"y\":0.3,\"name\":\"Base\",\"type\":\"base\"},{\"x\":999,\"y\":0.1}]}");Require(snapshot.Markers.Count==1&&snapshot.Markers[0].Name=="Base","HUB snapshot parser");lines.Add("PASS HUB marker validation");
   MarkerMergeTests.Run();lines.Add("PASS HUB linked markers, orphan filtering, post coordinates, titles and deletion");
   HubClient.TestRefreshStash();lines.Add("PASS selected stash refresh, invalid marker rejection, large post IDs, session expiry and server errors");
   HubClient.TestCreateMarker();lines.Add("PASS four marker types, coordinate round-trip, invalid input, access revocation and no write retries");
   HubClient.TestProtocol();lines.Add("PASS read-only HUB requests, marker additions/deletions and session expiry");
   Require(MapMath.Direction(0)=="Север"&&MapMath.Direction(30)=="СВ"&&MapMath.Direction(180)=="Юг"&&MapMath.Direction(225)=="ЮЗ"&&MapMath.Direction(359)=="Север","Compass directions");lines.Add("PASS compass labels and wraparound");
   Require(MapVisibility.ShouldShow(true,false,true)&&!MapVisibility.ShouldShow(true,false,false)&&!MapVisibility.ShouldShow(false,false,true)&&!MapVisibility.ShouldShow(true,true,true),"Overlay focus/manual/menu visibility");lines.Add("PASS overlay focus, manual hide and inventory hide");
   DateTime stamp=DateTime.UtcNow;List<AllyPosition> nearby=AllyLive.ParsePeers("{\"self\":\"me\",\"now\":10000,\"peers\":[{\"id\":\"me\",\"nick\":\"Self\",\"x\":1,\"y\":2,\"heading\":0,\"at\":10000},{\"id\":\"friend\",\"nick\":\"Ally\",\"x\":100,\"y\":200,\"heading\":225,\"at\":5000}]}",stamp);Require(nearby.Count==1&&nearby[0].Nick=="Ally"&&nearby[0].Heading==225&&Math.Abs((stamp-nearby[0].Observed).TotalSeconds-5)<.01,"Live allies parser");lines.Add("PASS ally identity, self exclusion, heading and stale sample age");
   Position still=new Position{X=100,Y=200,Z=3};Require(!AllyLive.Changed(still,still,90,90)&&AllyLive.Changed(still,new Position{X=102,Y=200,Z=3},90,90)&&AllyLive.Changed(still,still,90,91),"Movement-only packets");lines.Add("PASS stationary sample suppression and position/heading changes");
   HubClient.TestRenewal();lines.Add("PASS fixed twelve-hour session without renewal");
   NavigationTests.Run(lines);
   lines.Add("ALL TESTS PASSED");File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.txt"),lines.ToArray());return 0;
  }catch(Exception ex){lines.Add("FAIL "+ex.Message);File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.txt"),lines.ToArray());return 1;}}
  [STAThread] public static void Main(string[] args){if(args.Length>0&&args[0]=="--ui-test"){ProbeForm.UiTest=true;try{Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);using(ProbeForm testForm=new ProbeForm())testForm.TestCompactUI();Console.WriteLine("COMPACT UI TESTS PASSED");File.WriteAllText("ui-test.txt","COMPACT UI TESTS PASSED");}catch(Exception ex){Console.WriteLine(ex.ToString());File.WriteAllText("ui-test.txt",ex.ToString());Environment.ExitCode=1;}return;}if(args.Length==5&&args[0]=="--route-check"){Environment.ExitCode=NavigationTests.ExportCheck(args);return;}if(args.Length>0&&args[0]=="--self-test"){Environment.ExitCode=SelfTest();return;}
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);ProbeForm form=new ProbeForm();Application.ThreadException+=delegate(object sender,System.Threading.ThreadExceptionEventArgs e){form.Abort("Ошибка; считывание остановлено.");MessageBox.Show("Ошибка: "+e.Exception.Message+"\nЗакрой программу и отправь описание ошибки.");Application.Exit();};Application.Run(form);
  }
 }
}
