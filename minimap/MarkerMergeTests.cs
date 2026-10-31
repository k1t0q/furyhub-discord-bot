using System;
using System.Collections.Generic;
namespace ScumPositionProbe {
 public static class MarkerMergeTests {
  static void Check(bool value,string message){if(!value)throw new Exception(message);}
  public static void Run(){
   string fixture=@"{""member"":{""nick"":""tester""},""markers"":[
    {""id"":""base-post-44"",""name"":""тест"",""x"":0.3,""y"":0.4},
    {""id"":""standalone"",""name"":""тест"",""x"":0.4,""y"":0.5},
    {""id"":""other-post-4"",""name"":""Название на карте"",""x"":0.6,""y"":0.7,""color"":""#123456""},
    {""id"":""stash-post-6"",""name"":""Схрон #6"",""type"":""stash"",""x"":0.2,""y"":0.3}
   ],""posts"":[
    {""id"":2,""kind"":""stash"",""comment"":""Наш схрон\nКоординаты: X=100,5 Y=-200,25""},
    {""id"":3,""kind"":""base"",""comment"":""База у дороги\nКоординаты: X: 0 Y: 0""},
    {""id"":4,""kind"":""other"",""comment"":""Описание\nX=1 Y=2""},
    {""id"":5,""kind"":""other"",""comment"":""Нет координат""},
    {""id"":6,""kind"":""stash"",""comment"":""Новое название\nX=10 Y=20""}
   ]}";
   HubSnapshot snapshot=HubClient.Parse(fixture);Dictionary<string,HubMarker> byId=new Dictionary<string,HubMarker>();foreach(HubMarker m in snapshot.Markers)byId.Add(m.Id,m);
   Check(byId.Count==5,"Merged marker count");
   Check(!byId.ContainsKey("base-post-44"),"Deleted post must hide its orphan marker");
   Check(byId["standalone"].Name=="тест","A legitimate test-named marker must not be filtered by name");
   Check(byId["stash-post-2"].Name=="Наш схрон"&&Math.Abs(byId["stash-post-2"].X-MapMath.NormX(100.5))<1e-9&&Math.Abs(byId["stash-post-2"].Y-MapMath.NormY(-200.25))<1e-9,"Post coordinates and title");
   Check(byId["base-post-3"].Name=="База у дороги","Base from post");
   Check(byId["other-post-4"].Name=="Название на карте"&&byId["other-post-4"].X==0.6&&byId["other-post-4"].Color.R==18,"Explicit marker wins and is not duplicated");
   Check(byId["stash-post-6"].Name=="Новое название","Legacy placeholder title replaced as in HUB");
   HubSnapshot deleted=HubClient.Parse(@"{""markers"":[{""id"":""stash-post-2"",""name"":""Old"",""x"":0.2,""y"":0.3}],""posts"":[]}");Check(deleted.Markers.Count==0,"Deletion must remove linked markers");
  }
 }
}
