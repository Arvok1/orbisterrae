/** RLE is transport data only. Decoding is deliberately renderer-independent. */
export function decodeRle(encoded:Uint8Array,expected:number) {
  const result:number[]=[];
  for(let offset=0;offset<encoded.length;offset+=3) {
    const value=encoded[offset],count=(encoded[offset+1]??0)|((encoded[offset+2]??0)<<8);
    if(value===undefined||count<=0||result.length+count>expected)throw new Error('invalid_rle');
    result.push(...Array<number>(count).fill(value));
  }
  if(result.length!==expected)throw new Error('invalid_rle');
  return result;
}

export function citySurfacePixels(roads:Uint8Array,zones:Uint8Array,width:number,height:number):Uint8Array {
  const pixels=new Uint8Array(width*height*4);
  for(let index=0;index<width*height;index++) {
    const offset=index*4,road=roads[index]??0,zone=zones[index]??0;
    pixels[offset]=road?255:zone;
    pixels[offset+1]=zone;
    pixels[offset+2]=road?30:0;
    pixels[offset+3]=255;
  }
  return pixels;
}
