export const coordinateChannelId='1552235129827762276';
export const coordinateChannelNotice='Отправь координаты сюда → <#'+coordinateChannelId+'>.';
export function hasMapCoordinates(content){
 const text=String(content||'').replace(/,/g,'.');
 const x=text.match(/\bX\s*[:=]\s*(-?\d+(?:\.\d+)?)/i),y=text.match(/\bY\s*[:=]\s*(-?\d+(?:\.\d+)?)/i),z=text.match(/\bZ\s*[:=]\s*(-?\d+(?:\.\d+)?)/i);
 if(!x||!y)return false;
 const X=Number(x[1]),Y=Number(y[1]),Z=z?Number(z[1]):0,nx=(619646.8573-X)/1525015.8839,ny=(619659.7258-Y)/1524017.2528;
 return [X,Y,Z,nx,ny].every(Number.isFinite)&&nx>=0&&nx<=1&&ny>=0&&ny<=1;
}
export function createCoordinateChannelRouter({guildId,sendNotice,handleAllowed}){
 const seen=new Set();
 return async message=>{
  if(!guildId||message?.guild_id!==guildId||message.author?.bot||!/^\d{15,22}$/.test(String(message.author?.id||'')))return;
  if(message.channel_id===coordinateChannelId){await handleAllowed(message);return;}
  if(!hasMapCoordinates(message.content)||!/^\d{15,22}$/.test(String(message.id||''))||!/^\d{15,22}$/.test(String(message.channel_id||''))||seen.has(message.id))return;
  seen.add(message.id);if(seen.size>1000)seen.delete(seen.values().next().value);
  await sendNotice(message,coordinateChannelNotice);
 };
}
