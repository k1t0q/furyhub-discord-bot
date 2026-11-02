using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace ScumPositionProbe {
 public sealed class AllyPosition {public string Id,Nick;public double X,Y,Heading;public bool HasHeading;public DateTime Observed;}
 public sealed class AllyLive:IDisposable {
  readonly HubClient hub;readonly CookieContainer cookies;readonly CancellationTokenSource cancel=new CancellationTokenSource();ClientWebSocket socket;
  public volatile bool Sharing=true;public volatile bool SendingAllowed;public Action<List<AllyPosition>> Updated;public Action<string> Status;
  readonly object gate=new object();string lastPacket;long revision,sentRevision;Position previousSample;double? previousHeading;
  public AllyLive(HubClient hub){this.hub=hub;this.cookies=hub.Cookies;}
  internal Func<DateTime> UtcNow=()=>DateTime.UtcNow;
  public static bool Visible(DateTime observed,DateTime now){return (now-observed).TotalSeconds<=30;}
  internal long Revision{get{lock(gate)return revision;}}
  public static bool Changed(Position before,Position after,double? beforeHeading,double? afterHeading){return before==null||Math.Abs(before.X-after.X)>.5||Math.Abs(before.Y-after.Y)>.5||Math.Abs(before.Z-after.Z)>.5||beforeHeading.HasValue!=afterHeading.HasValue||(beforeHeading.HasValue&&Math.Abs(MapMath.DeltaAngle(beforeHeading.Value,afterHeading.Value))>.25);}
  public void Observe(Position p,double north){double? heading=p.HasYaw?(double?)MapMath.NormalizeAngle(p.Yaw-north):null;lock(gate){if(!Changed(previousSample,p,previousHeading,heading)&&(UtcNow()-observed).TotalSeconds<2)return;previousSample=p;previousHeading=heading;}double age=0;Dictionary<string,object> d=new Dictionary<string,object>();d["x"]=p.X;d["y"]=p.Y;d["z"]=p.Z;d["heading"]=p.HasYaw?(object)MapMath.NormalizeAngle(p.Yaw-north):null;d["age"]=age;lock(gate){lastPacket=new JavaScriptSerializer().Serialize(d);observed=UtcNow();revision++;}}
  DateTime observed;
  internal int HeartbeatMs=5000,SilenceMs=15000,SendTimeoutMs=5000;
  long lastReceivedTick;bool connectionConfirmed;
  static long MonotonicMs(){return Stopwatch.GetTimestamp()*1000/Stopwatch.Frequency;}
  async Task Send(ClientWebSocket ws,string packet){using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token)){timeout.CancelAfter(SendTimeoutMs);byte[] bytes=Encoding.UTF8.GetBytes(packet);await ws.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,timeout.Token);}}
  public void Start(){Task.Run((Func<Task>)Run);}
  void Report(string value){if(Status!=null)Status(value);}
  async Task Run(){while(!cancel.IsCancellationRequested){
   ClientWebSocket ws=null;Task receive=null;
   try{await hub.EnsureSession(cancel.Token);using(ws=new ClientWebSocket()){
    socket=ws;ws.Options.Cookies=cookies;ws.Options.SetRequestHeader("Origin","https://scum-hub.ru");
    using(var connect=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token)){connect.CancelAfter(10000);await ws.ConnectAsync(new Uri("wss://scum-hub.ru/api/minimap/live"),connect.Token);}
    Report("Союзники: проверяю связь…");connectionConfirmed=false;Interlocked.Exchange(ref lastReceivedTick,MonotonicMs());
    receive=Receive(ws);bool wasSharing=true;long renew=MonotonicMs()+240000,lastSent=MonotonicMs();
    while(ws.State==WebSocketState.Open&&!cancel.IsCancellationRequested&&MonotonicMs()<renew){
     if(receive.IsCompleted){await receive;break;}
     if(MonotonicMs()-Interlocked.Read(ref lastReceivedTick)>=SilenceMs)throw new TimeoutException("Ally server stopped responding");
     string packet=null;long sendingRevision=0;
     if(!Sharing){if(wasSharing)packet="{\"off\":true}";}else{lock(gate){if(SendingAllowed&&lastPacket!=null&&revision!=sentRevision&&(DateTime.UtcNow-observed).TotalMilliseconds<300){sendingRevision=revision;var d=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(lastPacket);d["age"]=Math.Min(86400000,Math.Max(0,(DateTime.UtcNow-observed).TotalMilliseconds));packet=new JavaScriptSerializer().Serialize(d);}}}
     wasSharing=Sharing;
     if(packet==null&&MonotonicMs()-lastSent>=HeartbeatMs)packet="{\"ping\":true}";
     if(packet!=null){await Send(ws,packet);lastSent=MonotonicMs();if(sendingRevision>0){lock(gate){sentRevision=sendingRevision;}}}
     await Task.Delay(150,cancel.Token);
    }
    ws.Abort();
   }}catch(HubExpiredException){if(!cancel.IsCancellationRequested)Report("Сессия HUB завершена. Войди снова; метки сохранены.");break;}
   catch(Exception){if(!cancel.IsCancellationRequested)Report("Союзники: связь потеряна · переподключаюсь…");}
   finally{if(ws!=null){ws.Abort();ws.Dispose();}if(receive!=null){try{await receive;}catch{}}socket=null;}
   try{await Task.Delay(2000,cancel.Token);}catch{break;}
  }}
  async Task Receive(ClientWebSocket ws){byte[] bytes=new byte[65536];while(ws.State==WebSocketState.Open&&!cancel.IsCancellationRequested){int length=0;WebSocketReceiveResult part;do{part=await ws.ReceiveAsync(new ArraySegment<byte>(bytes,length,bytes.Length-length),cancel.Token);if(part.MessageType==WebSocketMessageType.Close)return;length+=part.Count;if(length==bytes.Length&&!part.EndOfMessage)throw new Exception("Packet too large");}while(!part.EndOfMessage);
   List<AllyPosition> peers=ParsePeers(Encoding.UTF8.GetString(bytes,0,length),DateTime.UtcNow);Interlocked.Exchange(ref lastReceivedTick,MonotonicMs());if(!connectionConfirmed){connectionConfirmed=true;Report("Союзники: связь подтверждена · 150 мс");}if(Updated!=null)Updated(peers);
  }}
  public static List<AllyPosition> ParsePeers(string payload,DateTime clientNow){
   var root=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(payload);string self=Convert.ToString(root["self"]);double now=Convert.ToDouble(root["now"]);List<AllyPosition> peers=new List<AllyPosition>();foreach(object raw in (object[])root["peers"]){var d=raw as Dictionary<string,object>;if(d==null||Convert.ToString(d["id"])==self)continue;double x=Convert.ToDouble(d["x"]),y=Convert.ToDouble(d["y"]);if(Double.IsNaN(x)||Double.IsNaN(y)||Math.Abs(x)>2000000||Math.Abs(y)>2000000)continue;string nick=Convert.ToString(d["nick"]);peers.Add(new AllyPosition{Id=Convert.ToString(d["id"]),Nick=nick.Length>32?nick.Substring(0,32):nick,X=x,Y=y,HasHeading=d["heading"]!=null,Heading=d["heading"]==null?0:Convert.ToDouble(d["heading"]),Observed=clientNow.AddMilliseconds(-Math.Max(0,Math.Min(86400000,now-Convert.ToDouble(d["at"]))))});}return peers;
  }
  public void Dispose(){cancel.Cancel();if(socket!=null)socket.Abort();}
 }
}

