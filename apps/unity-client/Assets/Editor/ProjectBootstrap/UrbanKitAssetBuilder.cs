using System.IO;
using Orbis.World;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
    public static class UrbanKitAssetBuilder
    {
        const string Root="Assets/Resources/Orbis/UrbanKit";
        [MenuItem("Orbis/Build/Urban Kit")]
        public static void Ensure()
        {
            Directory.CreateDirectory(Root);
            var materials=new Material[4];
            for(var index=0;index<4;index++)
            {
                var path=$"{Root}/palette-{index}.mat";
                materials[index]=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(materials[index]!=null) continue;
                materials[index]=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Orbis/WorldRelief.mat")) { enableInstancing=true };
                materials[index].SetColor("_BaseColor",UrbanKit.Palette[index]);
                AssetDatabase.CreateAsset(materials[index],path);
            }
            foreach(var role in UrbanKit.Roles)
                for(var variant=0;variant<3;variant++)
                {
                    var host=new GameObject($"{role}-{variant}");
                    var lods=new LOD[3];
                    for(var lod=0;lod<3;lod++)
                    {
                        var path=$"{Root}/{role}-{variant}-lod{lod}.asset";
                        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                        if(mesh==null) { mesh=UrbanKit.Build(role,variant,lod);AssetDatabase.CreateAsset(mesh,path); }
                        var child=new GameObject("LOD"+lod);
                        child.transform.SetParent(host.transform,false);
                        child.AddComponent<MeshFilter>().sharedMesh=mesh;
                        var renderer=child.AddComponent<MeshRenderer>();renderer.sharedMaterials=materials;
                        lods[lod]=new LOD(lod==0?.18f:lod==1?.035f:.002f,new Renderer[]{renderer});
                    }
                    host.AddComponent<LODGroup>().SetLODs(lods);
                    PrefabUtility.SaveAsPrefabAsset(host,$"{Root}/{role}-{variant}.prefab");
                    Object.DestroyImmediate(host);
                }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
