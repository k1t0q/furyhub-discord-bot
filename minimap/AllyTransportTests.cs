using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace ScumPositionProbe {
 internal static class AllyTransportTests {
  sealed class FragmentSocket:WebSocket {
   readonly Queue<byte[]> messages=new Queue<byte[]>();byte[] current;int offset;
   public FragmentSocket(params string[] bodies){foreach(string body in bodies)messages.Enqueue(Encoding.UTF8.GetBytes(body));}
   public override WebSocketCloseStatus? CloseStatus{get{return null;}}
   public override string CloseStatusDescription{get{return null;}}
   public override WebSocketState State{get{return WebSocketState.Open;}}
   public override string SubProtocol{get{return null;}}
   public override void Abort(){}public override void Dispose(){}
   public override Task CloseAsync(WebSocketCloseStatus status,string reason,CancellationToken token){return Task.FromResult(0);}
   public override Task CloseOutputAsync(WebSocketCloseStatus status,string reason,CancellationToken token){return Task.FromResult(0);}
   public override Task SendAsync(ArraySegment<byte> buffer,WebSocketMessageType type,bool end,CancellationToken token){return Task.FromResult(0);}
   public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer,CancellationToken token){
    if(current==null){current=messages.Dequeue();offset=0;}int count=Math.Min(Math.Min(4093,buffer.Count),current.Length-offset);
    Array.Copy(current,offset,buffer.Array,buffer.Offset,count);offset+=count;bool end=offset==current.Length;if(end)current=null;
    return Task.FromResult(new WebSocketReceiveResult(count,WebSocketMessageType.Text,end));
   }
  }
  public static void Run(List<string> results){
   string large="{\"diagnostic\":true,\"padding\":\""+new string('я',40000)+"\"}";
   using(var ws=new FragmentSocket(large,"{\"ack\":2}")){
    int skipped=0;string first=AllyLive.ReadMessage(ws,CancellationToken.None,n=>skipped++).GetAwaiter().GetResult();
    if(first!=large||skipped!=0)throw new Exception("Fragmented UTF-8 message above 64KB failed");
    if(AllyLive.ReadMessage(ws,CancellationToken.None,null).GetAwaiter().GetResult()!="{\"ack\":2}")throw new Exception("Message boundary lost");
   }
   using(var ws=new FragmentSocket(new string('x',AllyLive.MaxMessageBytes+10000),"{\"ack\":3}")){
    int bytes=0;if(AllyLive.ReadMessage(ws,CancellationToken.None,n=>bytes=n).GetAwaiter().GetResult()!=null||bytes<=AllyLive.MaxMessageBytes)throw new Exception("Oversized frame was not drained");
    if(AllyLive.ReadMessage(ws,CancellationToken.None,null).GetAwaiter().GetResult()!="{\"ack\":3}")throw new Exception("Oversized frame broke next message");
   }
   results.Add("PASS fragmented UTF-8 messages above 64KB, bounded memory and oversized-frame recovery without disconnect");
  }
 }
}
