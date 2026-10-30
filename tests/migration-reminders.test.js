import {test} from 'node:test';
import assert from 'node:assert/strict';
import {queueMigrationReminders} from '../src/migration-reminders.js';
test('D1 reminders queue Moscow 15:00 presence, protect warning and stash expiry exactly once',async()=>{
 const now=new Date('2026-10-30T12:00:00Z'),events=[];
 const db=async(t,o={})=>{
 if(t==='squad_notification_events'){
 if(o.method==='POST'){events.push({id:events.length+1,...o.body});return [events.at(-1)]}
 const key=new URLSearchParams(o.query.slice(1)).get('source_key').slice(3);return events.filter(e=>e.source_key===key);
 }
 if(t==='members')return [{squad_id:'s'},{squad_id:'s'}];
 if(t==='protect_updates')return [{id:1,squad_id:'s',effective_at:new Date(+now-32400000+600000).toISOString()}];
 if(t==='squad_place_posts')return [{id:2,squad_id:'s',created_at:new Date(+now-12*86400000).toISOString(),map_marker_name:'Test stash'},{id:3,squad_id:'s',created_at:'2020-01-01T00:00:00Z'}];
 return [];
 };
 assert.equal(await queueMigrationReminders(db,now),3);
 assert.equal(await queueMigrationReminders(db,now),0);
 assert.deepEqual(events.map(e=>e.category),['presence','protect','stash']);
});
