import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import vm from 'node:vm';
const source=readFileSync(new URL('../src/server.js',import.meta.url),'utf8');
function setup(jobs){
 const claims=[],sent=[],writes=[];
 const c={console:{log(){},warn(){},error(){}},process:{env:{DISCORD_BOT_TOKEN:'test',DISCORD_DM_NOTIFICATIONS:'true',DISCORD_TEST_MODE:'false',DISCORD_DM_START_AT:'2026-10-30'}},apiReady:()=>true,d1Backend:{ready:()=>true,pendingNotifications:async()=>jobs},queueMigrationReminders:async()=>{},queueExpiredProtectNotifications:async()=>0,db:async(t,o)=>{writes.push({t,o});return []},rpc:async(_,p)=>{claims.push(p);return true},sendDiscordDM:async id=>sent.push(id)};
 vm.createContext(c);vm.runInContext('let discordDmPolling=false;'+source.slice(source.indexOf('async function processDiscordNotifications()'),source.indexOf('async function sectionProtect(')),c);
 return {c,claims,sent,writes};
}
test('idle D1 poll does not reload recipients, claim deliveries, or send messages',async()=>{
 const {c,claims,sent,writes}=setup([]);await c.processDiscordNotifications();assert.equal(claims.length,0);assert.equal(sent.length,0);assert.equal(writes.length,0);
});
test('poll sends only pending event-recipient pairs, including failed delivery retry',async()=>{
 const jobs=[{id:2,squad_id:'s',category:'task',actor_id:null,discord_user_id:'d-b',recipient_member_id:'b',enabled:true,categories:{task:true}},{id:1,squad_id:'s',category:'task',actor_id:null,discord_user_id:'d-a',recipient_member_id:'a',enabled:true,categories:{task:true}}];
 const {c,claims,sent,writes}=setup(jobs);await c.processDiscordNotifications();assert.deepEqual(sent,['d-a','d-b']);assert.equal(claims.length,2);assert.equal(writes.length,2);assert.ok(writes.every(w=>w.o.body.status==='sent'));
});
test('send failure keeps existing failed-status retry path',async()=>{
 const {c,writes}=setup([{id:1,squad_id:'s',category:'task',actor_id:null,discord_user_id:'d-a',recipient_member_id:'a',enabled:true,categories:{task:true}}]);c.sendDiscordDM=async()=>{throw Error('offline')};await c.processDiscordNotifications();assert.equal(writes[0].o.body.status,'failed');
});
