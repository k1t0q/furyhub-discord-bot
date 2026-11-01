using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ScumPositionProbe {
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
  AllyLive live;readonly CheckBox sharePosition=new CheckBox();
  readonly MiniMapForm mini=new MiniMapForm();HubClient hub;int hubGeneration;bool syncing;readonly Button refreshMarkers=new Button();
  readonly TextBox hubNick=new TextBox(),hubCode=new TextBox();readonly ComboBox hubRole=new ComboBox();readonly CheckBox adminLogin=new CheckBox();readonly Label hubState=new Label();readonly Button hubLogin=new Button();
  void BuildHubUI(){
   MakeLabel("МЕТКИ SCUM HUB · обновление по кнопке",20,562,550,25,10,true);
   MakeLabel("Ник",20,593,130,20,9,false);MakeLabel("Роль",170,593,130,20,9,false);MakeLabel("Код входа",325,593,240,20,9,false);
   hubNick.SetBounds(20,615,135,27);hubNick.Text="k1t0";Controls.Add(hubNick);
   hubRole.SetBounds(170,615,140,27);hubRole.DropDownStyle=ComboBoxStyle.DropDownList;hubRole.Items.AddRange(new object[]{"Rifler","Sniper","Bomber","Locker","Builder","Helper"});hubRole.SelectedIndex=0;Controls.Add(hubRole);
   hubCode.SetBounds(325,615,245,27);hubCode.UseSystemPasswordChar=true;Controls.Add(hubCode);
   adminLogin.SetBounds(20,649,550,24);adminLogin.Text="Личный вход администратора (код тот же, что на сайте)";adminLogin.Checked=true;Controls.Add(adminLogin);
   hubLogin.SetBounds(20,682,170,34);hubLogin.Text="Подключить HUB";StyleButton(hubLogin);hubLogin.Click+=async delegate{await LoginHub();};Controls.Add(hubLogin);
   Button logout=new Button();logout.SetBounds(200,682,110,34);logout.Text="Отключить";StyleButton(logout);logout.Click+=delegate{DisconnectHub();};Controls.Add(logout);
   hubState.SetBounds(20,722,550,40);hubState.Text="Вводи код здесь, не присылай его в чат. Код не сохраняется.";Controls.Add(hubState);
   Button show=new Button();show.SetBounds(20,770,170,34);show.Text="Карта · F7";StyleButton(show);show.Click+=delegate{ToggleMini();};Controls.Add(show);
   Button locked=new Button();locked.SetBounds(200,770,170,34);locked.Text="Сквозь клики · F7";StyleButton(locked);locked.Click+=delegate{mini.ToggleLock();};Controls.Add(locked);
   Button north=new Button();north.SetBounds(380,770,190,34);north.Text="Смотрю на север · F11";StyleButton(north);north.Click+=delegate{CalibrateNorth();};Controls.Add(north);
   refreshMarkers.Text="Обновить метки";StyleButton(refreshMarkers);refreshMarkers.Enabled=false;refreshMarkers.Click+=async delegate{await RefreshHub();};Controls.Add(refreshMarkers);
   LoadCalibration();Shown+=delegate{uint pid;IntPtr win;bool game=IsGame(out pid,out win);focusAllowed=game||pid==appPid;ApplyVisibility();};
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
   }catch(Exception ex){attempt.Dispose();if(!IsDisposed&&version==hubGeneration)hubState.Text=ex.Message;}finally{if(!IsDisposed){hubCode.Clear();hubLogin.Enabled=true;}}
  }
  async System.Threading.Tasks.Task RefreshHub(){if(hub==null||syncing)return;syncing=true;refreshMarkers.Enabled=false;refreshMarkers.Text="Обновляю…";HubClient current=hub;int version=hubGeneration;
   try{HubSnapshot snapshot=await current.Snapshot();if(IsDisposed||version!=hubGeneration||current!=hub)return;mini.Markers=snapshot.Markers;UpdateNavigationMarkers();mini.LastMarkers=DateTime.UtcNow;mini.SyncEnabled=true;mini.SyncStatus="HUB · "+snapshot.Markers.Count+" меток";hubState.Text="HUB: "+snapshot.Nick+" · обновлено "+DateTime.Now.ToString("HH:mm:ss");}
   catch(HubExpiredException){if(!IsDisposed&&version==hubGeneration){DisconnectHub();hubState.Text="Сессия истекла или доступ отозван. Войди снова.";}}
   catch(Exception){if(!IsDisposed&&version==hubGeneration){mini.SyncStatus="HUB: нет связи · метки устарели";hubState.Text="Нет связи с HUB. Нажми «Обновить метки», чтобы повторить.";}}
   finally{syncing=false;if(!IsDisposed){refreshMarkers.Enabled=hub!=null;refreshMarkers.Text="Обновить метки";}}
  }
  void DisconnectHub(){mini.Navigation.Cancel("Маршрут отменён: HUB отключён.");refreshMarkers.Enabled=false;if(live!=null){live.Dispose();live=null;}mini.Allies=new List<AllyPosition>();hubGeneration++;if(hub!=null){hub.Dispose();hub=null;}mini.Markers=new List<HubMarker>();UpdateNavigationMarkers();mini.SyncEnabled=false;mini.SyncStatus="HUB не подключён";hubCode.Clear();hubState.Text="Отключено. Метки удалены из миникарты.";}

  readonly Timer timer=new Timer();readonly Stopwatch clock=Stopwatch.StartNew();
  readonly Label state=new Label(),positionLabel=new Label(),metrics=new Label(),foregroundLabel=new Label();
  readonly Button toggle=new Button();readonly NumericUpDown interval=new NumericUpDown();readonly ComboBox mode=new ComboBox();
  readonly CheckBox top=new CheckBox();
  ushort copyKey=0x79;readonly Button keyPicker=new Button();bool movingWindow;
  bool running,ctrlOwned,keyOwned,hotkeysOk;long nextUi,nextFocus;uint cachedPid;IntPtr cachedWindow;bool cachedActive;ushort ownedKey;int phase;uint oldSequence,gamePid;
  IntPtr gameWindow=IntPtr.Zero;long nextAttempt,keyUpAt,clipboardUntil,lastSuccess=-1,focusSince=-1;
  int attempts,successes,changes,timeouts,blocked;Position previous;StreamWriter log;string logPath="";
  public ProbeForm(){
   Text="SCUM HUB";ClientSize=new Size(1010,820);MinimumSize=new Size(1026,859);AutoScroll=false;AutoScaleMode=AutoScaleMode.Dpi;
   DoubleBuffered=true;SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);
   using(Stream icon=typeof(ProbeForm).Assembly.GetManifestResourceStream("ScumPositionProbe.SH.ico")){if(icon!=null)Icon=new Icon(icon);}
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
   Button stop=new Button();stop.SetBounds(200,408,170,42);stop.Text="Стоп · F9";StyleButton(stop);stop.Click+=delegate{Stop("Остановлено.");};Controls.Add(stop);
   Button logs=new Button();logs.SetBounds(380,408,190,42);logs.Text="Открыть журнал";StyleButton(logs);logs.Click+=delegate{OpenLogs();};Controls.Add(logs);
   top.SetBounds(20,466,550,25);top.Text="Поверх остальных окон";top.Checked=false;TopMost=false;top.CheckedChanged+=delegate{TopMost=top.Checked;};Controls.Add(top);
   MakeLabel("Координаты остаются на ПК. HUB подключается только для чтения меток.\r\nБуфер заменяется координатами; закрой чат и меню игры.",20,504,550,50,9,false);
   BuildHubUI();int before=Controls.Count;BuildMapSettings();for(int i=before;i<Controls.Count;i++){Control c=Controls[i];c.Left=610+(c.Left-20)*380/550;c.Top-=732;c.Width=c.Width*380/550;}BuildOpacity();MinimalLayout();BuildKeyPicker();BuildAutoHide();BuildZoomUI();BuildSharing();BuildNavigationUI();
   timer.Interval=25;timer.Tick+=delegate{Tick();};timer.Start();FormClosing+=delegate{Stop("Закрытие");timer.Stop();layoutSave.Stop();SaveMapSettings();DisconnectHub();mini.Dispose();Native.RemoveClipboardFormatListener(Handle);for(int id=1;id<=5;id++)Native.UnregisterHotKey(Handle,id);};
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
   Place(hubState,24,355,311,43);hubState.Font=new Font("Segoe UI",9);hubState.Text="Введи код своего аккаунта HUB.";
   MakeLabel("ОТСЛЕЖИВАНИЕ",24,416,320,24,10,true);
   Place(mode,24,451,311,29);
   Place(toggle,24,496,150,40);Place(state,24,548,650,38);state.Font=new Font("Segoe UI",9);
   MakeLabel("КАРТА",385,108,290,24,10,true);
   MakeLabel("Положение X / Y",385,143,280,22,9,false);Place(mapX,385,167,130,29);Place(mapY,535,167,130,29);
   MakeLabel("Размер",385,211,280,22,9,false);Place(mapSize,385,235,280,29);
   foreach(Control c in Controls){if(c is ComboBox&&c!=mode&&c!=hubRole){Place(c,385,308,280,29);}}
   MakeLabel("Непрозрачность",385,284,280,22,9,false);
   foreach(Control c in Controls){if(!(c is Button))continue;switch(c.Text){
    case "Отключить":Place(c,228,308,107,36);break;
    case "Карта · F7":c.Text="Показать / скрыть карту · F7";Place(c,385,365,280,36);break;
    case "Вернуть карту на экран":c.Text="Сбросить положение";Place(c,385,414,280,36);break;
    case "Смотрю на север · F11":c.Text="Настроить север · F11";Place(c,385,463,280,36);break;
    case "Стоп · F9":Place(c,185,496,150,40);break;
   }}
  }
  readonly ComboBox navigationGoal=new ComboBox();readonly Button navigationStart=new Button(),navigationCancel=new Button();readonly Label navigationState=new Label();
  sealed class NavigationChoice {public HubMarker Marker;public override string ToString(){return Marker.Name+" · "+MapMath.Sector(Marker.X,Marker.Y);}}
  void BuildNavigationUI(){
   ClientSize=new Size(700,720);MinimumSize=MaximumSize=new Size(716,759);state.Top=674;
   MakeLabel("НАВИГАТОР",24,549,311,22,10,true);
   navigationGoal.SetBounds(24,575,311,30);navigationGoal.DropDownWidth=580;navigationGoal.DropDownStyle=ComboBoxStyle.DropDownList;navigationGoal.BackColor=Color.FromArgb(34,46,52);navigationGoal.ForeColor=ForeColor;navigationGoal.SelectedIndexChanged+=delegate{UpdateNavigationState();};Controls.Add(navigationGoal);
   navigationStart.Text="Проложить маршрут";navigationStart.SetBounds(24,617,192,34);StyleButton(navigationStart);Controls.Add(navigationStart);
   navigationCancel.Text="Отменить";navigationCancel.SetBounds(228,617,107,34);StyleButton(navigationCancel);Controls.Add(navigationCancel);
   navigationState.SetBounds(385,617,280,50);navigationState.Font=new Font("Segoe UI",9);Controls.Add(navigationState);
   navigationStart.Click+=async delegate{NavigationChoice choice=navigationGoal.SelectedItem as NavigationChoice;if(choice==null)return;if(mini.Current==null){navigationState.Text="Сначала включи отслеживание и получи позицию в игре.";return;}await mini.Navigation.Plan(mini.Current,choice.Marker);mini.Navigation.Update(mini.Current);};
   navigationCancel.Click+=delegate{mini.Navigation.Cancel("Маршрут отменён.");};
   mini.Navigation.Changed+=delegate{if(!IsDisposed){UpdateNavigationState();mini.Invalidate();}};
   UpdateNavigationState();
  }
  void UpdateNavigationState(){navigationState.Text=mini.Navigation.Status;navigationStart.Enabled=navigationGoal.SelectedIndex>=0&&!mini.Navigation.Busy;navigationCancel.Enabled=mini.Navigation.Route!=null||mini.Navigation.Busy;}
  void UpdateNavigationMarkers(){
   NavigationChoice previousChoice=navigationGoal.SelectedItem as NavigationChoice;string selected=previousChoice==null?null:previousChoice.Marker.Id;
   navigationGoal.BeginUpdate();navigationGoal.Items.Clear();List<HubMarker> sorted=new List<HubMarker>(mini.Markers);sorted.Sort((a,b)=>StringComparer.CurrentCultureIgnoreCase.Compare(a.Name,b.Name));
   foreach(HubMarker marker in sorted){int i=navigationGoal.Items.Add(new NavigationChoice{Marker=marker});if(marker.Id==selected)navigationGoal.SelectedIndex=i;}
   if(navigationGoal.SelectedIndex<0&&navigationGoal.Items.Count>0)navigationGoal.SelectedIndex=0;navigationGoal.EndUpdate();
   string activeId=mini.Navigation.GoalId;if(activeId!=null){HubMarker active=mini.Markers.Find(m=>m.Id==activeId);if(active==null)mini.Navigation.Cancel("Метка удалена из HUB. Маршрут отменён.");else mini.Navigation.RefreshTarget(active,mini.Current);}
   UpdateNavigationState();
  }
  void BuildSharing(){sharePosition.SetBounds(24,390,320,24);sharePosition.Text="Делиться позицией с отрядом";sharePosition.Font=new Font("Segoe UI",9);sharePosition.Checked=true;sharePosition.CheckedChanged+=delegate{if(live!=null)live.Sharing=sharePosition.Checked;};Controls.Add(sharePosition);}
  void StartAllies(){if(live!=null)live.Dispose();AllyLive current=new AllyLive(hub.Cookies);live=current;current.Sharing=sharePosition.Checked;
   current.Updated=delegate(List<AllyPosition> peers){try{if(!IsDisposed&&IsHandleCreated)BeginInvoke((Action)delegate{if(live==current&&!mini.IsDisposed){mini.Allies=peers;mini.Invalidate();}});}catch{}};
   current.Status=delegate(string status){try{if(!IsDisposed&&IsHandleCreated)BeginInvoke((Action)delegate{if(live==current)hubState.Text=status;});}catch{}};current.Start();
  }
  void BuildZoomUI(){
   ClientSize=new Size(700,650);MinimumSize=MaximumSize=new Size(716,689);
   foreach(Control c in Controls){if(c is Button&&(c.Text=="Показать / скрыть карту · F7"||c.Text=="Сбросить положение"||c.Text=="Настроить север · F11"))c.Top+=72;}
   autoHide.Top+=72;state.Top=608;
   MakeLabel("Масштаб карты",385,352,280,22,9,false);
   Button less=new Button(),more=new Button();less.Text="−";more.Text="+";less.SetBounds(385,380,58,34);more.SetBounds(607,380,58,34);StyleButton(less);StyleButton(more);Controls.Add(less);Controls.Add(more);
   Label value=MakeLabel("",452,386,146,25,10,false);value.TextAlign=ContentAlignment.TopCenter;
   try{double saved;if(Double.TryParse(File.ReadAllText(UserSettings.PathFor("zoom.txt")),NumberStyles.Float,CultureInfo.InvariantCulture,out saved))mini.SetZoomRadius(saved);}catch{}
   Action update=delegate{value.Text="Радиус "+mini.ViewRadius.ToString("0")+" м";};update();
   mini.ZoomChanged+=delegate{update();try{File.WriteAllText(UserSettings.PathFor("zoom.txt"),mini.ViewRadius.ToString(CultureInfo.InvariantCulture));}catch{}};
   less.Click+=delegate{mini.ChangeZoom(false);};more.Click+=delegate{mini.ChangeZoom(true);};
  }
  void BuildAutoHide(){autoHide.SetBounds(385,511,290,27);autoHide.Text="Скрывать карту при курсоре";autoHide.Font=new Font("Segoe UI",9);autoHide.Checked=true;try{autoHide.Checked=File.ReadAllText(UserSettings.PathFor("auto-hide.txt")).Trim()!="0";}catch{}autoHide.CheckedChanged+=delegate{try{File.WriteAllText(UserSettings.PathFor("auto-hide.txt"),autoHide.Checked?"1":"0");}catch{}if(!autoHide.Checked){menuCursor=autoHidden=lastCursor=false;ApplyVisibility();}};Controls.Add(autoHide);}
  void BuildKeyPicker(){
   try{int key;if(Int32.TryParse(File.ReadAllText(UserSettings.PathFor("copy-key.txt")),out key)&&KeyCapture.Allowed((Keys)key))copyKey=(ushort)key;}catch{}
   mode.Items[0]="Вручную";mode.Items[1]="Автоматически";mode.Width=155;
   keyPicker.SetBounds(190,451,145,29);keyPicker.Text="Клавиша: "+((Keys)copyKey).ToString();StyleButton(keyPicker);
   keyPicker.Click+=delegate{using(KeyCapture dialog=new KeyCapture()){if(dialog.ShowDialog(this)!=DialogResult.OK)return;copyKey=(ushort)dialog.Selected;keyPicker.Text="Клавиша: "+dialog.Selected;try{File.WriteAllText(UserSettings.PathFor("copy-key.txt"),copyKey.ToString());}catch{}}};Controls.Add(keyPicker);
  }
  Label MakeLabel(string text,int x,int y,int w,int h,int size,bool bold){Label l=new Label();l.Text=text;l.SetBounds(x,y,w,h);l.Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular);Controls.Add(l);return l;}
  void StyleButton(Button b){b.FlatStyle=FlatStyle.Flat;b.BackColor=Color.FromArgb(34,46,52);b.ForeColor=ForeColor;}
  protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Native.AddClipboardFormatListener(Handle);bool a=Native.RegisterHotKey(Handle,1,0x4000,0x77),b=Native.RegisterHotKey(Handle,2,0x4000,0x78);Native.RegisterHotKey(Handle,3,0x4000,0x76);Native.RegisterHotKey(Handle,5,0x4000,0x7A);hotkeysOk=a&&b;if(!hotkeysOk){Native.UnregisterHotKey(Handle,1);Native.UnregisterHotKey(Handle,2);state.Text="F8/F9 заняты. Закрой программу, освободившую клавиши, и запусти снова.";}}
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
  void Tick(){
   long now=clock.ElapsedMilliseconds;uint pid;IntPtr window;if(now>=nextFocus){cachedActive=IsGame(out cachedPid,out cachedWindow);nextFocus=now+150;}pid=cachedPid;window=cachedWindow;bool active=cachedActive;if(!movingWindow&&now>=nextUi){nextUi=now+250;foregroundLabel.Text=active?"SCUM активно · PID "+pid:"SCUM не в фокусе — команды не отправляются";
   metrics.Text="Попытки: "+attempts+"   Получено: "+successes+"   Изменений: "+changes+"   Нет ответа: "+timeouts+"\r\nПоследние координаты: "+(lastSuccess<0?"ещё не получены":((now-lastSuccess)/1000.0).ToString("0.0")+" с назад")+"   Пропуски ввода: "+blocked;
   }
   UpdateMenuVisibility(active,now);if(live!=null)live.SendingAllowed=running&&active&&!menuCursor&&!movingWindow;
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
   Button move=new Button();move.SetBounds(20,923,260,35);move.Text="Переместить карту мышью · F7";StyleButton(move);move.Click+=delegate{if(mini.Locked)mini.ToggleLock();mini.Show();};Controls.Add(move);
   Button reset=new Button();reset.SetBounds(300,923,250,35);reset.Text="Вернуть карту на экран";StyleButton(reset);reset.Click+=delegate{Rectangle a=Screen.FromControl(this).WorkingArea;mini.SetBounds(a.Left+25,a.Top+120,350,350);SyncMapSettings();};Controls.Add(reset);
   MakeLabel("X/Y работают и при сквозных кликах. F7 — снова заблокировать мышь.\r\nВсе настройки доступны в одном окне.",20,971,550,50,9,false);
  }
  void BuildOpacity(){
   MakeLabel("Непрозрачность карты",610,320,350,25,11,true);
   ComboBox opacity=new ComboBox();opacity.SetBounds(610,357,180,30);opacity.DropDownStyle=ComboBoxStyle.DropDownList;opacity.Items.AddRange(new object[]{"100%","75%","50%","25%"});
   int selected=0;try{Int32.TryParse(File.ReadAllText(UserSettings.PathFor("opacity.txt")),out selected);}catch{}selected=Math.Max(0,Math.Min(3,selected));opacity.SelectedIndex=selected;mini.SetMapOpacity(1-selected*.25);
   opacity.SelectedIndexChanged+=delegate{mini.SetMapOpacity(1-opacity.SelectedIndex*.25);try{File.WriteAllText(UserSettings.PathFor("opacity.txt"),opacity.SelectedIndex.ToString());}catch{}};Controls.Add(opacity);
  }
  void SyncMapSettings(){layoutBusy=true;mapX.Value=Math.Max(mapX.Minimum,Math.Min(mapX.Maximum,mini.Left));mapY.Value=Math.Max(mapY.Minimum,Math.Min(mapY.Maximum,mini.Top));mapSize.Value=Math.Max(mapSize.Minimum,Math.Min(mapSize.Maximum,mini.Width));layoutBusy=false;}
  void LoadMapSettings(){try{string[] v=File.ReadAllText(UserSettings.PathFor("layout.txt")).Split(new char[]{','},StringSplitOptions.None);int x,y,size;if(v.Length==3&&Int32.TryParse(v[0],out x)&&Int32.TryParse(v[1],out y)&&Int32.TryParse(v[2],out size)){size=Math.Max(160,Math.Min(1000,size));Rectangle bounds=new Rectangle(x,y,size,size);bool visible=false;foreach(Screen screen in Screen.AllScreens)if(screen.WorkingArea.IntersectsWith(bounds))visible=true;if(visible)mini.SetBounds(x,y,size,size);}}catch{}}
  void SaveMapSettings(){try{File.WriteAllText(UserSettings.PathFor("layout.txt"),mini.Left+","+mini.Top+","+mini.Width);}catch{}}
  void Record(Position pos){bool changed=previous!=null&&(Math.Abs(pos.X-previous.X)>.01||Math.Abs(pos.Y-previous.Y)>.01||Math.Abs(pos.Z-previous.Z)>.01);successes++;if(changed)changes++;lastSuccess=clock.ElapsedMilliseconds;previous=pos;mini.SetPosition(pos);if(live!=null)live.Observe(pos,mini.NorthYaw);positionLabel.Text=pos.Display()+"   "+MapMath.Sector(MapMath.NormX(pos.X),MapMath.NormY(pos.Y));state.Text=changed?"Координаты обновились во время движения.":"Координаты получены. Пробеги несколько метров.";
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
   HubClient.TestProtocol();lines.Add("PASS read-only HUB requests, marker additions/deletions and session expiry");
   Require(MapMath.Direction(0)=="Север"&&MapMath.Direction(30)=="СВ"&&MapMath.Direction(180)=="Юг"&&MapMath.Direction(225)=="ЮЗ"&&MapMath.Direction(359)=="Север","Compass directions");lines.Add("PASS compass labels and wraparound");
   Require(MapVisibility.ShouldShow(true,false,true)&&!MapVisibility.ShouldShow(true,false,false)&&!MapVisibility.ShouldShow(false,false,true)&&!MapVisibility.ShouldShow(true,true,true),"Overlay focus/manual/menu visibility");lines.Add("PASS overlay focus, manual hide and inventory hide");
   DateTime stamp=DateTime.UtcNow;List<AllyPosition> nearby=AllyLive.ParsePeers("{\"self\":\"me\",\"now\":10000,\"peers\":[{\"id\":\"me\",\"nick\":\"Self\",\"x\":1,\"y\":2,\"heading\":0,\"at\":10000},{\"id\":\"friend\",\"nick\":\"Ally\",\"x\":100,\"y\":200,\"heading\":225,\"at\":5000}]}",stamp);Require(nearby.Count==1&&nearby[0].Nick=="Ally"&&nearby[0].Heading==225&&Math.Abs((stamp-nearby[0].Observed).TotalSeconds-5)<.01,"Live allies parser");lines.Add("PASS ally identity, self exclusion, heading and stale sample age");
   Position still=new Position{X=100,Y=200,Z=3};Require(!AllyLive.Changed(still,still,90,90)&&AllyLive.Changed(still,new Position{X=102,Y=200,Z=3},90,90)&&AllyLive.Changed(still,still,90,91),"Movement-only packets");lines.Add("PASS stationary sample suppression and position/heading changes");
   NavigationTests.Run(lines);
   lines.Add("ALL TESTS PASSED");File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.txt"),lines.ToArray());return 0;
  }catch(Exception ex){lines.Add("FAIL "+ex.Message);File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.txt"),lines.ToArray());return 1;}}
  [STAThread] public static void Main(string[] args){if(args.Length==5&&args[0]=="--route-check"){Environment.ExitCode=NavigationTests.ExportCheck(args);return;}if(args.Length>0&&args[0]=="--self-test"){Environment.ExitCode=SelfTest();return;}
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);ProbeForm form=new ProbeForm();Application.ThreadException+=delegate(object sender,System.Threading.ThreadExceptionEventArgs e){form.Abort("Ошибка; считывание остановлено.");MessageBox.Show("Ошибка: "+e.Exception.Message+"\nЗакрой программу и отправь описание ошибки.");Application.Exit();};Application.Run(form);
  }
 }
}
