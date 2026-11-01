import {test} from 'node:test';
import assert from 'node:assert/strict';
import {DatabaseSync} from 'node:sqlite';
import {readFileSync} from 'node:fs';
import vm from 'node:vm';
import {createD1Backend} from '../src/d1-backend.js';
const env={CF_ACCOUNT_ID:'a',CF_D1_DATABASE_ID:'d',CF_D1_API_TOKEN:'test'};
function setup(){
 const db=new DatabaseSync(':memory:');db.exec(`CREATE TABLE members(id TEXT PRIMARY KEY,squad_id TEXT,nick TEXT,is_active INTEGER);CREATE TABLE discord_member_links(discord_user_id TEXT PRIMARY KEY,member_id TEXT);CREATE TABLE protect_updates(id INTEGER PRIMARY KEY,squad_id TEXT,updated_at TEXT,effective_at TEXT);`);
 const requests=[];let failMetadata=false;
 const fetcher=async(_,options)=>{const q=JSON.parse(options.body);requests.push(q);if(failMetadata&&q.sql.startsWith('PRAGMA')){failMetadata=false;return {ok:false,status:503,json:async()=>({success:false})}}const rows=db.prepare(q.sql).all(...q.params);return {ok:true,status:200,json:async()=>({success:true,result:[{results:rows}]})}};
 return {db,requests,api:createD1Backend(env,fetcher),restart:()=>createD1Backend(env,fetcher),failMetadata:()=>{failMetadata=true}};
}
test('schema metadata is shared across concurrent writes, but records stay fresh and restart reloads schema',async()=>{
 const {api,requests,restart}=setup();await Promise.all(['a','b'].map(id=>api.db('members',{method:'POST',body:{id,squad_id:'s',nick:id,is_active:true}})));
 assert.equal(requests.filter(q=>q.sql.startsWith('PRAGMA')).length,1);
 await api.db('members',{method:'PATCH',query:'?id=eq.a',body:{nick:'Changed'}});assert.equal((await api.db('members',{query:'?id=eq.a'}))[0].nick,'Changed');
 await restart().db('members',{method:'POST',body:{id:'c',squad_id:'s',nick:'C',is_active:true}});assert.equal(requests.filter(q=>q.sql.startsWith('PRAGMA')).length,2);
});
test('failed metadata fetch is not cached and next insert retries successfully',async()=>{
 const {api,requests,failMetadata}=setup();failMetadata();await assert.rejects(api.db('members',{method:'POST',body:{id:'a'}}),/D1 request failed/);await api.db('members',{method:'POST',body:{id:'a',is_active:true}});assert.equal(requests.filter(q=>q.sql.startsWith('PRAGMA')).length,2);
});
async function legacyActivity(api){const links=await api.db('discord_member_links',{query:'?select=member_id&limit=1'});if(!links.length)return null;const members=await api.db('members',{query:'?id=eq.'+links[0].member_id+'&select=squad_id&limit=1'});if(!members[0]?.squad_id)return null;return (await api.db('protect_updates',{query:'?squad_id=eq.'+members[0].squad_id+'&select=updated_at,effective_at&order=updated_at.desc&limit=1'}))[0]||null;}
test('single activity read matches old first-link logic for multiple squads, missing and inactive members',async()=>{
 const {api,db,requests}=setup();db.exec("INSERT INTO members VALUES('a','s1','A',1),('b','s2','B',1); INSERT INTO discord_member_links VALUES('u2','b'),('u1','a'); CREATE INDEX links_member ON discord_member_links(member_id); INSERT INTO protect_updates VALUES(1,'s1','2026-01-01',NULL),(2,'s2','2026-11-01','2026-11-02'),(3,'s1','2026-02-01','2026-02-02')");
 for(const change of ['',"UPDATE members SET is_active=0 WHERE id='a'","DELETE FROM members WHERE id='a'","DELETE FROM discord_member_links"]){if(change)db.exec(change);const expected=await legacyActivity(api);requests.length=0;assert.deepEqual(await api.protectActivity(),expected);assert.equal(requests.length,1);}
});
test('activity reads current changes every time; does not cache timer records',async()=>{
 const {api,db}=setup();db.exec("INSERT INTO members VALUES('a','s','A',1); INSERT INTO discord_member_links VALUES('u','a'); INSERT INTO protect_updates VALUES(1,'s','2026-01-01',NULL)");assert.equal((await api.protectActivity()).effective_at,null);db.exec("UPDATE protect_updates SET effective_at='2026-11-01'");assert.equal((await api.protectActivity()).effective_at,'2026-11-01');
});
test('bot activity labels retain startup, active and expired boundaries',async()=>{
 const source=readFileSync(new URL('../src/server.js',import.meta.url),'utf8');const from=source.indexOf('async function protectActivityText()'),to=source.indexOf('// Informational voice-channel',from);let now=Date.parse('2026-11-01T00:00:00Z');const base=now;
 class Clock extends Date{static now(){return now}}
 const c={Date:Clock,d1Backend:{ready:()=>true,protectActivity:async()=>({updated_at:new Date(base).toISOString(),effective_at:null})},db:()=>{throw Error('Unexpected fallback read')}};vm.createContext(c);vm.runInContext(source.slice(from,to),c);
 assert.match(await c.protectActivityText(),/^🟡 ЗАПУСК/);now=base+3600000;assert.match(await c.protectActivityText(),/^🟢 АКТИВЕН/);now=base+32400000;assert.match(await c.protectActivityText(),/^🔴 СПАЛ/);
});
