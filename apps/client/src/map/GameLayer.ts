import { Application,Container,Graphics,Sprite,Text,Texture,TilingSprite } from 'pixi.js';
import type { CityState,WorldSnapshot } from '@orbis/protocol';
import { project,type Point } from './projection.js';
import { outerEdges,rulerPosition } from './gameScene.js';

const BASIS=4096;
/** Server state rendered as cartoon symbols; never invents a completed village. */
export class GameLayer extends Container {
  private border=new Graphics();
  private ground:TilingSprite;
  private towns=new Map<string,{node:Container;sprite:Sprite;label:Text;active:boolean}>();
  private hero:Sprite;
  private village:Texture;
  private foundation:Texture;
  private textures:Texture[]=[];
  private territoryKey='';
  private snapshot:WorldSnapshot|null=null;

  constructor(private app:Application) {
    super();
    const ground=new Graphics().rect(0,0,64,64).fill({color:'#fff2d5',alpha:0});
    ground.moveTo(8,15).lineTo(10,11).lineTo(12,15).moveTo(43,45).lineTo(45,40).lineTo(47,44).stroke({color:'#6c8550',width:1,alpha:0.5});
    ground.ellipse(27,34,2,1).fill({color:'#9b946d',alpha:0.5});
    this.ground=new TilingSprite({texture:this.texture(ground),width:1,height:1});
    this.village=this.townTexture(true);this.foundation=this.townTexture(false);
    const hero=new Graphics();
    hero.ellipse(16,38,12,4).fill({color:'#423b2c',alpha:0.2});
    hero.poly([11,14,21,14,27,34,5,34]).fill('#526b8c').stroke({color:'#374653',width:1.5});
    hero.rect(11,30,4,8).rect(18,30,4,8).fill('#5c4939');
    hero.roundRect(10,15,13,17,4).fill('#63844d').stroke({color:'#374c35',width:1.2});
    hero.circle(16,11,7).fill('#e2b67c').stroke({color:'#604d35',width:1});
    hero.poly([9,5,10,0,14,3,17,-1,20,3,24,0,23,6]).fill('#e4b552').stroke({color:'#8c6b32',width:1});
    hero.circle(14,11,0.8).circle(19,11,0.8).fill('#4c3b2f');
    hero.moveTo(27,7).lineTo(27,35).stroke({color:'#745738',width:2});
    hero.poly([28,7,38,10,28,16]).fill('#c27849').stroke({color:'#745738',width:0.8});
    this.hero=new Sprite(this.texture(hero));this.hero.anchor.set(0.45,0.92);
    this.addChild(this.ground,this.border,this.hero);this.eventMode='none';
  }
  private texture(graphics:Graphics) {
    const texture=this.app.renderer.generateTexture({target:graphics,resolution:2});graphics.destroy();this.textures.push(texture);return texture;
  }
  private townTexture(active:boolean) {
    const g=new Graphics();
    g.ellipse(48,63,48,25).fill({color:active?'#c4b67d':'#d3c294',alpha:0.85});
    g.moveTo(10,72).quadraticCurveTo(45,50,80,72).stroke({color:'#ead7a1',width:9});
    for (const [x,y,s] of [[6,34,0.8],[36,13,1],[59,40,0.8]]) {
      if (active) {
        g.poly([x,y+15*s,x+25*s,y+12*s,x+25*s,y+34*s,x,y+38*s]).fill('#efe0ac').stroke({color:'#746044',width:1});
        g.poly([x+25*s,y+12*s,x+34*s,y+18*s,x+34*s,y+35*s,x+25*s,y+34*s]).fill('#c7ba8a').stroke({color:'#746044',width:1});
        g.poly([x-4*s,y+17*s,x+11*s,y-1*s,x+29*s,y+14*s]).fill('#7d9452').stroke({color:'#5c6b3b',width:1.3});
        g.poly([x+11*s,y-1*s,x+22*s,y+2*s,x+38*s,y+18*s,x+29*s,y+14*s]).fill('#9ca963').stroke({color:'#5c6b3b',width:1});
        g.rect(x+5*s,y+24*s,7*s,12*s).fill('#80623c');g.rect(x+17*s,y+22*s,5*s,5*s).fill('#687b72');
        g.moveTo(x+3*s,y+17*s).lineTo(x+24*s,y+14*s).stroke({color:'#53683c',width:0.7});
      } else {
        g.poly([x,y+31*s,x+25*s,y+27*s,x+34*s,y+32*s,x+10*s,y+37*s]).fill('#b69767').stroke({color:'#8e714d',width:1});
        g.moveTo(x+2*s,y+30*s).lineTo(x+2*s,y+14*s).lineTo(x+16*s,y+1*s).lineTo(x+30*s,y+15*s).lineTo(x+30*s,y+31*s)
          .moveTo(x+2*s,y+20*s).lineTo(x+30*s,y+22*s).stroke({color:'#9d7547',width:2});
      }
    }
    g.moveTo(50,44).lineTo(50,66).stroke({color:'#7c6040',width:1.5});
    g.poly([50,44,61,46,50,52]).fill('#b96742');
    return this.texture(g);
  }
  sync(world:WorldSnapshot|null) {
    this.snapshot=world;this.visible=world?.ruler!==undefined&&world?.ruler!==null;
    if (!world) return;
    const desired=new Set(world.cities.map(city=>city.id));
    for (const [id,town] of this.towns) if (!desired.has(id)) {town.node.destroy({children:true});this.towns.delete(id);}
    for (const city of world.cities) {
      let town=this.towns.get(city.id);
      if (!town) {
        const node=new Container();const sprite=new Sprite(city.active?this.village:this.foundation);sprite.anchor.set(0.5,0.7);
        const label=new Text({text:city.name,style:{fontFamily:'Nunito, sans-serif',fontSize:12,fontWeight:'800',fill:'#493d2c',stroke:{color:'#fff2d5',width:3}}});
        label.anchor.set(0.5,0);label.y=30;node.addChild(sprite,label);this.addChildAt(node,this.children.length-1);
        town={node,sprite,label,active:city.active};this.towns.set(city.id,town);
      }
      town.sprite.texture=city.active?this.village:this.foundation;town.active=city.active;
    }
    const key=world.territory.map(cell=>cell.h3+cell.cityId).join(',');
    if (key!==this.territoryKey) {
      this.border.clear();
      for (const [a,b] of outerEdges(world.territory)) {
        const p=project(a),q=project(b);this.border.moveTo(p.x*BASIS,p.y*BASIS).lineTo(q.x*BASIS,q.y*BASIS);
      }
      this.border.stroke({color:'#b67c31',pixelLine:true});this.territoryKey=key;
    }
  }
  frame(center:Point,size:number,width:number,height:number,nowMs:number) {
    const ruler=this.snapshot?.ruler;if (!ruler) return;
    const zoom=Math.log2(size/256);
    this.ground.visible=zoom>9;this.ground.alpha=Math.min(0.6,(zoom-9)/6);
    this.ground.width=width;this.ground.height=height;this.ground.tilePosition.set(width/2-center.x*size,height/2-center.y*size);
    this.border.position.set(width/2-center.x*size,height/2-center.y*size);this.border.scale.set(size/BASIS);
    const position=rulerPosition(ruler,nowMs),p=project(position);
    this.hero.position.set(width/2+(p.x-center.x)*size,height/2+(p.y-center.y)*size);
    this.hero.width=zoom>9?32:22;this.hero.scale.y=Math.abs(this.hero.scale.x)*(ruler.journey?1+Math.sin(nowMs/90)*0.035:1);
    for (const city of this.snapshot?.cities??[]) this.placeTown(city,center,size,width,height,zoom);
  }
  private placeTown(city:CityState,center:Point,size:number,width:number,height:number,zoom:number) {
    const town=this.towns.get(city.id);if (!town||!city.pos) return;
    const p=project(city.pos);const x=width/2+(p.x-center.x)*size,y=height/2+(p.y-center.y)*size;
    town.node.position.set(x,y);town.node.visible=x>-120&&x<width+120&&y>-120&&y<height+120;
    town.sprite.width=zoom>9?100:28;town.sprite.scale.y=town.sprite.scale.x;
    town.label.visible=zoom>9;
  }
  override destroy(options?:Parameters<Container['destroy']>[0]) {super.destroy(options);for (const texture of this.textures) texture.destroy(true);}
}
