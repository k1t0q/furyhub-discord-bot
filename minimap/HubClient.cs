using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Web.Script.Serialization;

namespace ScumPositionProbe {
 public sealed class HubMarker {public string Id,Name,Kind;public double X,Y;public Color Color;}
 public sealed class HubSnapshot {public List<HubMarker> Markers=new List<HubMarker>();public string Nick;}
 public sealed class HubExpiredException:Exception{public HubExpiredException():base("Сессия HUB истекла. Войди снова.") {}}
 public sealed class HubClient:IDisposable {
  internal CookieContainer Cookies;
  readonly SemaphoreSlim renewalGate=new SemaphoreSlim(1,1);
  internal Func<DateTime> UtcNow=()=>DateTime.UtcNow;
  DateTime renewAt=DateTime.MaxValue,retryRenewAt=DateTime.MinValue;
  readonly HttpClient client;readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=4194304};
  static HttpClientHandler Handler(){return new HttpClientHandler{CookieContainer=new CookieContainer(),UseCookies=true,AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate};}
  public HubClient():this(Handler()){}
  internal HubClient(HttpMessageHandler handler){HttpClientHandler native=handler as HttpClientHandler;if(native!=null)Cookies=native.CookieContainer;ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;client=new HttpClient(handler);client.BaseAddress=new Uri("https://scum-hub.ru/");client.Timeout=TimeSpan.FromSeconds(8);client.DefaultRequestHeaders.Add("Origin","https://scum-hub.ru");client.DefaultRequestHeaders.Add("Accept","application/json");}
  public async Task Login(string nick,string role,string code,bool admin){
   Dictionary<string,object> body=new Dictionary<string,object>();body["code"]=code;if(!admin){body["nick"]=nick;if(!String.IsNullOrEmpty(role))body["role"]=role;}
   using(HttpResponseMessage r=await client.PostAsync(admin?"api/admin/login":"api/member/login",new StringContent(json.Serialize(body),Encoding.UTF8,"application/json"))){
    if((int)r.StatusCode==429)throw new Exception("Слишком много попыток. Подожди перед повторным входом.");
    if(!r.IsSuccessStatusCode)throw new Exception("Вход не выполнен (HTTP "+(int)r.StatusCode+"). Проверь код, роль и режим входа.");
    renewAt=UtcNow().AddMinutes(30);retryRenewAt=DateTime.MinValue;
   }
  }
  public async Task EnsureSession(CancellationToken cancel){
   if(UtcNow()<renewAt)return;await renewalGate.WaitAsync(cancel);
   try{if(UtcNow()<renewAt)return;if(UtcNow()<retryRenewAt)throw new Exception("Повторное продление сессии ожидает связи.");
    retryRenewAt=UtcNow().AddSeconds(15);
    using(HttpResponseMessage r=await client.PostAsync("api/member/refresh",new StringContent("{}",Encoding.UTF8,"application/json"),cancel)){
     if(r.StatusCode==HttpStatusCode.Unauthorized||r.StatusCode==HttpStatusCode.Forbidden)throw new HubExpiredException();
     if(!r.IsSuccessStatusCode)throw new Exception("Не удалось продлить сессию HUB (HTTP "+(int)r.StatusCode+").");
     renewAt=UtcNow().AddMinutes(30);retryRenewAt=DateTime.MinValue;
    }
   }finally{renewalGate.Release();}
  }
  static string Text(Dictionary<string,object>d,string k){object v;return d.TryGetValue(k,out v)&&v!=null?Convert.ToString(v,CultureInfo.InvariantCulture):"";}
  static string LinkedKind(string id){foreach(string k in new string[]{"stash","base","other"})if(id.StartsWith(k+"-post-",StringComparison.Ordinal))return k;return "";}
  static string PostTitle(Dictionary<string,object> post,string kind){
   string first="";foreach(string line in Text(post,"comment").Split(new char[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)){if(line.Trim().Length>0){first=line.Trim();break;}}
   if(first.Length==0){string author=Text(post,"author_nick");first="Схрон "+(author.Length==0?"отряда":author);}
   if(first.Length>70)first=first.Substring(0,70);
   if(kind=="base"&&first.StartsWith("Схрон "))first="База "+first.Substring(6);
   if(kind=="other"&&first.StartsWith("Схрон "))first="Метка "+first.Substring(6);
   return first;
  }
  static bool Coordinates(Dictionary<string,object>d,out double x,out double y){
   x=y=0;return Double.TryParse(Text(d,"x"),NumberStyles.Float,CultureInfo.InvariantCulture,out x)&&Double.TryParse(Text(d,"y"),NumberStyles.Float,CultureInfo.InvariantCulture,out y)&&!Double.IsNaN(x)&&!Double.IsNaN(y)&&!Double.IsInfinity(x)&&!Double.IsInfinity(y)&&x>=0&&x<=1&&y>=0&&y<=1;
  }
  static Color MarkerColor(Dictionary<string,object> row,string kind,Dictionary<string,object> post){
   string hex=Text(row,"color");if(System.Text.RegularExpressions.Regex.IsMatch(hex,@"^#[0-9a-fA-F]{6}$"))return ColorTranslator.FromHtml(hex);
   if(kind=="stash"){
    Dictionary<string,object> time=post??row;string raw=Text(time,post==null?"updated_at":"stash_checked_at");if(raw.Length==0)raw=Text(time,"created_at");DateTime checkedAt;
    double left=14;if(DateTime.TryParse(raw,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out checkedAt))left=Math.Max(0,Math.Ceiling((checkedAt.AddDays(14)-DateTime.UtcNow).TotalDays));
    return left==0?Color.FromArgb(136,136,136):left<=1?Color.FromArgb(224,82,82):left<=5?Color.FromArgb(229,170,48):Color.FromArgb(52,168,83);
   }
   return kind=="base"?Color.FromArgb(224,82,82):kind=="other"?Color.FromArgb(229,170,48):Color.FromArgb(115,194,235);
  }
  public static HubSnapshot Parse(string text){
   JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=4194304};Dictionary<string,object> root=json.DeserializeObject(text) as Dictionary<string,object>;
   if(root==null)throw new Exception("Некорректный ответ HUB.");HubSnapshot result=new HubSnapshot();object member;if(root.TryGetValue("member",out member)&&member is Dictionary<string,object>)result.Nick=Text((Dictionary<string,object>)member,"nick");
   object rows,rawPosts;if(!root.TryGetValue("markers",out rows)||!(rows is object[])||!root.TryGetValue("posts",out rawPosts)||!(rawPosts is object[]))throw new Exception("В ответе HUB нет полного списка меток и публикаций.");
   Dictionary<string,Dictionary<string,object>> posts=new Dictionary<string,Dictionary<string,object>>(StringComparer.Ordinal);
   foreach(object raw in (object[])rawPosts){Dictionary<string,object> d=raw as Dictionary<string,object>;if(d==null)continue;string kind=Text(d,"kind");if(kind=="stash"||kind=="base"||kind=="other")posts[kind+"-post-"+Text(d,"id")]=d;}
   List<Dictionary<string,object>> merged=new List<Dictionary<string,object>>();HashSet<string> existing=new HashSet<string>(StringComparer.Ordinal);
   foreach(object raw in (object[])rows){Dictionary<string,object>d=raw as Dictionary<string,object>;if(d==null)continue;string id=Text(d,"id");if(LinkedKind(id).Length>0&&!posts.ContainsKey(id))continue;if(existing.Add(id))merged.Add(d);}
   foreach(KeyValuePair<string,Dictionary<string,object>> item in posts){if(existing.Contains(item.Key))continue;string comment=Text(item.Value,"comment").Replace(',','.');
    System.Text.RegularExpressions.Match mx=System.Text.RegularExpressions.Regex.Match(comment,@"\bX\s*[:=]\s*(-?\d+(?:\.\d+)?)",System.Text.RegularExpressions.RegexOptions.IgnoreCase),my=System.Text.RegularExpressions.Regex.Match(comment,@"\bY\s*[:=]\s*(-?\d+(?:\.\d+)?)",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    double wx,wy;if(!mx.Success||!my.Success||!Double.TryParse(mx.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out wx)||!Double.TryParse(my.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out wy))continue;
    Dictionary<string,object> d=new Dictionary<string,object>();d["id"]=item.Key;d["x"]=MapMath.NormX(wx);d["y"]=MapMath.NormY(wy);d["type"]=Text(item.Value,"kind");merged.Add(d);
   }
   foreach(Dictionary<string,object>d in merged){double x,y;if(!Coordinates(d,out x,out y))continue;string id=Text(d,"id"),linked=LinkedKind(id),kind=Text(d,"type"),name=Text(d,"name");Dictionary<string,object> post;posts.TryGetValue(id,out post);
    if(linked.Length>0){kind=linked;if(name.Length==0||(linked=="stash"&&System.Text.RegularExpressions.Regex.IsMatch(name,@"^Схрон\s*#\s*\d+$",System.Text.RegularExpressions.RegexOptions.IgnoreCase)))name=PostTitle(post,linked);}
    if(name.Length>80)name=name.Substring(0,80);result.Markers.Add(new HubMarker{Id=id,Name=name,Kind=kind,X=x,Y=y,Color=MarkerColor(d,kind,post)});
   }return result;
  }
  internal static Dictionary<string,object> MarkerPost(string category,string name,Position position){
   if(category!="stash"&&category!="car"&&category!="base"&&category!="other")throw new ArgumentException("Неизвестный тип метки.");
   name=(name??"").Trim();if(name.Length==0||name.Length>70||name.IndexOfAny(new char[]{'\r','\n'})>=0)throw new ArgumentException("Введи название от 1 до 70 символов в одну строку.");
   if(position==null)throw new ArgumentException("Нет позиции игрока.");double x=MapMath.NormX(position.X),y=MapMath.NormY(position.Y);
   if(Double.IsNaN(x)||Double.IsNaN(y)||Double.IsInfinity(x)||Double.IsInfinity(y)||x<0||x>1||y<0||y>1)throw new ArgumentException("Координаты за пределами карты.");
   string title=category=="car"?"🚗 "+name:name;
   return new Dictionary<string,object>{{"table","squad_place_posts"},{"action","insert"},{"values",new Dictionary<string,object>{{"kind",category=="car"?"other":category},{"images",new object[0]},{"map_marker_name",title},{"comment",title+"\nКоординаты: X="+position.X.ToString("R",CultureInfo.InvariantCulture)+" Y="+position.Y.ToString("R",CultureInfo.InvariantCulture)+"\nИсточник: Миникарта SCUM HUB"}}}};
  }
  public async Task CreateMarker(string category,string name,Position position){
   Dictionary<string,object> body=MarkerPost(category,name,position);await EnsureSession(CancellationToken.None);
   using(HttpResponseMessage response=await client.PostAsync("api/legacy",new StringContent(json.Serialize(body),Encoding.UTF8,"application/json"))){
    if(response.StatusCode==HttpStatusCode.Unauthorized||response.StatusCode==HttpStatusCode.Forbidden)throw new HubExpiredException();
    if(!response.IsSuccessStatusCode)throw new Exception("Не удалось сохранить точку (HTTP "+(int)response.StatusCode+"). Проверь метки HUB перед повтором.");
    Dictionary<string,object> result=json.DeserializeObject(await response.Content.ReadAsStringAsync()) as Dictionary<string,object>;object error,data;
    if(result==null||(result.TryGetValue("error",out error)&&error!=null)||!result.TryGetValue("data",out data)||!(data is object[])||((object[])data).Length!=1)throw new Exception("HUB не подтвердил сохранение. Обнови метки перед повтором.");
   }
  }
  public async Task<HubSnapshot> Snapshot(){await EnsureSession(CancellationToken.None);using(HttpResponseMessage r=await client.GetAsync("api/member/snapshot")){
   if(r.StatusCode==HttpStatusCode.Unauthorized||r.StatusCode==HttpStatusCode.Forbidden)throw new HubExpiredException();
   if(!r.IsSuccessStatusCode)throw new Exception("HUB временно недоступен (HTTP "+(int)r.StatusCode+").");
   string text=await r.Content.ReadAsStringAsync();return Parse(text);
  }}
  public static void TestCreateMarker(){
   JavaScriptSerializer serializer=new JavaScriptSerializer();Position position=new Position{X=12345.125,Y=-54321.5};
   var handler=new MarkerHandler();using(HubClient client=new HubClient(handler)){
    foreach(string category in new string[]{"stash","car","base","other"}){
     client.CreateMarker(category,"Проверка",position).GetAwaiter().GetResult();var values=(Dictionary<string,object>)handler.Last["values"];
     if((string)values["kind"]!=(category=="car"?"other":category)||values.ContainsKey("squad_id")||values.ContainsKey("created_by"))throw new Exception("Marker kind or identity error");
     string title=(string)values["map_marker_name"],comment=(string)values["comment"];
     if((category=="car")!=title.StartsWith("🚗 "))throw new Exception("Vehicle label missing");
     var post=new Dictionary<string,object>(values);post["id"]=17;post["created_at"]="2026-11-02T00:00:00Z";
     HubSnapshot snapshot=Parse(serializer.Serialize(new {posts=new object[]{post},markers=new object[0]}));
     if(snapshot.Markers.Count!=1||snapshot.Markers[0].Name!=title||snapshot.Markers[0].Kind!=(string)values["kind"]||Math.Abs(snapshot.Markers[0].X-MapMath.NormX(position.X))>1e-9||Math.Abs(snapshot.Markers[0].Y-MapMath.NormY(position.Y))>1e-9)throw new Exception("Created publication does not round-trip to map");
     if(!comment.Contains("X=12345.125 Y=-54321.5"))throw new Exception("Coordinates changed with culture");
    }
    int count=handler.Writes;foreach(Position invalid in new Position[]{null,new Position{X=Double.NaN},new Position{X=2000000}}){bool failed=false;try{client.CreateMarker("stash","test",invalid).GetAwaiter().GetResult();}catch(ArgumentException){failed=true;}if(!failed)throw new Exception("Invalid marker coordinates accepted");}
    foreach(string name in new string[]{"",new string('x',71),"one\ntwo"}){bool failed=false;try{client.CreateMarker("stash",name,position).GetAwaiter().GetResult();}catch(ArgumentException){failed=true;}if(!failed)throw new Exception("Invalid marker name accepted");}if(handler.Writes!=count)throw new Exception("Invalid input reached network");
    handler.Status=HttpStatusCode.Unauthorized;bool expired=false;try{client.CreateMarker("base","test",position).GetAwaiter().GetResult();}catch(HubExpiredException){expired=true;}if(!expired)throw new Exception("Marker access revocation ignored");
    handler.Status=HttpStatusCode.OK;handler.Response="{\"data\":null,\"error\":\"database_unavailable\"}";bool rejected=false;count=handler.Writes;try{client.CreateMarker("other","test",position).GetAwaiter().GetResult();}catch(Exception){rejected=true;}if(!rejected||handler.Writes!=count+1)throw new Exception("Marker error was retried or treated as success");
   }
  }
  sealed class MarkerHandler:HttpMessageHandler{
   public int Writes;public Dictionary<string,object> Last;public HttpStatusCode Status=HttpStatusCode.OK;public string Response="{\"data\":[{\"id\":17}],\"error\":null}";
   protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel){
    if(request.Method!=HttpMethod.Post||request.RequestUri.AbsolutePath!="/api/legacy"||!request.Headers.Contains("Origin"))throw new Exception("Wrong marker API");Writes++;Last=new JavaScriptSerializer().DeserializeObject(await request.Content.ReadAsStringAsync()) as Dictionary<string,object>;
    if((string)Last["table"]!="squad_place_posts"||(string)Last["action"]!="insert")throw new Exception("Wrong marker write");return new HttpResponseMessage(Status){Content=new StringContent(Response)};
   }
  }
  public static void TestRenewal(){
   var handler=new RenewalHandler();using(HubClient h=new HubClient(handler)){DateTime now=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc);h.UtcNow=()=>now;h.Login("tester",null,"test-only-code",false).GetAwaiter().GetResult();
    for(int i=0;i<16;i++){h.EnsureSession(CancellationToken.None).GetAwaiter().GetResult();if(handler.Refreshes!=i)throw new Exception("Redundant session request");now=now.AddMinutes(30);h.EnsureSession(CancellationToken.None).GetAwaiter().GetResult();if(handler.Refreshes!=i+1)throw new Exception("Session did not renew");}
    handler.Fail=true;now=now.AddMinutes(30);try{h.EnsureSession(CancellationToken.None).GetAwaiter().GetResult();throw new Exception("Expected temporary error");}catch(HttpRequestException){}
    int before=handler.Refreshes;try{h.EnsureSession(CancellationToken.None).GetAwaiter().GetResult();}catch{}if(handler.Refreshes!=before)throw new Exception("Renewal retry storm");
    handler.Fail=false;now=now.AddSeconds(16);h.EnsureSession(CancellationToken.None).GetAwaiter().GetResult();
    handler.Revoked=true;now=now.AddMinutes(30);bool rejected=false;try{h.EnsureSession(CancellationToken.None).GetAwaiter().GetResult();}catch(HubExpiredException){rejected=true;}if(!rejected)throw new Exception("Revoked session accepted");
   }
  }
  sealed class RenewalHandler:HttpMessageHandler{
   public int Refreshes;public bool Fail,Revoked;
   protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel){
    if(request.Method!=HttpMethod.Post||request.RequestUri.Host!="scum-hub.ru"||!request.Headers.Contains("Origin"))throw new Exception("Invalid renewal request");
    if(request.RequestUri.AbsolutePath=="/api/member/login")return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{}")};
    if(request.RequestUri.AbsolutePath!="/api/member/refresh"||await request.Content.ReadAsStringAsync()!="{}")throw new Exception("Refresh must not contain credentials or identity");
    Refreshes++;if(Fail)throw new HttpRequestException("Temporary network failure");return new HttpResponseMessage(Revoked?HttpStatusCode.Unauthorized:HttpStatusCode.OK){Content=new StringContent("{}")};
   }
  }
  public static void TestProtocol(){
   ScriptedHandler handler=new ScriptedHandler();using(HubClient h=new HubClient(handler)){
    h.Login("tester","Rifler","test-only-code",false).GetAwaiter().GetResult();
    HubSnapshot a=h.Snapshot().GetAwaiter().GetResult(),b=h.Snapshot().GetAwaiter().GetResult();
    if(a.Markers.Count!=1||a.Markers[0].Id!="1"||b.Markers.Count!=1||b.Markers[0].Id!="2")throw new Exception("Snapshot replacement failed");
    bool expired=false;try{h.Snapshot().GetAwaiter().GetResult();}catch(HubExpiredException){expired=true;}if(!expired)throw new Exception("Expired session was not rejected");
   }
  }
  sealed class ScriptedHandler:HttpMessageHandler{
   int count;
   protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,System.Threading.CancellationToken cancel){
    count++;if(request.RequestUri.Host!="scum-hub.ru"||!request.Headers.Contains("Origin"))throw new Exception("Unexpected API origin");
    if(count==1){if(request.Method!=HttpMethod.Post||request.RequestUri.AbsolutePath!="/api/member/login")throw new Exception("Unexpected login request");string body=await request.Content.ReadAsStringAsync();if(!body.Contains("test-only-code"))throw new Exception("Login payload missing");return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{}")};}
    if(request.Method!=HttpMethod.Get||request.RequestUri.AbsolutePath!="/api/member/snapshot")throw new Exception("Non-read-only map request");
    if(count>=4)return new HttpResponseMessage(HttpStatusCode.Unauthorized){Content=new StringContent("{}")};
    string id=count==2?"1":"2";return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"member\":{\"nick\":\"test\"},\"posts\":[],\"markers\":[{\"id\":"+id+",\"x\":0.2,\"y\":0.3,\"name\":\"test\"}]}")};
   }
  }
  public void Dispose(){client.Dispose();}
 }
}
