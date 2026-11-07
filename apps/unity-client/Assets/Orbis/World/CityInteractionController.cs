using System;
using Orbis.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Orbis.World
{
    public sealed class CityInteractionController : MonoBehaviour
    {
        GameStore store;
        GlobeRenderer globe;
        Camera worldCamera;
        WorldCartography cartography;
        OrbisInputActions input;
        Action<uint,uint> edit;
        Action<string> selectCity;
        GameObject preview;
        Renderer previewRenderer;
        Material previewMaterial;
        readonly MaterialPropertyBlock properties=new();
        Vector2 pressedAt;
        bool wasPressed;
        bool blocked;
        bool dragged;
        public void Initialize(GameStore state,GlobeRenderer terrain,WorldCartography map,Action<uint,uint> editCell,Action<string> select)
        {
            store=state;globe=terrain;cartography=map;edit=editCell;selectCity=select;worldCamera=Camera.main;
            input=new OrbisInputActions();input.Enable();
            preview=GameObject.CreatePrimitive(PrimitiveType.Cube);preview.name="Placement preview";
            preview.transform.SetParent(transform,false);Destroy(preview.GetComponent<Collider>());
            previewRenderer=preview.GetComponent<Renderer>();
            previewMaterial=new Material(Resources.Load<Material>("Orbis/WorldRelief"));
            previewRenderer.sharedMaterial=previewMaterial;
            preview.SetActive(false);
        }
        void Update()
        {
            if(store==null||worldCamera==null) return;
            if(!HudInputGate.IsKeyboardBlocked&&input.Cancel.WasPressedThisFrame()) store.Update(store.State.With(tool:new CityTool(),clearPreview:true));
            var point=input.Point.ReadValue<Vector2>();
            var pressed=input.PrimaryDrag.IsPressed();
            if(!pressed&&!wasPressed)dragged=false;
            if(pressed&&!wasPressed) { pressedAt=point;blocked=HudInputGate.IsPointerBlocked;dragged=false; }
            if(pressed&&(point-pressedAt).sqrMagnitude>=16) dragged=true;
            var state=store.State;
            var city=state.City;
            var location=CityPresentation.FindCity(state,state.SelectedCityId);
            PlacementPreview candidate=null;
            if(!HudInputGate.IsPointerBlocked&&city!=null&&location?.Pos!=null&&state.AltitudeMeters<30000&&state.Tool.Kind!="select")
            {
                var origin=new GeoCoordinate(location.Pos.Lat,location.Pos.Lon);
                var frame=new CityFrame(origin,globe.SampleHeightMeters(origin));
                if(frame.Pick(worldCamera.ScreenPointToRay(point),city.Width,city.Height,out var cell))
                {
                    candidate=PlacementPreview.Evaluate(state,cell.x,cell.y);
                    var center=frame.CellCenter(city.Width,city.Height,cell.x+candidate.Width/2f,cell.y+candidate.Height/2f);
                    preview.transform.SetPositionAndRotation(frame.LocalToWorld.MultiplyPoint3x4(center+Vector3.up*.3f),frame.LocalToWorld.rotation);
                    preview.transform.localScale=new Vector3(candidate.Width*.01f,.0004f,candidate.Height*.01f);
                    properties.SetColor("_BaseColor",candidate.Valid?new Color(.35f,.75f,.3f):new Color(.8f,.15f,.1f));
                    previewRenderer.SetPropertyBlock(properties);
                }
            }
            preview.SetActive(candidate!=null&&!dragged);
            var previous=state.Preview;
            if(candidate?.X!=previous?.X||candidate?.Y!=previous?.Y||candidate?.Valid!=previous?.Valid||candidate?.Width!=previous?.Width
                ||candidate?.Height!=previous?.Height||candidate?.Reason!=previous?.Reason)
                store.Update(store.State.With(preview:candidate,clearPreview:candidate==null));
            if(!pressed&&wasPressed&&!dragged&&!blocked&&!HudInputGate.IsPointerBlocked)
            {
                if(candidate!=null&&candidate.Valid) edit((uint)candidate.X,(uint)candidate.Y);
                else if(state.Tool.Kind=="select")
                {
                    var selected=cartography.Pick(point);
                    if(selected!=null)
                    {
                        selectCity(selected);
                        var selectedState=CityPresentation.FindCity(store.State,selected);
                        if(selectedState?.Pos!=null) store.Focus(new GeoCoordinate(selectedState.Pos.Lat,selectedState.Pos.Lon),8000);
                    }
                    else if(globe.SurfaceCollider.Raycast(worldCamera.ScreenPointToRay(point),out var hit,120000))
                        store.Update(store.State.With(selection:GeoPresentation.FromNormal(hit.point.normalized)));
                }
            }
            wasPressed=pressed;
        }
        void OnDestroy() { input?.Dispose();if(previewMaterial!=null)Destroy(previewMaterial); }
    }
}
