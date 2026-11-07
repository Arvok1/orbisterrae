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
        MaterialPropertyBlock properties;
        GameObject selectionMarker;
        Renderer selectionMarkerRenderer;
        MaterialPropertyBlock markerProperties;
        GeoCoordinate? renderedSelection;
        Vector2 pressedAt;
        bool wasPressed;
        bool blocked;
        bool dragged;
        public void Initialize(GameStore state,GlobeRenderer terrain,WorldCartography map,Action<uint,uint> editCell,Action<string> select)
        {
            store=state;globe=terrain;cartography=map;edit=editCell;selectCity=select;worldCamera=Camera.main;
            properties=new MaterialPropertyBlock();
            input=new OrbisInputActions();input.Enable();
            preview=GameObject.CreatePrimitive(PrimitiveType.Cube);preview.name="Placement preview";
            preview.transform.SetParent(transform,false);Destroy(preview.GetComponent<Collider>());
            previewRenderer=preview.GetComponent<Renderer>();
            previewMaterial=new Material(Resources.Load<Material>("Orbis/WorldRelief"));
            previewRenderer.sharedMaterial=previewMaterial;
            preview.SetActive(false);
            markerProperties=new MaterialPropertyBlock();
            selectionMarker=GameObject.CreatePrimitive(PrimitiveType.Sphere);selectionMarker.name="Selection marker";
            selectionMarker.transform.SetParent(transform,false);Destroy(selectionMarker.GetComponent<Collider>());
            selectionMarkerRenderer=selectionMarker.GetComponent<Renderer>();
            selectionMarkerRenderer.sharedMaterial=previewMaterial;
            selectionMarker.SetActive(false);
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
            RefreshSelectionMarker();
        }
        /// <summary>The globe selection deserves a pin so admins see what a click picked.</summary>
        void RefreshSelectionMarker()
        {
            if(selectionMarker==null||worldCamera==null) return;
            var selection=store.State.Selection;
            if(selection==null)
            {
                if(selectionMarker.activeSelf){selectionMarker.SetActive(false);renderedSelection=null;}
                return;
            }
            var value=selection.Value;
            var normal=GeoMath.SurfaceNormal(value);
            var radius=(float)GeoMath.EarthRadiusUnity;
            var height=globe.SampleHeightMeters(value);
            var surface=normal*(radius+(float)(height/GeoMath.MetersPerUnityUnit));
            var distance=(worldCamera.transform.position-surface).magnitude;
            var size=Mathf.Clamp(distance*.02f,radius*.0025f,radius*.05f);
            selectionMarker.transform.position=surface+normal*size*.9f;
            selectionMarker.transform.localScale=Vector3.one*size;
            markerProperties.SetColor("_BaseColor",new Color(.95f,.78f,.25f));
            selectionMarkerRenderer.SetPropertyBlock(markerProperties);
            var moved=!renderedSelection.HasValue
                ||Math.Abs(renderedSelection.Value.LatitudeDegrees-value.LatitudeDegrees)>1e-9
                ||Math.Abs(renderedSelection.Value.LongitudeDegrees-value.LongitudeDegrees)>1e-9;
            if(moved&&!selectionMarker.activeSelf)selectionMarker.SetActive(true);
            renderedSelection=value;
        }
        void OnDestroy() { input?.Dispose();if(previewMaterial!=null)Destroy(previewMaterial); }
    }
}
