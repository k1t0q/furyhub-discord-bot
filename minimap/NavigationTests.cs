using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;
namespace ScumPositionProbe {
 public static class NavigationTests {
  static void Check(bool value,string message){if(!value)throw new Exception("Navigation: "+message);}
  static HubMarker Goal(float x,float y){return new HubMarker{Id="nav-test",Name="Destination",X=x,Y=y};}
  static Position World(float x,float y){return new Position{X=MapMath.Left-x*(MapMath.Left-MapMath.Right),Y=MapMath.Top-y*(MapMath.Top-MapMath.Bottom)};}
  static RoadGraph WalkingFixture(bool river){
   using(MemoryStream stream=new MemoryStream()){
    using(BinaryWriter w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true)){
     w.Write(new char[]{'S','H','R','N','1'});w.Write(4);w.Write(3);
     foreach(PointF p in new PointF[]{new PointF(.4f,.4f),new PointF(.4f,.42f),new PointF(.41f,.42f),new PointF(.41f,.4f)}){w.Write(p.X);w.Write(p.Y);}
     for(int i=0;i<3;i++){w.Write(i);w.Write(i+1);w.Write((byte)0);}
     byte[] mask=new byte[1024*1024/8];if(river)for(int y=395;y<=415;y++)for(int x=413;x<=417;x++){int index=y*1024+x;mask[index/8]|=(byte)(1<<(index%8));}
     w.Write(new char[]{'W','A','T','R'});w.Write(1024);w.Write(1024);w.Write(mask.Length);w.Write(mask);
    }stream.Position=0;return RoadGraph.Read(stream);
   }
  }
  public static void Run(List<string> results){
   DateTime presenceNow=new DateTime(2026,11,2,0,0,0,DateTimeKind.Utc);
   Check(AllyLive.Visible(presenceNow,presenceNow.AddSeconds(7))&&!AllyLive.Visible(presenceNow,presenceNow.AddMilliseconds(7001)),"ally hides after seven seconds");
   using(HubClient presenceHub=new HubClient())using(AllyLive presence=new AllyLive(presenceHub)){
    DateTime sampleNow=presenceNow;presence.UtcNow=()=>sampleNow;Position stationary=new Position{X=100,Y=100};presence.Observe(stationary,0);long initial=presence.Revision;
    sampleNow=sampleNow.AddSeconds(1);presence.Observe(stationary,0);Check(presence.Revision==initial,"stationary samples throttled");sampleNow=sampleNow.AddSeconds(1);presence.Observe(stationary,0);Check(presence.Revision==initial+1,"fresh stationary samples confirm presence every two seconds");
    sampleNow=sampleNow.AddSeconds(8);Check(presence.Revision==initial+1,"no confirmation without a newly observed coordinate");
   }results.Add("PASS seven-second ally expiry and fresh stationary confirmation throttling");
   ZoomKeys zoom=new ZoomKeys();Check(zoom.Update(true,true,false,0)==1&&zoom.Update(true,true,false,25)==0&&zoom.Update(true,true,false,400)==1,"zoom press and delayed repeat");
   Check(zoom.Update(false,true,false,425)==0&&zoom.Update(true,false,true,450)==-1,"focus/menu guard and zoom out");
   Check(zoom.Update(true,true,true,500)==0&&zoom.Update(true,false,false,525)==0&&zoom.Update(true,true,false,550)==1,"opposing arrows and release reset");results.Add("PASS keyboard zoom direction, repeat, focus guard and release");
   Position viewer=World(.5f,.5f);double half=500*100/(MapMath.Left-MapMath.Right);
   HubMarker near=Goal(.501f,.5f);near.Id="near";near.Kind="base";
   HubMarker corner=Goal((float)(.5+half*.9),(float)(.5-half*.9));corner.Id="corner";corner.Kind="other";
   HubMarker farMarker=Goal(.7f,.7f);farMarker.Id="far";farMarker.Kind="stash";
   HubMarker edge=Goal((float)(.5+half*1.05),.5f),footer=Goal(.5f,(float)(.5+half*.95));
   Check(MapMath.MarkerInView(near,viewer,500,320,320)&&MapMath.MarkerInView(corner,viewer,500,320,320),"nearby section matches visible square, including corners outside circular radius");
   Check(!MapMath.MarkerInView(edge,viewer,500,320,320)&&MapMath.MarkerInView(edge,viewer,1000,320,320),"nearby section responds to zoom");
   Check(!MapMath.MarkerInView(footer,viewer,500,320,320)&&!MapMath.MarkerInView(near,null,500,320,320),"footer and missing position are excluded");
   HubMarker stashNear=Goal(.501f,.5f);stashNear.Id="stash-post-1";stashNear.Kind="stash";
   HubMarker stashCorner=Goal((float)(.5+half*.9),(float)(.5-half*.9));stashCorner.Id="stash-post-2";stashCorner.Kind="stash";
   HubMarker stashFar=Goal(.7f,.7f);stashFar.Id="stash-post-3";stashFar.Kind="stash";
   HubMarker stashEdge=Goal((float)(.5+half*1.05),.5f);stashEdge.Id="stash-post-4";stashEdge.Kind="stash";
   var stashMarkers=new List<HubMarker>{stashFar,stashCorner,near,stashNear,stashEdge};var visibleStashes=ProbeForm.VisibleStashes(stashMarkers,viewer,500,320,320);
   Check(visibleStashes.Count==2&&visibleStashes[0].Marker.Id==stashNear.Id&&visibleStashes[1].Marker.Id==stashCorner.Id&&visibleStashes[0].MetresAway.HasValue,"stash picker uses visible viewport and distance, excludes bases and distant stashes");
   Check(ProbeForm.VisibleStashes(stashMarkers,viewer,1000,320,320).Count==3&&ProbeForm.VisibleStashes(stashMarkers,null,1000,320,320).Count==0,"stash picker follows zoom and handles missing position");
   var movedStashes=ProbeForm.VisibleStashes(stashMarkers,World(.7f,.7f),500,320,320);Check(movedStashes.Count==1&&movedStashes[0].Marker.Id==stashFar.Id,"stash picker follows player movement");results.Add("PASS nearby stash picker, exact viewport, nearest-first distances, zoom, movement and type filtering");
   var destinations=ProbeForm.GroupNavigationMarkers(new List<HubMarker>{farMarker,corner,near},viewer,500,320,320);
   Check(destinations[0].Header=="НА МИНИКАРТЕ"&&destinations[1].Marker.Id=="near"&&destinations[2].Marker.Id=="corner","visible destinations lead the list sorted by distance");
   Check(destinations.FindAll(row=>row.Marker!=null&&row.Marker.Id=="near").Count==2&&destinations.Exists(row=>row.Header=="СХРОНЫ")&&destinations.Exists(row=>row.Header=="БАЗЫ")&&destinations.Exists(row=>row.Header=="ДРУГОЕ"),"complete category lists remain available below nearby shortcuts");
   var moved=ProbeForm.GroupNavigationMarkers(new List<HubMarker>{farMarker,corner,near},World(.7f,.7f),500,320,320);
   Check(moved[1].Marker.Id=="far"&&!moved.Exists(row=>row.Marker!=null&&row.Marker.Id=="near"&&row.MetresAway.HasValue),"nearby shortcuts follow current player position");
   results.Add("PASS visible-map destination shortcuts, nearest-first order, zoom, viewport corners, footer and movement");
   RoadGraph dry=WalkingFixture(false);PointF nearby=new PointF(.4f,.4f);RoadRoute walk=dry.Find(nearby,Goal(.41f,.4f),TravelMode.Foot);
   Check(walk.DirectFoot&&walk.Points.Length==2&&Math.Abs(walk.Remaining-RoadGeometry.Distance(nearby,walk.Target))<.1,"nearby marker avoids the road detour");
   double walkingDistance=walk.Remaining;walk.Update(new PointF(.405f,.4f));Check(walk.Remaining<walkingDistance*.6&&RoadGeometry.Distance(walk.Projection,new PointF(.405f,.4f))<.1,"walking line and distance follow current position");
   walk.Update(new PointF(.4095f,.4f));Check(walk.Arrived&&walk.Remaining==0,"walking arrival remains within 30m");
   RoadRoute remote=dry.Find(new PointF(.7f,.7f),Goal(.71f,.7f),TravelMode.Foot);Check(remote.DirectFoot,"walking destination needs no nearby road");
   RoadRoute far=dry.Find(nearby,Goal(.44f,.4f),TravelMode.Foot);Check(far.DirectFoot,"long walking trips cut across dry land");
   RoadRoute river=WalkingFixture(true).Find(nearby,Goal(.41f,.4f));Check(!river.DirectFoot&&river.Points.Length==4&&river.Remaining>700,"nearby target across water follows road detour");
   RoadRoute driving=dry.Find(nearby,Goal(.41f,.4f),TravelMode.Car);Check(!driving.DirectFoot&&driving.Remaining>700,"car retains roads even for nearby targets");
   RoadRoute footRiver=WalkingFixture(true).Find(nearby,Goal(.41f,.4f),TravelMode.Foot);Check(!footRiver.DirectFoot&&footRiver.Remaining<driving.Remaining,"walking search bypasses river with a shorter land path");
   results.Add("PASS walking and driving modes, long land shortcuts, water detours and arrival");
   RoadGraph u=RoadGraph.Create(new PointF[]{new PointF(.1f,.1f),new PointF(.1f,.2f),new PointF(.2f,.2f),new PointF(.2f,.1f)},new int[,]{{0,1,0},{1,2,1},{2,3,0}});
   RoadRoute detour=u.Find(new PointF(.1f,.1f),Goal(.2f,.1f));
   Check(detour.Points.Length==4&&detour.Remaining>4500,"road detour must not cut across missing road");
   double before=detour.Remaining;detour.Update(new PointF(.1f,.15f));Check(detour.Remaining<before&&detour.Remaining>3700,"remaining distance follows road bends");
   RoadGraph straight=RoadGraph.Create(new PointF[]{new PointF(.1f,.1f),new PointF(.2f,.1f)},new int[,]{{0,1,0}});
   RoadRoute partial=straight.Find(new PointF(.125f,.1f),Goal(.175f,.1f));Check(Math.Abs(partial.Remaining-RoadGeometry.Width*.05)<1,"same edge uses partial segment, no detour to endpoints");
   partial.Update(new PointF(.1745f,.1f));Check(partial.Arrived&&partial.Remaining==0,"arrival within 30m clears distance");
   RoadRoute offroad=straight.Find(new PointF(.125f,.105f),Goal(.175f,.105f));Check(offroad.Remaining>partial.Remaining+850,"off-road approaches counted");offroad.Update(new PointF(.125f,.103f));Check(offroad.Deviation<1,"initial off-road leg does not repeatedly reroute");
   RoadGraph split=RoadGraph.Create(new PointF[]{new PointF(.1f,.1f),new PointF(.2f,.1f),new PointF(.8f,.8f),new PointF(.9f,.8f)},new int[,]{{0,1,0},{2,3,0}});
   bool rejected=false;try{split.Find(new PointF(.1f,.1f),Goal(.9f,.8f));}catch(Exception){rejected=true;}Check(rejected,"disconnected roads never get a straight-line substitute");
   RoadGraph spur=RoadGraph.Create(new PointF[]{new PointF(.1f,.1f),new PointF(.5f,.1f),new PointF(.49f,.101f),new PointF(.51f,.101f)},new int[,]{{0,1,0},{2,3,0}});
   RoadRoute accessible=spur.Find(new PointF(.12f,.1f),Goal(.5f,.102f));Check(accessible.Points.Length>=2&&accessible.Remaining>5000,"nearby isolated spur must not hide the connected access road");
   results.Add("PASS road routing, partial edges, detours, off-road legs, arrival and disconnected components");
   RoadGraph real=RoadGraph.Load();Check(real.Nodes.Length>10000&&real.Edges.Length>10000,"embedded island road graph");
   Stopwatch watch=Stopwatch.StartNew();RoadRoute across=real.Find(new PointF(.3f,.3f),Goal(.65f,.88f));watch.Stop();Check(across.Points.Length>20&&across.Remaining>1000,"cross-island roads and bridge connection");
   foreach(PointF p in across.Points)Check(p.X>=0&&p.Y>=0&&p.X<=1&&p.Y<=1,"route stays on map");
   rejected=false;try{real.Find(new PointF(.3f,.3f),Goal(.8f,.8f));}catch(Exception){rejected=true;}Check(rejected,"offshore goal cannot use a straight approach through open water");
   results.Add("PASS embedded island graph: "+real.Nodes.Length+" nodes, "+real.Edges.Length+" edges; cross-island route "+watch.ElapsedMilliseconds+" ms");
   NavigationSession session=new NavigationSession();session.Plan(World(.3f,.3f),Goal(.65f,.88f)).GetAwaiter().GetResult();Check(session.Route!=null,"session starts route");session.Update(World(.65f,.88f));Check(session.Route==null&&!session.Busy,"arrival removes active line");
   var pending=session.Plan(World(.3f,.3f),Goal(.65f,.88f));session.Cancel("Cancelled");pending.GetAwaiter().GetResult();Check(session.Route==null&&!session.Busy,"cancelled asynchronous calculation cannot restore line");
   results.Add("PASS navigation session arrival and asynchronous cancellation");
   DateTime fakeTime=DateTime.UtcNow;NavigationSession replanning=new NavigationSession(()=>fakeTime);replanning.Plan(World(.3f,.3f),Goal(.65f,.88f)).GetAwaiter().GetResult();fakeTime=fakeTime.AddSeconds(3);replanning.Update(World(.55f,.3f));Check(replanning.BackgroundPlanning!=null,"departure from route starts automatic replanning");replanning.BackgroundPlanning.GetAwaiter().GetResult();Check(replanning.Route!=null&&RoadGeometry.Distance(replanning.Route.Origin,new PointF(.55f,.3f))<1,"replanned route starts at new position");
   replanning.RefreshTarget(Goal(.6f,.88f),World(.55f,.3f));replanning.BackgroundPlanning.GetAwaiter().GetResult();Check(replanning.Route!=null&&RoadGeometry.Distance(replanning.Route.Target,new PointF(.6f,.88f))<1,"edited HUB marker updates active destination");
   results.Add("PASS automatic replanning after deviation and destination edits");
  }
  public static int ExportCheck(string[] args){
   try{float x=float.Parse(args[1],CultureInfo.InvariantCulture),y=float.Parse(args[2],CultureInfo.InvariantCulture),gx=float.Parse(args[3],CultureInfo.InvariantCulture),gy=float.Parse(args[4],CultureInfo.InvariantCulture);RoadRoute route=RoadGraph.Load().Find(new PointF(x,y),Goal(gx,gy));List<double[]> points=new List<double[]>();foreach(PointF p in route.Points)points.Add(new double[]{p.X,p.Y});File.WriteAllText("route-check.json",new JavaScriptSerializer().Serialize(new {points=points,metres=route.Remaining,origin=new float[]{x,y},target=new float[]{gx,gy}}));return 0;}catch(Exception e){File.WriteAllText("route-check.json",new JavaScriptSerializer().Serialize(new {error=e.Message}));return 1;}
  }
 }
}
