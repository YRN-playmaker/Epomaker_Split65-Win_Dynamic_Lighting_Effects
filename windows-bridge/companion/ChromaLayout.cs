// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Split65Companion {
public sealed class LayoutFixResult {
    public string Json;public int Profiles,Mice;
}
// The observed Chroma v1 exports use 40-pixel logical cells, even where the
// keyboard illustration is narrower. This is an inferred layout workaround,
// not a change to HID geometry, key bindings, effect targets or device IDs.
public static class ChromaLayout {
    const int Cell=40,Gap=8*Cell,MaxLength=16*1024*1024;
    static Dictionary<string,object> Object(object value) {
        Dictionary<string,object> d=value as Dictionary<string,object>;
        if(d==null)throw new IOException("Unsupported Chroma object format.");return d;
    }
    static object Get(Dictionary<string,object> d,string name) {
        object v;if(!d.TryGetValue(name,out v))throw new IOException("Missing Chroma field: "+name);return v;
    }
    static object[] Array(object v) {
        object[] a=v as object[];if(a==null)throw new IOException("Unsupported Chroma array format.");return a;
    }
    static double Number(object v) {
        if(!(v is int || v is long || v is decimal || v is double || v is float))throw new IOException("Unsupported Chroma coordinate.");
        double n=Convert.ToDouble(v);
        if(Double.IsNaN(n)||Double.IsInfinity(n)||Math.Abs(n)>10000000)throw new IOException("Chroma coordinate outside the supported range.");return n;
    }
    static string Text(Dictionary<string,object> d,string name) {
        string s=Get(d,name) as string;if(s==null)throw new IOException("Unsupported Chroma text field: "+name);return s;
    }
    static bool Mouse(Dictionary<string,object> d){return Text(d,"category").Equals("mouse",StringComparison.OrdinalIgnoreCase);}
    static bool Split65(Dictionary<string,object> d) {
        if(!Text(d,"category").Equals("keyboard",StringComparison.OrdinalIgnoreCase))return false;
        double pid=Number(Get(d,"productId"));
        return Number(Get(d,"vendorId"))==0x342D && (pid==0xE4C6 || pid==0xE4C7 || pid==0xE4C8);
    }
    static double Extent(Dictionary<string,object> d,bool horizontal) {
        double visual=Number(Get(d,horizontal?"width":"height"));
        if(visual<0)throw new IOException("Negative Chroma device extent.");
        Dictionary<string,object> config=Object(Get(d,"config"));
        double count=Number(Get(config,horizontal?"MatrixMaxCol":"MatrixMaxRow"));
        if(count<1 || count>4096 || count!=Math.Floor(count))throw new IOException("Unsupported Chroma matrix dimensions.");
        return Math.Max(visual,count*Cell);
    }
    static void Validate(Dictionary<string,object> d) {
        Number(Get(d,"x"));Number(Get(d,"y"));Extent(d,true);Extent(d,false);
        if(Number(Get(d,"orientation"))!=0)throw new IOException("Rotated Chroma layouts are not supported; leave the original unchanged.");
        Dictionary<string,object> config=Object(Get(d,"config"));
        foreach(string name in new string[]{"LedWidth","LedHeight"}) {
            double size=Number(Get(config,name));
            if(size!=0 && size!=Cell)throw new IOException("Unsupported Chroma cell size; leave the original unchanged.");
        }
        double rows=Number(Get(config,"MatrixMaxRow")),cols=Number(Get(config,"MatrixMaxCol"));
        foreach(object item in Array(Get(d,"leds"))) {
            Dictionary<string,object> led=Object(item);double row=Number(Get(led,"row")),col=Number(Get(led,"col"));
            if(row<0 || col<0 || row>=rows || col>=cols || row!=Math.Floor(row) || col!=Math.Floor(col))throw new IOException("Chroma LED is outside its device matrix.");
        }
    }
    static void NewLayerIds(object value) {
        foreach(object item in Array(value)) {
            Dictionary<string,object> layer=Object(item);
            if(layer.ContainsKey("id"))layer["id"]=Guid.NewGuid().ToString("N").Substring(0,12);
            object children;if(layer.TryGetValue("children",out children))NewLayerIds(children);
        }
    }
    static bool Overlap(Dictionary<string,object> a,Dictionary<string,object> b) {
        double ax=Number(Get(a,"x")),ay=Number(Get(a,"y")),bx=Number(Get(b,"x")),by=Number(Get(b,"y"));
        return ax<bx+Extent(b,true) && bx<ax+Extent(a,true) && ay<by+Extent(b,false) && by<ay+Extent(a,false);
    }
    public static LayoutFixResult Repair(string json) {
        if(json==null || json.Length>MaxLength)throw new IOException("Chroma export exceeds the supported size.");
        JavaScriptSerializer serializer=new JavaScriptSerializer{MaxJsonLength=MaxLength,RecursionLimit=128};
        Dictionary<string,object> document=Object(serializer.DeserializeObject(json.TrimStart('\uFEFF')));
        if(Number(Get(document,"version"))!=1)throw new IOException("Only the observed Chroma version-1 JSON export is supported.");
        LayoutFixResult result=new LayoutFixResult();
        foreach(object value in Array(Get(document,"profiles"))) {
            Dictionary<string,object> profile=Object(value);object[] values=Array(Get(profile,"devices"));
            List<Dictionary<string,object>> fixedDevices=new List<Dictionary<string,object>>(),mice=new List<Dictionary<string,object>>();bool found=false;
            foreach(object item in values){Dictionary<string,object> d=Object(item);found|=Split65(d);if(Mouse(d))mice.Add(d);else fixedDevices.Add(d);}
            if(!found || mice.Count==0)continue;
            foreach(object item in values)Validate(Object(item));
            double cursor=Double.MinValue;
            foreach(Dictionary<string,object> d in fixedDevices)cursor=Math.Max(cursor,Number(Get(d,"x"))+Extent(d,true));
            cursor+=Gap;
            foreach(Dictionary<string,object> mouse in mice){mouse["x"]=Math.Ceiling(cursor/Cell)*Cell;cursor=Number(mouse["x"])+Extent(mouse,true)+Gap;}
            // Check full matrix extents, not only today's selected keys. This
            // covers bracket keys as well as P, native and WDL mouse instances.
            foreach(Dictionary<string,object> mouse in mice)
                foreach(object other in values)if(!System.Object.ReferenceEquals(mouse,other) && Overlap(mouse,Object(other)))throw new IOException("Chroma logical bounds still overlap; no repaired file was written.");
            profile["id"]=Guid.NewGuid().ToString("N").Substring(0,12);
            profile["title"]=Text(profile,"title")+" [Split65 separated]";
            NewLayerIds(Get(profile,"layers"));++result.Profiles;result.Mice+=mice.Count;
        }
        if(result.Profiles==0)throw new IOException("No supported Split65 and mouse layout was found. Export the current Chroma Studio setup first.");
        result.Json=serializer.Serialize(document);return result;
    }
    public static LayoutFixResult RepairFile(string source,string destination) {
        if(String.Equals(Path.GetFullPath(source),Path.GetFullPath(destination),StringComparison.OrdinalIgnoreCase))throw new IOException("Choose a new output file; the original Chroma export is preserved.");
        if(new FileInfo(source).Length>MaxLength)throw new IOException("Chroma export exceeds the supported size.");
        LayoutFixResult result=Repair(File.ReadAllText(source,Encoding.UTF8));
        using(FileStream output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write))using(StreamWriter writer=new StreamWriter(output,new UTF8Encoding(false)))writer.Write(result.Json);
        return result;
    }
}
}
