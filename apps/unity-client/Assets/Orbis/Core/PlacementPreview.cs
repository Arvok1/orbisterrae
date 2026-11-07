using System;
using System.Linq;
using Orbis.V1;

namespace Orbis.Core
{
    public sealed class PlacementPreview
    {
        public string CityId { get; }
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public bool Valid { get; }
        public string Reason { get; }
        PlacementPreview(string cityId, int x, int y, int width, int height, bool valid, string reason)
        { CityId=cityId;X=x;Y=y;Width=width;Height=height;Valid=valid;Reason=reason; }
        public static PlacementPreview Evaluate(ClientViewState state, int x, int y)
        {
            var city=state.City;
            var definition=city?.Definitions.FirstOrDefault(candidate=>candidate.Id==state.Tool.Value);
            var size=definition==null?new UnityEngine.Vector2Int(1,1):CityPresentation.Footprint(definition,state.Tool.Orientation);
            var reason="server_confirmation";
            var valid=CityPresentation.CanEdit(state);
            if(!valid) reason="read_only";
            if(city==null||x<0||y<0||x+size.x>city.Width||y+size.y>city.Height) { valid=false;reason="outside_grid"; }
            if(state.Tool.Kind=="building"&&definition==null) { valid=false;reason="unknown_building"; }
            if(valid&&state.Tool.Kind=="building")
                foreach(var building in city.Buildings)
                {
                    var occupied=city.Definitions.FirstOrDefault(candidate=>candidate.Id==building.BuildingId);
                    if(occupied==null) continue;
                    var footprint=CityPresentation.Footprint(occupied,building.Orientation);
                    if(x<building.X+footprint.x&&x+size.x>building.X&&y<building.Y+footprint.y&&y+size.y>building.Y)
                    { valid=false;reason="occupied";break; }
                }
            return new PlacementPreview(city?.CityId??"",x,y,size.x,size.y,valid,reason);
        }
    }
}
