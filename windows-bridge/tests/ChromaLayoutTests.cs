// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using Split65Companion;

public static class ChromaLayoutTests {
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=16*1024*1024};
    static Dictionary<string,object> D(params object[] pairs){Dictionary<string,object> d=new Dictionary<string,object>();for(int i=0;i<pairs.Length;i+=2)d.Add((string)pairs[i],pairs[i+1]);return d;}
    static Dictionary<string,object> Obj(object v){return (Dictionary<string,object>)v;}
    static object[] Arr(object v){return (object[])v;}
    static void Check(bool b){if(!b)throw new Exception("Chroma layout regression");}
    static void Reject(Action a){try{a();}catch(IOException){return;}throw new Exception("Unsupported Chroma export was accepted");}
    static object[] Leds(string points){List<object> values=new List<object>();foreach(string point in points.Split(' ')){string[] rc=point.Split(',');values.Add(D("row",Int32.Parse(rc[0]),"col",Int32.Parse(rc[1])));}return values.ToArray();}
    static Dictionary<string,object> Device(string id,int pid,string category,int x,int y,int width,int height,int cols,int rows,object[] leds,bool wdl){return D("id",id,"vendorId",category=="Keyboard"?0x342D:0x1532,"productId",pid,"category",category,"x",x,"y",y,"width",width,"height",height,"orientation",0,"leds",leds,"useWDL",wdl,"deviceContainerId","test-only-container","config",D("MatrixMaxCol",cols,"MatrixMaxRow",rows,"LedWidth",wdl||category=="Keyboard"?0:40,"LedHeight",wdl||category=="Keyboard"?0:40),"unknownField",D("preserve","原始字段"));}
    static Dictionary<string,object> Fixture(){
        // Anonymous geometry reproduces the supplied exports, without their
        // device identifiers, USB paths, effect titles or serial numbers.
        object[] keyboard=Leds("7,8 7,3 4,1 6,2 7,1 7,5 6,4 6,6 6,8 6,9 6,11 4,10 4,8 4,7 4,5 4,4 2,1 2,3 2,5 2,6 2,8 2,9 1,10 1,9 1,7 1,5 1,4 1,2 1,1 1,12 1,13 1,15 1,16 1,18 1,20 1,22 2,25 2,22 2,20 2,19 2,17 2,16 2,14 2,13 2,11 4,11 4,13 4,15 4,16 4,18 4,19 4,22 4,25 6,25 6,23 6,21 6,18 6,17 6,15 6,14 6,12 7,13 7,16 7,18 7,19 7,21 7,22 7,25");
        object[] native=Leds("0,4 2,4 4,4 5,4 6,2 5,0 4,0 2,0 0,0 0,2 5,2");
        object[] wdl=Leds("1,1 3,1 6,1 8,1 9,3 8,4 6,4 3,3 1,3 1,4 7,4");
        return D("version",1,"unknownRoot","keep", "profiles",new object[]{D("id","original-profile","title","当前配置","devices",new object[]{
            Device("wired",0xE4C6,"Keyboard",4680,4880,660,210,28,9,keyboard,false),
            Device("wireless",0xE4C8,"Keyboard",4680,4880,660,210,28,9,keyboard,false),
            Device("mouse-native",0xAF,"mouse",5360,4880,160,280,5,7,native,false),
            Device("mouse-wdl",0xAF,"mouse",5360,4800,250,370,6,11,wdl,true)},"layers",new object[]{D("id","original-group","children",new object[]{D("id","original-layer","name","static","params",D("color",16711680),"regions",new object[]{D("devices",new object[]{D("id","wired","leds",new object[]{keyboard[38],keyboard[40]})})})})})});
    }
    static double N(Dictionary<string,object> d,string key){return Convert.ToDouble(d[key]);}
    static double Width(Dictionary<string,object> d){return Math.Max(N(d,"width"),N(Obj(d["config"]),"MatrixMaxCol")*40);}
    static bool Mouse(Dictionary<string,object> d){return ((string)d["category"]).Equals("mouse",StringComparison.OrdinalIgnoreCase);}
    static void DropLayerIds(object[] layers){foreach(object v in layers){Dictionary<string,object> d=Obj(v);d.Remove("id");if(d.ContainsKey("children"))DropLayerIds(Arr(d["children"]));}}
    static int Invariants(string original,string repaired){
        Dictionary<string,object> a=Obj(Json.DeserializeObject(original)),b=Obj(Json.DeserializeObject(repaired));if(a.ContainsKey("unknownRoot"))Check((string)b["unknownRoot"]=="keep");
        object[] ap=Arr(a["profiles"]),bp=Arr(b["profiles"]);Check(ap.Length==bp.Length);int moved=0;
        for(int p=0;p<ap.Length;++p){Dictionary<string,object> pa=Obj(ap[p]),pb=Obj(bp[p]);object[] before=Arr(pa["devices"]),after=Arr(pb["devices"]);Check(before.Length==after.Length);
            bool changed=(string)pa["id"]!=(string)pb["id"];
            for(int i=0;i<before.Length;++i){Dictionary<string,object> old=Obj(before[i]),now=Obj(after[i]);
                object savedX=now["x"];
                if(Mouse(old)&&changed){++moved;Check(N(old,"y")==N(now,"y"));
                    foreach(object other in after)if(!System.Object.ReferenceEquals(now,other)){Dictionary<string,object> d=Obj(other);Check(N(now,"x")>=N(d,"x")+Width(d) || N(d,"x")>=N(now,"x")+Width(now));}
                    now["x"]=old["x"];
                }
                Check(Json.Serialize(old)==Json.Serialize(now));
                now["x"]=savedX;
            }
            object[] al=Arr(pa["layers"]),bl=Arr(pb["layers"]);DropLayerIds(al);DropLayerIds(bl);Check(Json.Serialize(al)==Json.Serialize(bl));
        }
        return moved;
    }
    public static void Run(string[] files){
        Dictionary<string,object> fixture=Fixture();string original=Json.Serialize(fixture);LayoutFixResult fixedLayout=ChromaLayout.Repair(original);
        Check(fixedLayout.Profiles==1 && fixedLayout.Mice==2 && Invariants(original,fixedLayout.Json)==2);
        Dictionary<string,object> bad=Obj(Json.DeserializeObject(original));bad["version"]=2;Reject(()=>ChromaLayout.Repair(Json.Serialize(bad)));
        bad=Obj(Json.DeserializeObject(original));Obj(Arr(Obj(Arr(bad["profiles"])[0])["devices"])[0])["orientation"]=90;Reject(()=>ChromaLayout.Repair(Json.Serialize(bad)));
        bad=Obj(Json.DeserializeObject(original));Obj(Arr(Obj(Arr(bad["profiles"])[0])["devices"])[0]).Remove("config");Reject(()=>ChromaLayout.Repair(Json.Serialize(bad)));
        string dir=Path.Combine(Path.GetTempPath(),"split65-chroma-test-"+Guid.NewGuid());Directory.CreateDirectory(dir);
        try{string source=Path.Combine(dir,"原始.ChromaEffects"),dest=Path.Combine(dir,"分离.ChromaEffects");File.WriteAllText(source,original);ChromaLayout.RepairFile(source,dest);Check(File.ReadAllText(source)==original);string output=File.ReadAllText(dest);Reject(()=>ChromaLayout.RepairFile(source,source));Reject(()=>ChromaLayout.RepairFile(source,dest));Check(File.ReadAllText(dest)==output);}finally{Directory.Delete(dir,true);}
        foreach(string file in files){string input=File.ReadAllText(file);LayoutFixResult result=ChromaLayout.Repair(input);Check(Invariants(input,result.Json)==result.Mice);Console.WriteLine("PASS: supplied Chroma export; moved mice="+result.Mice+", device/effect invariants preserved");}
        Console.WriteLine("PASS: Chroma native/WDL separation, both keyboard identities and bracket/P targets, metadata/effect preservation, unsupported format rejection and non-overwrite");
    }
}
