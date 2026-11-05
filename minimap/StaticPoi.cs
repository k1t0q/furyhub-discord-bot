using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;
namespace ScumPositionProbe {
 // Local game objects. This catalog is never merged into the HUB snapshot or sent to its API.
 public sealed class StaticPoiLayer {
  public static readonly string[] Categories={"police","bunker_abandoned","secret_bunker","research","ww2_bunker"};
  public static readonly string[] Labels={"ПОЛИЦАЙКА","БРЕНОР","СЕКРЕТКА","TEC1","WW2"};
  static readonly Color[] Colors={Color.FromArgb(104,183,255),Color.FromArgb(224,130,82),Color.FromArgb(184,142,244),Color.FromArgb(104,220,198),Color.FromArgb(216,197,140)};
  readonly List<HubMarker> entries=new List<HubMarker>();readonly HashSet<string> enabled=new HashSet<string>(Categories);readonly bool persist;
  public List<HubMarker> Visible{get;private set;}
  public StaticPoiLayer(bool persist=true){this.persist=persist;using(Stream stream=typeof(StaticPoiLayer).Assembly.GetManifestResourceStream("ScumPositionProbe.static-poi.json")){
   if(stream==null)throw new InvalidOperationException("Встроенные объекты SCUM не найдены.");
   string text;using(StreamReader reader=new StreamReader(stream))text=reader.ReadToEnd();
   var root=new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string,object>;
   var ids=new HashSet<string>();var counts=new Dictionary<string,int>();
   foreach(object raw in (object[])root["markers"]){var row=(Dictionary<string,object>)raw;string category=Convert.ToString(row["category"]);int index=Array.IndexOf(Categories,category);if(index<0)continue;
    double x=Convert.ToDouble(row["map_x"]),y=Convert.ToDouble(row["map_y"]);string id=Convert.ToString(row["id"]),sector=Convert.ToString(row["sector"]);
    if(!id.StartsWith("poi-",StringComparison.Ordinal)||!ids.Add(id)||Double.IsNaN(x)||Double.IsNaN(y)||Double.IsInfinity(x)||Double.IsInfinity(y)||x<0||x>1||y<0||y>1||MapMath.Sector(x,y)!=sector)throw new InvalidDataException("Некорректные координаты встроенного объекта.");
    string key=category+":"+sector;int ordinal;counts.TryGetValue(key,out ordinal);counts[key]=++ordinal;
    entries.Add(new HubMarker{Id=id,Kind="poi_"+category,Name=Labels[index]+" "+sector+(ordinal>1?" №"+ordinal:""),X=x,Y=y,Color=Colors[index]});
   }
  }
   if(persist)try{string path=UserSettings.PathFor("poi-layers.txt");if(File.Exists(path)){enabled.Clear();foreach(string key in File.ReadAllText(path).Split(','))if(Array.IndexOf(Categories,key)>=0)enabled.Add(key);}}catch{}
   Rebuild();
  }
  void Rebuild(){Visible=entries.FindAll(m=>enabled.Contains(m.Kind.Substring(4)));}
  public HubMarker FindMarker(string id){return entries.Find(m=>m.Id==id);}
  public bool IsEnabled(string category){return enabled.Contains(category);}
  public int Count(string category){return entries.FindAll(m=>m.Kind=="poi_"+category).Count;}
  public void SetEnabled(string category,bool value){if(Array.IndexOf(Categories,category)<0)throw new ArgumentException("Unknown POI category");if(value)enabled.Add(category);else enabled.Remove(category);Rebuild();if(persist)try{File.WriteAllText(UserSettings.PathFor("poi-layers.txt"),String.Join(",",CategoriesForSave()));}catch{}}
  string[] CategoriesForSave(){var result=new List<string>();foreach(string c in Categories)if(enabled.Contains(c))result.Add(c);return result.ToArray();}
  public static bool IsStatic(HubMarker marker){return marker!=null&&marker.Kind!=null&&marker.Kind.StartsWith("poi_",StringComparison.Ordinal);}
 }
}
