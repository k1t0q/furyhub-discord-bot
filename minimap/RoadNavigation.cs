using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
namespace ScumPositionProbe {
 public static class RoadGeometry {
  public const double Width=(MapMath.Left-MapMath.Right)/100,Height=(MapMath.Top-MapMath.Bottom)/100;
  public static PointF FromPosition(Position p){return new PointF((float)MapMath.NormX(p.X),(float)MapMath.NormY(p.Y));}
  public static double Distance(PointF a,PointF b){double x=(a.X-b.X)*Width,y=(a.Y-b.Y)*Height;return Math.Sqrt(x*x+y*y);}
  public static double Project(PointF p,PointF a,PointF b,out PointF point){double dx=(b.X-a.X)*Width,dy=(b.Y-a.Y)*Height,den=dx*dx+dy*dy;double t=den==0?0:Math.Max(0,Math.Min(1,((p.X-a.X)*Width*dx+(p.Y-a.Y)*Height*dy)/den));point=new PointF((float)(a.X+(b.X-a.X)*t),(float)(a.Y+(b.Y-a.Y)*t));return t;}
  public static string Metres(double d){return d>=1000?(d/1000).ToString("0.0",System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))+" км":Math.Ceiling(Math.Max(0,d)).ToString("0")+" м";}
 }
 public sealed class RoadRoute {
  public PointF Origin,Target;public PointF[] Points;public string TargetId,TargetName;public int Segment;public PointF Projection;
  public double Remaining,Deviation;public bool Arrived,DirectFoot;
  public void Update(PointF current){
   if(RoadGeometry.Distance(current,Target)<=30){Arrived=true;Remaining=0;return;}
   if(DirectFoot){PointF foot;RoadGeometry.Project(current,Origin,Target,out foot);Deviation=RoadGeometry.Distance(current,foot);Projection=current;Segment=0;Remaining=RoadGeometry.Distance(current,Target);return;}
   double nearest=Double.PositiveInfinity;int best=Segment;PointF projected=Points[Math.Min(Segment,Points.Length-1)];
   for(int i=Math.Max(0,Segment-3);i<Points.Length-1;i++){
    PointF q;RoadGeometry.Project(current,Points[i],Points[i+1],out q);double d=RoadGeometry.Distance(current,q);
    if(d<nearest-2){nearest=d;best=i;projected=q;}
   }
   // The first and last approaches are explicit off-road legs.
   if(best==0){PointF q;RoadGeometry.Project(current,Origin,Points[0],out q);nearest=Math.Min(nearest,RoadGeometry.Distance(current,q));}
   PointF last=Points[Points.Length-1];PointF finish;RoadGeometry.Project(current,last,Target,out finish);
   double finishDistance=RoadGeometry.Distance(current,finish);
   if(best>=Points.Length-3&&finishDistance<nearest){Segment=Points.Length-1;Projection=last;Deviation=finishDistance;Remaining=RoadGeometry.Distance(current,Target);return;}
   Segment=best;Projection=projected;Deviation=nearest;
   Remaining=RoadGeometry.Distance(current,Projection);
   if(Segment<Points.Length-1)Remaining+=RoadGeometry.Distance(Projection,Points[Segment+1]);
   for(int i=Segment+1;i<Points.Length-1;i++)Remaining+=RoadGeometry.Distance(Points[i],Points[i+1]);
   Remaining+=RoadGeometry.Distance(last,Target);
  }
 }
 public sealed class RoadGraph {
  public sealed class Edge {public int A,B;public double Length,Cost;}
  public PointF[] Nodes;public Edge[] Edges;List<int>[] neighbours;int[] components;int waterWidth,waterHeight;byte[] water;
  sealed class Snap {public int Edge;public PointF Point;public double Distance;}
  public static RoadGraph Load(){using(Stream stream=typeof(RoadGraph).Assembly.GetManifestResourceStream("ScumPositionProbe.roads.bin")){if(stream==null)throw new Exception("Не найдена сеть дорог.");return Read(stream);}}
  public static RoadGraph Read(Stream stream){
   using(BinaryReader reader=new BinaryReader(stream)){
    if(new String(reader.ReadChars(5))!="SHRN1")throw new Exception("Некорректная сеть дорог.");
    int n=reader.ReadInt32(),m=reader.ReadInt32();if(n<2||n>500000||m<1||m>1000000)throw new Exception("Некорректный размер сети дорог.");
    PointF[] nodes=new PointF[n];for(int i=0;i<n;i++){float x=reader.ReadSingle(),y=reader.ReadSingle();if(Single.IsNaN(x)||Single.IsInfinity(x)||Single.IsNaN(y)||Single.IsInfinity(y)||x<0||x>1||y<0||y>1)throw new Exception("Некорректные координаты дороги.");nodes[i]=new PointF(x,y);}
    int[,] pairs=new int[m,3];for(int i=0;i<m;i++){pairs[i,0]=reader.ReadInt32();pairs[i,1]=reader.ReadInt32();pairs[i,2]=reader.ReadByte();}
    RoadGraph graph=Create(nodes,pairs);
    if(stream.Position<stream.Length){if(new String(reader.ReadChars(4))!="WATR")throw new Exception("Некорректная маска воды.");graph.waterWidth=reader.ReadInt32();graph.waterHeight=reader.ReadInt32();int length=reader.ReadInt32();if(graph.waterWidth<1||graph.waterHeight<1||graph.waterWidth>4096||graph.waterHeight>4096||length!=(graph.waterWidth*graph.waterHeight+7)/8)throw new Exception("Некорректный размер маски воды.");graph.water=reader.ReadBytes(length);if(graph.water.Length!=length)throw new Exception("Неполная маска воды.");}
    return graph;
   }
  }
  public static RoadGraph Create(PointF[] nodes,int[,] pairs){
   RoadGraph graph=new RoadGraph();graph.Nodes=nodes;graph.Edges=new Edge[pairs.GetLength(0)];graph.neighbours=new List<int>[nodes.Length];for(int i=0;i<nodes.Length;i++)graph.neighbours[i]=new List<int>();
   for(int i=0;i<graph.Edges.Length;i++){int a=pairs[i,0],b=pairs[i,1];if(a<0||b<0||a>=nodes.Length||b>=nodes.Length)throw new Exception("Некорректное соединение дорог.");double length=RoadGeometry.Distance(nodes[a],nodes[b]);graph.Edges[i]=new Edge{A=a,B=b,Length=length,Cost=length*(pairs[i,2]==0?1:1.03)};graph.neighbours[a].Add(i);graph.neighbours[b].Add(i);}
   graph.components=new int[nodes.Length];for(int i=0;i<nodes.Length;i++)graph.components[i]=-1;
   int component=0;for(int seed=0;seed<nodes.Length;seed++){if(graph.components[seed]>=0)continue;Queue<int> queue=new Queue<int>();queue.Enqueue(seed);graph.components[seed]=component;
    while(queue.Count>0){int a=queue.Dequeue();foreach(int index in graph.neighbours[a]){Edge edge=graph.Edges[index];int b=edge.A==a?edge.B:edge.A;if(graph.components[b]<0){graph.components[b]=component;queue.Enqueue(b);}}}component++;
   }
   return graph;
  }
  bool CrossesWater(PointF a,PointF b,double minimumWet=30){
   if(water==null)return false;double length=RoadGeometry.Distance(a,b);if(length<15&&minimumWet>=30)return false;int steps=Math.Max(1,(int)Math.Ceiling(length/5));double wet=0;
   for(int i=0;i<=steps;i++){double t=(double)i/steps;int x=Math.Max(0,Math.Min(waterWidth-1,(int)((a.X+(b.X-a.X)*t)*waterWidth))),y=Math.Max(0,Math.Min(waterHeight-1,(int)((a.Y+(b.Y-a.Y)*t)*waterHeight)));int index=y*waterWidth+x;if((water[index/8]&(1<<(index%8)))!=0){wet+=length/steps;if(wet>=minimumWet)return true;}else wet=0;}
   return false;
  }
  Dictionary<int,Snap> Nearest(PointF point){Dictionary<int,Snap> candidates=new Dictionary<int,Snap>();for(int i=0;i<Edges.Length;i++){Edge e=Edges[i];PointF q;RoadGeometry.Project(point,Nodes[e.A],Nodes[e.B],out q);double distance=RoadGeometry.Distance(point,q);int component=components[e.A];Snap best;if(distance<=1200&&(!candidates.TryGetValue(component,out best)||distance<best.Distance)&&!CrossesWater(point,q))candidates[component]=new Snap{Edge=i,Point=q,Distance=distance};}return candidates;}
  public RoadRoute Find(PointF from,HubMarker marker){
   PointF to=new PointF((float)marker.X,(float)marker.Y);
   // Nearby destinations use a walking guide. Only do this with a known water
   // mask, and check the entire direct leg before considering road approaches.
   if(water!=null&&RoadGeometry.Distance(from,to)<=500&&!CrossesWater(from,to,1)){
    RoadRoute foot=new RoadRoute{Origin=from,Target=to,TargetId=marker.Id,TargetName=marker.Name,Points=new PointF[]{from,to},Projection=from,DirectFoot=true};foot.Update(from);return foot;
   }
   Dictionary<int,Snap> starts=Nearest(from),ends=Nearest(to);Snap start=null,end=null;double approach=Double.PositiveInfinity;
   if(starts.Count==0||ends.Count==0)throw new Exception("Нет подхода по суше к дороге в пределах 1,2 км.");
   foreach(KeyValuePair<int,Snap> candidate in starts){Snap finish;if(ends.TryGetValue(candidate.Key,out finish)&&candidate.Value.Distance+finish.Distance<approach){start=candidate.Value;end=finish;approach=start.Distance+end.Distance;}}
   if(start==null)throw new Exception("Дорожного соединения нет. Прямой маршрут через воду не строится.");
   Edge se=Edges[start.Edge],ee=Edges[end.Edge];List<PointF> line=new List<PointF>();line.Add(start.Point);
   if(start.Edge==end.Edge)line.Add(end.Point);
   else{
    double[] costs=new double[Nodes.Length];int[] previous=new int[Nodes.Length];for(int i=0;i<costs.Length;i++){costs[i]=Double.PositiveInfinity;previous[i]=-1;}
    Heap heap=new Heap();int[] initial={se.A,se.B};foreach(int node in initial){double d=RoadGeometry.Distance(start.Point,Nodes[node])*se.Cost/Math.Max(.0001,se.Length);costs[node]=d;heap.Push(node,d+RoadGeometry.Distance(Nodes[node],end.Point),d);}
    double best=Double.PositiveInfinity;int last=-1;
    while(heap.Count>0){Heap.Item item=heap.Pop();if(item.Score>=best)break;if(item.Cost>costs[item.Node]+.00001)continue;int a=item.Node;
     if(a==ee.A||a==ee.B){double total=costs[a]+RoadGeometry.Distance(Nodes[a],end.Point)*ee.Cost/Math.Max(.0001,ee.Length);if(total<best){best=total;last=a;}}
     foreach(int index in neighbours[a]){Edge edge=Edges[index];int b=edge.A==a?edge.B:edge.A;double cost=costs[a]+edge.Cost;if(cost+1e-7<costs[b]){costs[b]=cost;previous[b]=a;heap.Push(b,cost+RoadGeometry.Distance(Nodes[b],end.Point),cost);}}
    }
    if(last<0)throw new Exception("Дорожного соединения нет. Прямой маршрут через воду не строится.");
    List<PointF> middle=new List<PointF>();for(int n=last;n>=0;n=previous[n])middle.Add(Nodes[n]);middle.Reverse();line.AddRange(middle);line.Add(end.Point);
   }
   // Remove zero-length joins while preserving all actual road bends.
   for(int i=line.Count-1;i>0;i--)if(RoadGeometry.Distance(line[i-1],line[i])<.05)line.RemoveAt(i);
   if(line.Count==1)line.Add(line[0]);
   RoadRoute route=new RoadRoute{Origin=from,Target=to,TargetId=marker.Id,TargetName=marker.Name,Points=line.ToArray(),Projection=line[0]};route.Update(from);return route;
  }
  sealed class Heap {
   public struct Item {public int Node;public double Score,Cost;}
   readonly List<Item> items=new List<Item>();public int Count{get{return items.Count;}}
   public void Push(int node,double score,double cost){Item value=new Item{Node=node,Score=score,Cost=cost};items.Add(value);int i=items.Count-1;while(i>0){int p=(i-1)/2;if(items[p].Score<=score)break;items[i]=items[p];i=p;}items[i]=value;}
   public Item Pop(){Item first=items[0],last=items[items.Count-1];items.RemoveAt(items.Count-1);if(items.Count>0){int i=0;while(i*2+1<items.Count){int c=i*2+1;if(c+1<items.Count&&items[c+1].Score<items[c].Score)c++;if(items[c].Score>=last.Score)break;items[i]=items[c];i=c;}items[i]=last;}return first;}
  }
 }
 public sealed class NavigationSession {
  readonly Task<RoadGraph> graph=Task.Run(()=>RoadGraph.Load());readonly Func<DateTime> now;int version;DateTime replanAt=DateTime.MinValue;PointF lastReplan;HubMarker target;internal Task BackgroundPlanning;
  public NavigationSession():this(()=>DateTime.UtcNow){}
  internal NavigationSession(Func<DateTime> clock){now=clock;}
  public RoadRoute Route;public bool Busy;public string Status="Выбери метку HUB и проложи маршрут.";public event EventHandler Changed;
  public string GoalId{get{return target==null?null:target.Id;}}
  public void RefreshTarget(HubMarker marker,Position position){
   if(target==null)return;bool moved=RoadGeometry.Distance(new PointF((float)target.X,(float)target.Y),new PointF((float)marker.X,(float)marker.Y))>2;
   target.Name=marker.Name;if(Route!=null)Route.TargetName=marker.Name;
   if(moved&&position!=null)BackgroundPlanning=Plan(position,marker);
  }
  void Notify(){if(Changed!=null)Changed(this,EventArgs.Empty);}
  public void Cancel(string message){version++;Busy=false;Route=null;target=null;Status=message;Notify();}
  public async Task Plan(Position position,HubMarker marker){
   if(RoadGeometry.Distance(RoadGeometry.FromPosition(position),new PointF((float)marker.X,(float)marker.Y))<=30){Cancel("Ты прибыл к метке «"+marker.Name+"».");return;}
   int own=++version;Busy=true;target=new HubMarker{Id=marker.Id,Name=marker.Name,X=marker.X,Y=marker.Y};HubMarker selected=target;
   replanAt=now();lastReplan=RoadGeometry.FromPosition(position);PointF start=lastReplan;Status=Route==null?"Строю маршрут…":"Перестраиваю маршрут…";Notify();
   try{RoadGraph roads=await graph;RoadRoute route=await Task.Run(()=>roads.Find(start,selected));if(own!=version)return;Route=route;if(route.Arrived){Cancel("Ты прибыл к метке «"+marker.Name+"».");return;}Status=(route.DirectFoot?"Пешком · ":"")+"До цели: "+RoadGeometry.Metres(route.Remaining)+" · "+marker.Name;}
   catch(Exception ex){if(own==version){Route=null;Status=ex.Message;}}
   finally{if(own==version){Busy=false;Notify();}}
  }
  public void Update(Position position){
   if(Route==null)return;PointF current=RoadGeometry.FromPosition(position);Route.Update(current);
   if(Route.Arrived){Cancel("Ты прибыл к метке «"+Route.TargetName+"».");return;}
   if(!Busy)Status=(Route.DirectFoot?"Пешком · ":"")+"До цели: "+RoadGeometry.Metres(Route.Remaining)+" · "+Route.TargetName;Notify();
   if(!Busy&&Route.Deviation>60&&(now()-replanAt).TotalSeconds>=2&&RoadGeometry.Distance(current,lastReplan)>15&&target!=null)BackgroundPlanning=Plan(position,target);
  }
 }
}
