using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orbis.Core
{
    public static class ClientText
    {
        [Serializable] sealed class Entry { public string key; public string pt; public string en; }
        [Serializable] sealed class Table { public Entry[] entries; }
        static Dictionary<string,Entry> entries;
        public static string Get(string locale,string key)
        {
            if(string.IsNullOrEmpty(key)) return "";
            if(entries==null)
            {
                entries=new Dictionary<string,Entry>();
                var asset=Resources.Load<TextAsset>("Orbis/ClientStrings");
                if(asset!=null) foreach(var entry in JsonUtility.FromJson<Table>(asset.text).entries) entries.Add(entry.key,entry);
            }
            if(entries.TryGetValue(key,out var translated)) return locale=="en"?translated.en:translated.pt;
            return key;
        }
        public static string Remaining(double dueAt,double now)
        {
            if(dueAt<=0) return "—";
            var seconds=Math.Max(0,Math.Ceiling((dueAt-now)/1000));
            return $"{(int)seconds/60:00}:{(int)seconds%60:00}";
        }
    }
}
