import {test} from 'node:test';
import assert from 'node:assert/strict';
import {DatabaseSync} from 'node:sqlite';
import {createD1Backend} from '../src/d1-backend.js';

function setup(){
 const db=new DatabaseSync(':memory:');
 db.exec(`CREATE TABLE squad_notification_events(id NUMERIC PRIMARY KEY,squad_id TEXT,category TEXT,actor_id TEXT,title TEXT,body TEXT,source_key TEXT,created_at TEXT);
 CREATE TABLE members(id TEXT PRIMARY KEY,squad_id TEXT,is_active INTEGER);
 CREATE TABLE discord_member_links(discord_user_id TEXT PRIMARY KEY,member_id TEXT);
 CREATE TABLE discord_dm_preferences(member_id TEXT PRIMARY KEY,enabled INTEGER,categories TEXT);
 CREATE TABLE discord_dm_deliveries(event_id NUMERIC,discord_user_id TEXT,status TEXT,attempts INTEGER,updated_at TEXT,PRIMARY KEY(event_id,discord_user_id));`);
 const calls=[];
 const api=createD1Backend({CF_ACCOUNT_ID:'a',CF_D1_DATABASE_ID:'d',CF_D1_API_TOKEN:'test'},async(_,o)=>{
  const q=JSON.parse(o.body);calls.push(q);const rows=db.prepare(q.sql).all(...q.params);
  return {ok:true,status:200,json:async()=>({success:true,result:[{results:rows}]})};
 });
 function member(id,squad='s',enabled=1,active=1,categories={task:true,stash:true,protect:true}){
  db.prepare('INSERT INTO members VALUES(?,?,?)').run(id,squad,active);
  db.prepare('INSERT INTO discord_member_links VALUES(?,?)').run('d-'+id,id);
  db.prepare('INSERT INTO discord_dm_preferences VALUES(?,?,?)').run(id,enabled,JSON.stringify(categories));
 }
 function event(id,category='task',actor=null,squad='s',title='Task',source='event-'+id){db.prepare('INSERT INTO squad_notification_events VALUES(?,?,?,?,?,?,?,?)').run(id,squad,category,actor,title,'body',source,'2026-11-01T00:00:00.000Z')}
 const pending=()=>api.pendingNotifications('2026-10-31T00:00:00.000Z');
 return {db,api,calls,member,event,pending};
}
test('only pending recipients; successful delivery stays excluded after restart',async()=>{
 const {db,api,calls,member,event,pending}=setup();member('a');member('b');event(1);
 assert.equal((await pending()).length,2);assert.equal(calls.length,1);
 assert.equal(await api.rpc('claim_discord_dm_delivery',{p_event_id:1,p_discord_user_id:'d-a'}),true);
 db.exec("UPDATE discord_dm_deliveries SET status='sent' WHERE discord_user_id='d-a'");
 assert.deepEqual((await pending()).map(j=>j.discord_user_id),['d-b']);
 assert.equal(await api.rpc('claim_discord_dm_delivery',{p_event_id:1,p_discord_user_id:'d-b'}),true);
 assert.equal(await api.rpc('claim_discord_dm_delivery',{p_event_id:1,p_discord_user_id:'d-b'}),false);
 assert.equal((await pending()).length,0);
});
test('failed deliveries retry twice, then stop; in-flight and sent never selected',async()=>{
 const {db,api,member,event,pending}=setup();member('a');for(let i=1;i<=4;i++)event(i);
 for(const [id,status,attempts] of [[1,'failed',1],[2,'failed',3],[3,'sending',1],[4,'sent',1]])db.prepare('INSERT INTO discord_dm_deliveries VALUES(?,?,?,?,?)').run(id,'d-a',status,attempts,'now');
 assert.deepEqual((await pending()).map(j=>j.id),[1]);
 for(let i=0;i<2;i++){
  assert.equal(await api.rpc('claim_discord_dm_delivery',{p_event_id:1,p_discord_user_id:'d-a'}),true);
  db.exec("UPDATE discord_dm_deliveries SET status='failed' WHERE event_id=1");
 }
 assert.equal((await pending()).length,0);
});
test('preserves squad, active membership, actor, enabled and boolean category preferences',async()=>{
 const {member,event,pending}=setup();
 member('ok');member('actor');member('other','other');member('disabled','s',0);member('inactive','s',1,0);member('numeric','s',1,1,{task:1});member('empty','s',1,1,{});
 event(1,'task','actor');
 assert.deepEqual((await pending()).map(j=>j.discord_user_id),['d-ok']);
});
test('publication announcements and alarms excluded; stash expiry and base preference fallback retained',async()=>{
 const {member,event,pending}=setup();member('a');
 event(1,'base',null,'s','Новая база');event(2,'stash',null,'s','Any','squad-place:1');event(3,'alarm');
 event(4,'stash',null,'s','СХРОН ПРОПАЛ');event(5,'base',null,'s','Base change');event(6,'stash',null,'s',null);
 assert.deepEqual((await pending()).map(j=>j.id),[6,5,4]);
});
test('malformed preferences do not break polling; launch cutoff and 60-event window preserved',async()=>{
 const {db,member,event,pending}=setup();member('a');member('bad');
 db.exec("UPDATE discord_dm_preferences SET categories='bad json' WHERE member_id='bad'");
 for(let i=1;i<=65;i++)event(i);
 const jobs=await pending();assert.equal(jobs.length,60);assert.equal(jobs[0].id,65);assert.equal(jobs.at(-1).id,6);
 db.exec("UPDATE squad_notification_events SET created_at='2020-01-01'");assert.equal((await pending()).length,0);
});
