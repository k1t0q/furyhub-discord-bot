using System;
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
  readonly CookieContainer cookies;readonly CancellationTokenSource cancel=new CancellationTokenSource();ClientWebSocket socket;
  public volatile bool Sharing=true;public volatile bool SendingAllowed;public Action<List<AllyPosition>> Updated;public Action<string> Status;
  readonly object gate=new object();string lastPacket;long revision,sentRevision;Position previousSample;double? previousHeading;
  public AllyLive(CookieContainer cookies){this.cookies=cookies;}
  public static bool Changed(Position before,Position after,double? beforeHeading,double? afterHeading){return before==null||Math.Abs(before.X-after.X)>.5||Math.Abs(before.Y-after.Y)>.5||Math.Abs(before.Z-after.Z)>.5||beforeHeading.HasValue!=afterHeading.HasValue||(beforeHeading.HasValue&&Math.Abs(MapMath.DeltaAngle(beforeHeading.Value,afterHeading.Value))>.25);}
  public void Observe(Position p,double north){double? heading=p.HasYaw?(double?)MapMath.NormalizeAngle(p.Yaw-north):null;lock(gate){if(!Changed(previousSample,p,previousHeading,heading))return;previousSample=p;previousHeading=heading;}double age=0;Dictionary<string,object> d=new Dictionary<string,object>();d["x"]=p.X;d["y"]=p.Y;d["z"]=p.Z;d["heading"]=p.HasYaw?(object)MapMath.NormalizeAngle(p.Yaw-north):null;d["age"]=age;lock(gate){lastPacket=new JavaScriptSerializer().Serialize(d);observed=DateTime.UtcNow;revision++;}}
  DateTime observed;
  public void Start(){Task.Run((Func<Task>)Run);}
  void Report(string value){if(Status!=null)Status(value);}
  async Task Run(){while(!cancel.IsCancellationRequested){try{using(ClientWebSocket ws=new ClientWebSocket()){socket=ws;ws.Options.Cookies=cookies;ws.Options.SetRequestHeader("Origin","https://scum-hub.ru");using(CancellationTokenSource connect=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token)){connect.CancelAfter(10000);await ws.ConnectAsync(new Uri("wss://scum-hub.ru/api/minimap/live"),connect.Token);}Report("Союзники: подключено · 100 мс");
    Task receive=Receive(ws);bool wasSharing=true;DateTime renew=DateTime.UtcNow.AddMinutes(4);
    while(ws.State==WebSocketState.Open&&!cancel.IsCancellationRequested&&DateTime.UtcNow<renew){string packet=null;long sendingRevision=0;if(!Sharing){if(wasSharing)packet="{\"off\":true}";}else{lock(gate){if(SendingAllowed&&lastPacket!=null&&revision!=sentRevision&&(DateTime.UtcNow-observed).TotalMilliseconds<300){sendingRevision=revision;var d=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(lastPacket);d["age"]=Math.Min(86400000,Math.Max(0,(DateTime.UtcNow-observed).TotalMilliseconds));packet=new JavaScriptSerializer().Serialize(d);}}}wasSharing=Sharing;
     if(packet!=null){byte[] bytes=Encoding.UTF8.GetBytes(packet);await ws.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,cancel.Token);if(sendingRevision>0){lock(gate){sentRevision=sendingRevision;}}}await Task.Delay(100,cancel.Token);
    }ws.Abort();try{await receive;}catch{}
   }}catch(Exception){if(!cancel.IsCancellationRequested)Report("Союзники: нет связи · последние позиции");}finally{socket=null;}
   try{await Task.Delay(2000,cancel.Token);}catch{break;}
  }}
  async Task Receive(ClientWebSocket ws){byte[] bytes=new byte[65536];while(ws.State==WebSocketState.Open&&!cancel.IsCancellationRequested){int length=0;WebSocketReceiveResult part;do{part=await ws.ReceiveAsync(new ArraySegment<byte>(bytes,length,bytes.Length-length),cancel.Token);if(part.MessageType==WebSocketMessageType.Close)return;length+=part.Count;if(length==bytes.Length&&!part.EndOfMessage)throw new Exception("Packet too large");}while(!part.EndOfMessage);
   List<AllyPosition> peers=ParsePeers(Encoding.UTF8.GetString(bytes,0,length),DateTime.UtcNow);if(Updated!=null)Updated(peers);
  }}
  public static List<AllyPosition> ParsePeers(string payload,DateTime clientNow){
   var root=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(payload);string self=Convert.ToString(root["self"]);double now=Convert.ToDouble(root["now"]);List<AllyPosition> peers=new List<AllyPosition>();foreach(object raw in (object[])root["peers"]){var d=raw as Dictionary<string,object>;if(d==null||Convert.ToString(d["id"])==self)continue;double x=Convert.ToDouble(d["x"]),y=Convert.ToDouble(d["y"]);if(Double.IsNaN(x)||Double.IsNaN(y)||Math.Abs(x)>2000000||Math.Abs(y)>2000000)continue;string nick=Convert.ToString(d["nick"]);peers.Add(new AllyPosition{Id=Convert.ToString(d["id"]),Nick=nick.Length>32?nick.Substring(0,32):nick,X=x,Y=y,HasHeading=d["heading"]!=null,Heading=d["heading"]==null?0:Convert.ToDouble(d["heading"]),Observed=clientNow.AddMilliseconds(-Math.Max(0,Math.Min(86400000,now-Convert.ToDouble(d["at"]))))});}return peers;
  }
  public void Dispose(){cancel.Cancel();if(socket!=null)socket.Abort();}
 }
}
