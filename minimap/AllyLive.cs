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
 public sealed class AllyPosition {public string Id,Nick;public double X,Y,Heading;public bool HasHeading;public DateTime Observed;public long SourceAt;}
 internal sealed class AllyMotion {
  sealed class Track {public AllyPosition Target;public double X,Y,Heading;public DateTime Started,Received;}
  readonly Dictionary<string,Track> tracks=new Dictionary<string,Track>();
  const double BlendMs=100;
  static AllyPosition At(Track t,DateTime now){double f=Math.Max(0,Math.Min(1,(now-t.Started).TotalMilliseconds/BlendMs));return new AllyPosition{Id=t.Target.Id,Nick=t.Target.Nick,X=t.X+(t.Target.X-t.X)*f,Y=t.Y+(t.Target.Y-t.Y)*f,Heading=MapMath.NormalizeAngle(t.Heading+MapMath.DeltaAngle(t.Heading,t.Target.Heading)*f),HasHeading=t.Target.HasHeading,Observed=t.Target.Observed,SourceAt=t.Target.SourceAt};}
  public AllyPosition Render(AllyPosition sample,DateTime now){Track t;bool found=tracks.TryGetValue(sample.Id,out t);
   if(!found){t=new Track{Target=sample,X=sample.X,Y=sample.Y,Heading=sample.Heading,Started=now,Received=now};tracks[sample.Id]=t;return At(t,now);}
   bool moved=t.Target.X!=sample.X||t.Target.Y!=sample.Y||t.Target.HasHeading!=sample.HasHeading||MapMath.DeltaAngle(t.Target.Heading,sample.Heading)!=0;
   if(moved){AllyPosition rendered=At(t,now);bool snap=(now-t.Received).TotalMilliseconds>1000||Math.Sqrt(Math.Pow(sample.X-t.Target.X,2)+Math.Pow(sample.Y-t.Target.Y,2))>10000;t.X=snap?sample.X:rendered.X;t.Y=snap?sample.Y:rendered.Y;t.Heading=snap?sample.Heading:rendered.Heading;t.Started=now;t.Received=now;t.Target=sample;}
   else {t.Target=sample;}
   return At(t,now);
  }
  public void Clear(){tracks.Clear();}
 }
 internal sealed class AllyInbox {
  readonly object gate=new object();object owner;List<AllyPosition> latest;
  public void Publish(object source,List<AllyPosition> peers){lock(gate){owner=source;latest=peers;}}
  public List<AllyPosition> Take(object source){lock(gate){var result=owner==source?latest:null;owner=null;latest=null;return result;}}
 }
 // Track application replies separately from successful writes to the local socket.
 internal sealed class AllyLinkHealth {
  readonly object gate=new object();long sent,ack,pendingSince,lastReply;bool acknowledgedProtocol;
  internal const int TimeoutMs=2500;
  public void Reset(long now){lock(gate){sent=ack=pendingSince=0;lastReply=now;acknowledgedProtocol=false;}}
  public void Sent(long seq,long now){lock(gate){sent=Math.Max(sent,seq);if(sent>ack&&pendingSince==0)pendingSince=now;}}
  public void Reply(long now,long? accepted){lock(gate){lastReply=now;if(accepted.HasValue){acknowledgedProtocol=true;if(accepted.Value>ack){ack=accepted.Value;pendingSince=sent>ack?now:0;}}}}
  public bool TimedOut(long now){lock(gate)return now-lastReply>=TimeoutMs||(acknowledgedProtocol&&pendingSince>0&&now-pendingSince>=TimeoutMs);}
  public long Acknowledged{get{lock(gate)return ack;}}
  public long PendingAge(long now){lock(gate)return pendingSince==0?0:Math.Max(0,now-pendingSince);}
 }
 public sealed class AllyLive:IDisposable {
  readonly HubClient hub;readonly CookieContainer cookies;readonly CancellationTokenSource cancel=new CancellationTokenSource();ClientWebSocket socket;
  public volatile bool Sharing=true;public volatile bool SendingAllowed;public Action<List<AllyPosition>> Updated;public Action<string> Status;
  readonly object gate=new object();string lastPacket;long revision,sentRevision;Position previousSample;double? previousHeading;
  public AllyLive(HubClient hub){this.hub=hub;this.cookies=hub.Cookies;}
  readonly object reportGate=new object();readonly Queue<string> reports=new Queue<string>();
  readonly Dictionary<string,Dictionary<string,object>> peerFlow=new Dictionary<string,Dictionary<string,object>>();
  long diagnosticAsked,lastFrameTick,maxFrameGap,positionSent;
  readonly AllyLinkHealth health=new AllyLinkHealth();
  internal Func<Uri> SocketUri=()=>new Uri("wss://scum-hub.ru/api/minimap/live");
  void AddReport(string kind,object server){
   long now=MonotonicMs();var data=new Dictionary<string,object>{{"build","flow-2"},{"time",DateTime.UtcNow.ToString("o")},{"kind",kind},{"samples",Interlocked.Read(ref sampleCount)},{"positionsSent",Interlocked.Read(ref positionSent)},{"acknowledgedRevision",health.Acknowledged},{"pendingAckMs",health.PendingAge(now)},{"frames",Interlocked.Read(ref receivedCount)},{"reconnects",Interlocked.Read(ref reconnectCount)},{"sampleAgeMs",Interlocked.Read(ref lastSampleTick)==0?-1:now-Interlocked.Read(ref lastSampleTick)},{"frameAgeMs",Interlocked.Read(ref lastReceivedTick)==0?-1:now-Interlocked.Read(ref lastReceivedTick)},{"maxFrameGapMs",Interlocked.Read(ref maxFrameGap)},{"sharing",Sharing},{"sendingAllowed",SendingAllowed},{"failure",lastFailure}};
   if(server!=null)data["server"]=server;
   lock(reportGate){data["peers"]=new List<Dictionary<string,object>>(peerFlow.Values);reports.Enqueue(new JavaScriptSerializer().Serialize(data));while(reports.Count>240)reports.Dequeue();}
  }
  public string DiagnosticReport(){AddReport("export",null);lock(reportGate)return "SCUM HUB network report flow-2\r\nNo coordinates, account codes or session cookies are included.\r\n"+String.Join("\r\n",reports.ToArray());}
  internal bool HandleFlowFrame(Dictionary<string,object> root){
   long now=MonotonicMs();object raw;
   if(root.TryGetValue("diagnostic",out raw)&&raw is bool&&(bool)raw){
    var safe=new List<Dictionary<string,object>>();object connections;
    if(root.TryGetValue("connections",out connections)&&connections is object[])foreach(object row in (object[])connections){var original=row as Dictionary<string,object>;if(original==null)continue;var clean=new Dictionary<string,object>();foreach(string key in new string[]{"nick","active","received","rejected","lastPacketAge","positionAge","reason"})if(original.TryGetValue(key,out raw))clean[key]=raw;
     object client;if(original.TryGetValue("client",out client)&&client is Dictionary<string,object>){var counters=new Dictionary<string,object>();foreach(string key in new string[]{"samples","sent","received","reconnects","sampleAge","replyAge","sending","failure"})if(((Dictionary<string,object>)client).TryGetValue(key,out raw))counters[key]=raw;clean["client"]=counters;}safe.Add(clean);}
    AddReport("server",new Dictionary<string,object>{{"roundTripMs",diagnosticAsked==0?-1:now-diagnosticAsked},{"connections",safe}});return true;
   }
   object rows;if(root.TryGetValue("peers",out rows)&&rows is object[])lock(reportGate)foreach(object row in (object[])rows){var p=row as Dictionary<string,object>;if(p==null||!p.ContainsKey("id")||!p.ContainsKey("at"))continue;string id=Convert.ToString(p["id"]);if(root.ContainsKey("self")&&id==Convert.ToString(root["self"]))continue;long at=Convert.ToInt64(p["at"]);Dictionary<string,object> old;if(peerFlow.TryGetValue(id,out old)&&Convert.ToInt64(old["sourceAt"])==at)continue;
    peerFlow[id]=new Dictionary<string,object>{{"nick",p.ContainsKey("nick")?p["nick"]:""},{"updates",old==null?1:Convert.ToInt64(old["updates"])+1},{"sourceStepMs",old==null?0:at-Convert.ToInt64(old["sourceAt"])},{"arrivalStepMs",old==null?0:now-Convert.ToInt64(old["arrivalAt"])},{"sourceAt",at},{"arrivalAt",now}};
    if(peerFlow.Count>50)peerFlow.Clear();
   }
   return false;
  }
  internal Func<DateTime> UtcNow=()=>DateTime.UtcNow;
  public static bool Visible(DateTime observed,DateTime now){return (now-observed).TotalSeconds<=30;}
  public static bool Stale(DateTime observed,DateTime now){return (now-observed).TotalSeconds>7;}
  internal long Revision{get{lock(gate)return revision;}}
  public static bool Changed(Position before,Position after,double? beforeHeading,double? afterHeading){return before==null||Math.Abs(before.X-after.X)>.5||Math.Abs(before.Y-after.Y)>.5||Math.Abs(before.Z-after.Z)>.5||beforeHeading.HasValue!=afterHeading.HasValue||(beforeHeading.HasValue&&Math.Abs(MapMath.DeltaAngle(beforeHeading.Value,afterHeading.Value))>.25);}
  public void Observe(Position p,double north){Interlocked.Increment(ref sampleCount);Interlocked.Exchange(ref lastSampleTick,MonotonicMs());double? heading=p.HasYaw?(double?)MapMath.NormalizeAngle(p.Yaw-north):null;lock(gate){if(!Changed(previousSample,p,previousHeading,heading)&&(UtcNow()-observed).TotalSeconds<5)return;previousSample=p;previousHeading=heading;}double age=0;Dictionary<string,object> d=new Dictionary<string,object>();d["x"]=p.X;d["y"]=p.Y;d["z"]=p.Z;d["heading"]=p.HasYaw?(object)MapMath.NormalizeAngle(p.Yaw-north):null;d["age"]=age;lock(gate){lastPacket=new JavaScriptSerializer().Serialize(d);observed=UtcNow();revision++;}}
  DateTime observed;
  internal int HeartbeatMs=1000,SendTimeoutMs=2500;
  long lastReceivedTick,lastSampleTick,sampleCount,sentCount,receivedCount,reconnectCount,nextDiagnostic;string lastFailure="";bool connectionConfirmed;
  static long MonotonicMs(){return Stopwatch.GetTimestamp()*1000/Stopwatch.Frequency;}
  async Task Send(ClientWebSocket ws,string packet){using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token)){timeout.CancelAfter(SendTimeoutMs);long now=MonotonicMs();if(now>=nextDiagnostic){nextDiagnostic=now+5000;var body=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(packet);long sample=Interlocked.Read(ref lastSampleTick);body["client"]=new Dictionary<string,object>{{"samples",Interlocked.Read(ref sampleCount)},{"sent",Interlocked.Read(ref sentCount)},{"received",Interlocked.Read(ref receivedCount)},{"reconnects",Interlocked.Read(ref reconnectCount)},{"sampleAge",sample==0?-1:Math.Max(0,now-sample)},{"replyAge",Math.Max(0,now-Interlocked.Read(ref lastReceivedTick))},{"sending",SendingAllowed},{"failure",lastFailure}};packet=new JavaScriptSerializer().Serialize(body);}
   byte[] bytes=Encoding.UTF8.GetBytes(packet);await ws.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,timeout.Token);Interlocked.Increment(ref sentCount);}}
  public void Start(){Task.Run((Func<Task>)Run);}
  void Report(string value){if(Status!=null)Status(value);}
  async Task Run(){while(!cancel.IsCancellationRequested){
   ClientWebSocket ws=null;Task receive=null;
   try{await hub.EnsureSession(cancel.Token);using(ws=new ClientWebSocket()){
    Interlocked.Increment(ref reconnectCount);nextDiagnostic=0;socket=ws;if(cookies!=null)ws.Options.Cookies=cookies;ws.Options.SetRequestHeader("Origin","https://scum-hub.ru");
    using(var connect=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token)){connect.CancelAfter(10000);await ws.ConnectAsync(SocketUri(),connect.Token);}
    Report("Союзники: проверяю связь…");connectionConfirmed=false;health.Reset(MonotonicMs());lock(gate){sentRevision=0;}Interlocked.Exchange(ref lastReceivedTick,MonotonicMs());
    receive=Receive(ws);bool wasSharing=true;long renew=MonotonicMs()+240000,lastHeartbeat=MonotonicMs(),nextFlow=MonotonicMs()+1000;
    while(ws.State==WebSocketState.Open&&!cancel.IsCancellationRequested&&MonotonicMs()<renew){
     if(receive.IsCompleted){await receive;break;}
     if(health.TimedOut(MonotonicMs()))throw new TimeoutException("Ally delivery acknowledgement timed out");
     string packet=null;long sendingRevision=0;
     if(!Sharing){if(wasSharing)packet="{\"off\":true}";}else{lock(gate){if(SendingAllowed&&lastPacket!=null&&revision!=sentRevision&&(DateTime.UtcNow-observed).TotalMilliseconds<300){sendingRevision=revision;var d=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(lastPacket);d["seq"]=sendingRevision;d["age"]=Math.Min(86400000,Math.Max(0,(DateTime.UtcNow-observed).TotalMilliseconds));packet=new JavaScriptSerializer().Serialize(d);}}}
     wasSharing=Sharing;
     if(packet!=null){await Send(ws,packet);if(sendingRevision>0){health.Sent(sendingRevision,MonotonicMs());Interlocked.Increment(ref positionSent);lock(gate){sentRevision=sendingRevision;}}}
     if(MonotonicMs()-lastHeartbeat>=HeartbeatMs){await Send(ws,"{\"ping\":true}");lastHeartbeat=MonotonicMs();}
     if(MonotonicMs()>=nextFlow){nextFlow=MonotonicMs()+5000;diagnosticAsked=MonotonicMs();AddReport("request",null);await Send(ws,"{\"diagnostic\":true}");}
     await Task.Delay(100,cancel.Token);
    }
    ws.Abort();
   }}catch(HubExpiredException){if(!cancel.IsCancellationRequested)Report("Сессия HUB завершена. Войди снова; метки сохранены.");break;}
   catch(Exception ex){lastFailure=ex.GetType().Name+": "+ex.Message;if(lastFailure.Length>80)lastFailure=lastFailure.Substring(0,80);AddReport("failure",null);if(!cancel.IsCancellationRequested)Report("Союзники: связь потеряна · переподключаюсь…");}
   finally{if(ws!=null){ws.Abort();ws.Dispose();}if(receive!=null){try{await receive;}catch{}}socket=null;}
   try{await Task.Delay(250,cancel.Token);}catch{break;}
  }}
  async Task Receive(ClientWebSocket ws){byte[] bytes=new byte[65536];while(ws.State==WebSocketState.Open&&!cancel.IsCancellationRequested){int length=0;WebSocketReceiveResult part;do{part=await ws.ReceiveAsync(new ArraySegment<byte>(bytes,length,bytes.Length-length),cancel.Token);if(part.MessageType==WebSocketMessageType.Close)throw new WebSocketException("Remote close "+part.CloseStatus+": "+part.CloseStatusDescription);length+=part.Count;if(length==bytes.Length&&!part.EndOfMessage)throw new Exception("Packet too large");}while(!part.EndOfMessage);
   string payload=Encoding.UTF8.GetString(bytes,0,length);long tick=MonotonicMs();Interlocked.Exchange(ref lastReceivedTick,tick);var root=new JavaScriptSerializer().DeserializeObject(payload) as Dictionary<string,object>;object accepted;health.Reply(tick,root!=null&&root.TryGetValue("ack",out accepted)?(long?)Convert.ToInt64(accepted):null);if(HandleFlowFrame(root))continue;
   long previous=Interlocked.Exchange(ref lastFrameTick,tick);if(previous>0){long gap=tick-previous;if(gap>Interlocked.Read(ref maxFrameGap))Interlocked.Exchange(ref maxFrameGap,gap);}
   List<AllyPosition> peers=ParsePeers(payload,DateTime.UtcNow);Interlocked.Increment(ref receivedCount);if(!connectionConfirmed){connectionConfirmed=true;Report("Союзники: связь подтверждена · отчёт в настройках");}if(Updated!=null)Updated(peers);
  }}
  public static List<AllyPosition> ParsePeers(string payload,DateTime clientNow){
   var root=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(payload);string self=Convert.ToString(root["self"]);double now=Convert.ToDouble(root["now"]);List<AllyPosition> peers=new List<AllyPosition>();foreach(object raw in (object[])root["peers"]){var d=raw as Dictionary<string,object>;if(d==null||Convert.ToString(d["id"])==self)continue;double x=Convert.ToDouble(d["x"]),y=Convert.ToDouble(d["y"]);if(Double.IsNaN(x)||Double.IsNaN(y)||Math.Abs(x)>2000000||Math.Abs(y)>2000000)continue;string nick=Convert.ToString(d["nick"]);peers.Add(new AllyPosition{Id=Convert.ToString(d["id"]),Nick=nick.Length>32?nick.Substring(0,32):nick,X=x,Y=y,HasHeading=d["heading"]!=null,Heading=d["heading"]==null?0:Convert.ToDouble(d["heading"]),SourceAt=Convert.ToInt64(d["at"]),Observed=clientNow.AddMilliseconds(-Math.Max(0,Math.Min(86400000,now-Convert.ToDouble(d["at"]))))});}return peers;
  }
  internal static async Task NetworkTest(){
   string endpoint=Environment.GetEnvironmentVariable("SCUM_NETWORK_PROBE");if(String.IsNullOrEmpty(endpoint))throw new Exception("Test endpoint missing");
   var clients=new List<AllyLive>();var hubs=new List<HubClient>();
   try{
    foreach(string id in new string[]{"a","b"}){var h=new HubClient();hubs.Add(h);var live=new AllyLive(h){SendingAllowed=true};int connects=0;string own=id;live.SocketUri=()=>new Uri(endpoint+"&id="+own+"&drop="+(++connects==1?"1":"0"));clients.Add(live);live.Start();}
    for(int i=0;i<220;i++){foreach(var live in clients)live.Observe(new Position{X=i*100,Y=100,Z=0},0);await Task.Delay(100);}
    var lines=new List<string>();foreach(var live in clients){long frames=Interlocked.Read(ref live.receivedCount),reconnects=Interlocked.Read(ref live.reconnectCount),ack=live.health.Acknowledged;string report=live.DiagnosticReport();
     if(frames<90||ack<190||reconnects!=2||!report.Contains("\"kind\":\"server\"")||Interlocked.Read(ref live.maxFrameGap)>5500)throw new Exception("Network recovery failed: frames="+frames+" ack="+ack+" reconnects="+reconnects+" gap="+live.maxFrameGap);
     lines.Add("PASS frames="+frames+" acknowledgedRevision="+ack+" connections="+reconnects+" maxGapMs="+live.maxFrameGap);
    }System.IO.File.WriteAllLines("network-test.txt",lines);
   }finally{foreach(var live in clients)live.Dispose();foreach(var h in hubs)h.Dispose();}
  }
  public void Dispose(){cancel.Cancel();if(socket!=null)socket.Abort();}
 }
}
