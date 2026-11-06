import test from 'node:test';
import assert from 'node:assert/strict';
import {coordinateChannelId,coordinateChannelNotice,createCoordinateChannelRouter} from '../src/coordinate-channel.js';
const message={id:'1552235129827762200',author:{id:'1552235129827762201'},guild_id:'1552235129827762202',channel_id:coordinateChannelId,content:'X=100 Y=-200 Z=0'};
function fixture(){const imported=[],notices=[];return {imported,notices,route:createCoordinateChannelRouter({guildId:message.guild_id,handleAllowed:async m=>imported.push(m),sendNotice:async(m,text)=>notices.push({m,text})})};}
test('coordinates and screenshots reach the marker handler only in the designated channel',async()=>{
 const f=fixture();await f.route(message);await f.route({...message,content:'Схрон',attachments:[{url:'https://example.com/map.png'}]});assert.equal(f.imported.length,2);assert.equal(f.notices.length,0);
});
test('other channels receive one clickable redirect and never start adding a marker',async()=>{
 const f=fixture(),other={...message,channel_id:'1552235129827762299'};await f.route(other);await f.route(other);assert.equal(f.imported.length,0);assert.equal(f.notices.length,1);assert.equal(f.notices[0].text,coordinateChannelNotice);assert.ok(coordinateChannelNotice.includes('<#'+coordinateChannelId+'>'));
});
test('ordinary chat, bots, DMs, foreign guilds and invalid coordinates produce no redirect',async()=>{
 const f=fixture();for(const change of [{content:'привет'},{content:'X=999999999 Y=0'},{author:{...message.author,bot:true}},{guild_id:undefined},{guild_id:'other'}])await f.route({...message,channel_id:'1552235129827762299',...change});assert.equal(f.imported.length,0);assert.equal(f.notices.length,0);
});
