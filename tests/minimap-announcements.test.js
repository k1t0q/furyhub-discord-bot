import {test} from 'node:test';
import assert from 'node:assert/strict';
import {DatabaseSync} from 'node:sqlite';
import {readFileSync} from 'node:fs';
import {announcementPosition,announcementPayload,createMinimapAnnouncements} from '../src/minimap-announcements.js';
const squad='b6305fbe-04a7-451f-920e-d347e3faf953',channel='1552235129827762276';
function setup(){
 const db=new DatabaseSync(':memory:');
 db.exec(`CREATE TABLE members(id TEXT PRIMARY KEY,squad_id TEXT,is_active INTEGER,nick TEXT);
 CREATE TABLE squad_place_posts(id NUMERIC PRIMARY KEY,squad_id TEXT,kind TEXT,comment TEXT,map_marker_name TEXT,created_by TEXT,created_at TEXT);
 CREATE TABLE squad_map_markers(id TEXT PRIMARY KEY,squad_id TEXT,type TEXT,name TEXT,x REAL,y REAL,created_by TEXT,updated_at TEXT);`);
 db.exec(readFileSync(new URL('../migrations/20261102_minimap_announcements.sql',import.meta.url),'utf8'));
 db.prepare('INSERT INTO members VALUES(?,?,?,?)').run('member',squad,1,'Игрок');
 let now=Date.parse('2026-11-02T12:00:00Z');const sent=[],lookups=[],renders=[],logs=[];
 const env={DISCORD_BOT_TOKEN:'test-only',DISCORD_GUILD_ID:'guild',DISCORD_TEST_MODE:'false'};
 const options={sql:async(query,params=[])=>db.prepare(query).all(...params),env,clock:()=>now,logger:{log:(...a)=>logs.push(a),error:(...a)=>logs.push(a)},render:async(x,y)=>{renders.push({x,y});return Buffer.from('png')},fetcher:async(url,o)=>{
  lookups.push(url);if(!o.method)return {ok:true,json:async()=>url.includes('/messages?')?sent.map((s,i)=>({id:String(1552235129827762300n+BigInt(i)),embeds:s.embeds})):{guild_id:env.DISCORD_GUILD_ID,type:0}};
  const payload=JSON.parse(o.body.get('payload_json'));sent.push(payload);assert.ok(o.body.get('files[0]'));return {ok:true,json:async()=>({id:String(1552235129827762300n+BigInt(sent.length-1))})};}};
 const addPost=(id,kind='stash',owner='member',s=squad,source=true)=>db.prepare('INSERT INTO squad_place_posts VALUES(?,?,?,?,?,?,?)').run(id,s,kind,'Название\nКоординаты: X=12345.125 Y=-54321.5\n'+(source?'Источник: Миникарта SCUM HUB':''),'Название',owner,new Date(now).toISOString());
 const addCar=(id='minimap-vehicle',s=squad)=>db.prepare('INSERT INTO squad_map_markers VALUES(?,?,?,?,?,?,?,?)').run(id,s,'vehicle','Машина',.4,.6,'member',new Date(now).toISOString());
 return {db,options,sent,lookups,renders,addPost,addCar,advance:ms=>{now+=ms}};
}
test('all four mini-map buttons enqueue and publish one picture with coordinates, safely, without duplicate marker writes',async()=>{
 const s=setup();['stash','base','other'].forEach((k,i)=>s.addPost(i+1,k));s.addCar();
 const publisher=createMinimapAnnouncements(s.options);await publisher.poll();await publisher.poll();
 assert.equal(s.sent.length,4);assert.equal(s.renders.length,4);assert.equal(s.db.prepare("SELECT count(*) AS n FROM minimap_discord_outbox WHERE status='sent'").get().n,4);
 assert.equal(s.db.prepare('SELECT count(*) AS n FROM squad_place_posts').get().n,3);assert.equal(s.db.prepare('SELECT count(*) AS n FROM squad_map_markers').get().n,1);
 for(const message of s.sent){assert.equal(message.allowed_mentions.parse.length,0);assert.equal(message.enforce_nonce,true);assert.ok(message.nonce.length<=25);assert.match(message.content,/Автор: Игрок/);assert.match(message.content,/X=.* Y=.* Z=0/);assert.equal(message.embeds[0].image.url,'attachment://scum-marker-preview.png');}
 assert.equal(s.lookups.filter(u=>u.endsWith('/channels/'+channel)).length,1);
});
test('triggers ignore edits, imports, other squads, inactive actors and ordinary vehicles',()=>{
 const s=setup();s.addPost(1);s.db.prepare("UPDATE squad_place_posts SET map_marker_name='Новое название' WHERE id=1").run();s.addPost(2,'stash','member',squad,false);s.addPost(3,'stash','member','other-squad');s.addCar('ordinary-vehicle');
 s.db.prepare('UPDATE members SET is_active=0').run();s.addPost(4);assert.equal(s.db.prepare('SELECT count(*) AS n FROM minimap_discord_outbox').get().n,1);
 s.db.exec('BEGIN');s.db.prepare('UPDATE members SET is_active=1').run();s.addPost(5);s.db.exec('ROLLBACK');assert.equal(s.db.prepare('SELECT count(*) AS n FROM minimap_discord_outbox').get().n,1);
});
test('failed upload retries durable job after backoff, restart recovers pending work',async()=>{
 const s=setup();s.addPost(1);let failures=1;const fetcher=s.options.fetcher;
 s.options.fetcher=async(...args)=>{if(args[1].method&&failures-->0)return {ok:false,status:503};return fetcher(...args)};
 await createMinimapAnnouncements(s.options).poll();assert.equal(s.db.prepare('SELECT status FROM minimap_discord_outbox').get().status,'failed');
 await createMinimapAnnouncements(s.options).poll();assert.equal(s.sent.length,0);s.advance(61000);await createMinimapAnnouncements(s.options).poll();assert.equal(s.sent.length,1);
});
test('retry reconciles an already accepted message instead of posting the picture twice',async()=>{
 const s=setup();s.addPost(1);const sql=s.options.sql;let failed=false;
 s.options.sql=async(q,p)=>{if(!failed&&q.includes("SET status='sent'")){failed=true;throw Error('Lost database acknowledgement')}return sql(q,p)};
 await createMinimapAnnouncements(s.options).poll();assert.equal(s.sent.length,1);s.advance(61000);await createMinimapAnnouncements(s.options).poll();assert.equal(s.sent.length,1);assert.equal(s.db.prepare('SELECT status FROM minimap_discord_outbox').get().status,'sent');
});
test('wrong Discord server, disabled route and revoked membership cannot publish coordinates',async()=>{
 const s=setup();s.addPost(1);s.options.fetcher=async()=>({ok:true,json:async()=>({guild_id:'wrong',type:0})});await createMinimapAnnouncements(s.options).poll();assert.equal(s.sent.length,0);assert.equal(s.renders.length,0);
 const t=setup();t.addPost(1);t.db.prepare('UPDATE members SET is_active=0').run();await createMinimapAnnouncements(t.options).poll();assert.equal(t.sent.length,0);
 const u=setup();u.addPost(1);u.db.prepare('UPDATE minimap_discord_routes SET enabled=0').run();await createMinimapAnnouncements(u.options).poll();assert.equal(u.sent.length,0);
});
test('coordinate conversion matches mini-map map bounds and hostile names cannot ping or inject markdown',()=>{
 assert.deepEqual(announcementPosition({kind:'stash',raw_coords:'X=12345.125 Y=-54321.5'}),{x:12345.125,y:-54321.5});
 assert.deepEqual(announcementPosition({kind:'car',x:0,y:0}),{x:619646.8573,y:619659.7258});
 assert.throws(()=>announcementPosition({kind:'car',x:2,y:0}));assert.throws(()=>announcementPosition({kind:'base',raw_coords:'missing'}));
 const msg=announcementPayload({id:'post:1',kind:'other',name:'<@everyone> **name**',actor_nick:'<@123>'},{x:1,y:2});assert.ok(!msg.content.includes('<'));assert.deepEqual(msg.allowed_mentions,{parse:[]});
});
