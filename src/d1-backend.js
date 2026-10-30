import {randomBytes,createHash,createHmac} from 'node:crypto';
const allowed=new Set(['members','presence_history','tasks','task_contributions','raids','raid_participants','explosives_inventory','protect_updates','protect_update_edits','squad_place_posts','squad_notification_events','squad_raid_alarms','discord_member_links','discord_link_codes','discord_dm_preferences','discord_dm_deliveries','discord_place_drafts','fury_alarm_channel_jobs','fury_alarm_dm_receipts','squad_map_markers']);
const ident=s=>{if(!/^[a-z_][a-z0-9_]*$/.test(s))throw Error('Invalid SQL identifier');return '"'+s+'"'};
const value=x=>typeof x==='boolean'?Number(x):x&&typeof x==='object'?JSON.stringify(x):x;
const decode=r=>Object.fromEntries(Object.entries(r).map(([k,v])=>{if(['images','target_images','categories','enemy_nicks'].includes(k)&&typeof v==='string'){try{v=JSON.parse(v)}catch{}}if(['enabled','is_active'].includes(k))v=!!v;return[k,v]}));
export function createD1Backend(env=process.env,fetcher=fetch){
 const endpoint='https://api.cloudflare.com/client/v4/accounts/'+env.CF_ACCOUNT_ID+'/d1/database/'+env.CF_D1_DATABASE_ID+'/query';
 const sign=src=>{if(typeof src!=='string'||!src.startsWith('https://ik.imagekit.io/')||!env.IMAGEKIT_PRIVATE_KEY)return src;const u=new URL(src);u.searchParams.delete('ik-t');u.searchParams.delete('ik-s');const expiry=Math.floor(Date.now()/1000)+7200;const sig=createHmac('sha1',env.IMAGEKIT_PRIVATE_KEY.trim()).update(u.pathname.split('/').slice(2).join('/')+(u.search||'')+expiry).digest('hex');u.searchParams.set('ik-t',String(expiry));u.searchParams.set('ik-s',sig);return u.href};
 const signRows=rows=>rows.map(r=>({...r,...(Array.isArray(r.images)?{images:r.images.map(sign)}:{}),...(Array.isArray(r.target_images)?{target_images:r.target_images.map(sign)}:{})}));
 const ready=()=>!!(env.CF_ACCOUNT_ID&&env.CF_D1_DATABASE_ID&&env.CF_D1_API_TOKEN);
 async function sql(query,params=[]){if(!ready())throw Error('D1 not configured');const r=await fetcher(endpoint,{method:'POST',headers:{Authorization:'Bearer '+env.CF_D1_API_TOKEN,'content-type':'application/json'},body:JSON.stringify({sql:query,params:params.map(value)}),signal:AbortSignal.timeout(15000)});const v=await r.json();if(!r.ok||!v.success)throw Error('D1 request failed '+r.status);return (v.result?.[0]?.results||[]).map(decode)}
 async function db(table,{method='GET',query='',body}={}){
 if(!allowed.has(table))throw Error('Table not allowed');const q=new URLSearchParams(query.replace(/^\?/,'')),args=[],clauses=[];
 for(const [k,v] of q){if(['select','limit','order','on_conflict'].includes(k))continue;const c=ident(k),pos=v.indexOf('.'),op=v.slice(0,pos),raw=v.slice(pos+1);if(op==='is'&&raw==='null'){clauses.push(c+' IS NULL');continue}if(op==='in'){const vals=raw.replace(/^\(|\)$/g,'').split(',');clauses.push(c+' IN ('+vals.map(()=>'?').join(',')+')');args.push(...vals);continue}const ops={eq:'=',neq:'!=',lt:'<',lte:'<=',gt:'>',gte:'>=',ilike:'LIKE'};if(!ops[op])throw Error('Unsupported query filter');clauses.push(c+' '+ops[op]+' ?');args.push(raw==='true'?1:raw==='false'?0:op==='ilike'?raw.replaceAll('*','%'):raw)}
 if(table==='members'&&method==='GET')clauses.push('is_active=1');
 const where=clauses.length?' WHERE '+clauses.join(' AND '):'';
 const fields=q.get('select')||'*',select=fields==='*'?'*':fields.split(',').map(ident).join(',');
 if(method==='GET'){let order='';if(q.get('order')){const [col,dir]=q.get('order').split('.');order=' ORDER BY '+ident(col)+(dir==='desc'?' DESC':' ASC')}return signRows(await sql('SELECT '+select+' FROM '+ident(table)+where+order+' LIMIT '+Math.min(1000,Number(q.get('limit'))||1000),args))}
 if(method==='DELETE'){if(!where)throw Error('Delete filter required');return sql('DELETE FROM '+ident(table)+where+' RETURNING *',args)}
 if(method==='PATCH'){if(!where)throw Error('Update filter required');return sql('UPDATE '+ident(table)+' SET '+Object.keys(body).map(k=>ident(k)+'=?').join(',')+where+' RETURNING *',[...Object.values(body),...args])}
 if(method!=='POST')throw Error('Unsupported method');const info=await sql('PRAGMA table_info('+ident(table)+')'),result=[];
 for(const input of Array.isArray(body)?body:[body]){const row={...input},cols=info.map(c=>c.name);if(cols.includes('id')&&!row.id)row.id=Date.now()*1000+randomBytes(2).readUInt16BE()%1000;if(cols.includes('created_at')&&!row.created_at)row.created_at=new Date().toISOString();if(table==='presence_history'){const old=await sql('SELECT id FROM presence_history WHERE member_id=? AND day=?',[row.member_id,row.day]);if(old[0])row.id=old[0].id}const keys=Object.keys(row);if(keys.some(k=>!cols.includes(k)))throw Error('Unknown column');if(q.get('on_conflict')==='source_key'&&row.source_key){const old=await sql('SELECT id FROM '+ident(table)+' WHERE source_key=?',[row.source_key]);if(old[0])row.id=old[0].id}const conflict=info.filter(c=>c.pk).map(c=>c.name);const update=keys.filter(k=>!conflict.includes(k));const suffix=conflict.length?' ON CONFLICT ('+conflict.map(ident).join(',')+') DO '+(update.length?'UPDATE SET '+update.map(k=>ident(k)+'=excluded.'+ident(k)).join(','):'NOTHING'):'';result.push(...await sql('INSERT INTO '+ident(table)+' ('+keys.map(ident).join(',')+') VALUES ('+keys.map(()=>'?').join(',')+')'+suffix+' RETURNING *',Object.values(row)))}
 if(['protect_updates','squad_place_posts'].includes(table))for(const r of result){
 const protect=table==='protect_updates';
 await db('squad_notification_events',{method:'POST',query:'?on_conflict=source_key',body:{
 squad_id:r.squad_id,category:protect?'protect_updated':r.kind==='base'||r.kind==='other'?'base':'stash',
 actor_id:protect?r.member_id:r.created_by,title:protect?'ПРОТЕКТ ОБНОВЛЕН':r.kind==='stash'?'Новый схрон':'Новая база',
 body:r.map_marker_name||String(r.comment||'').split('\\n')[0],source_key:(protect?'protect-updated:':'squad-place:')+r.id
 }});
 }
 return result;
 }
 async function linked(uid){return (await sql('SELECT m.* FROM members m JOIN discord_member_links d ON d.member_id=m.id WHERE d.discord_user_id=? AND m.is_active=1',[uid]))[0]}
 async function rpc(name,p={}){
 const now=new Date().toISOString();
 if(name==='consume_discord_link_code'){
 const rows=await sql('UPDATE discord_link_codes SET used_at=? WHERE code_hash=? AND used_at IS NULL AND expires_at>? AND member_id IN (SELECT id FROM members WHERE is_active=1) AND NOT EXISTS(SELECT 1 FROM discord_member_links WHERE discord_user_id=? OR member_id=discord_link_codes.member_id) RETURNING member_id',[now,p.p_code_hash,now,p.p_discord_user_id]);if(!rows[0])return false;await db('discord_member_links',{method:'POST',body:{discord_user_id:p.p_discord_user_id,member_id:rows[0].member_id,linked_at:now}});return true}
 if(name==='claim_discord_dm_delivery'){const rows=await sql("INSERT INTO discord_dm_deliveries(event_id,discord_user_id,status,attempts,updated_at) VALUES(?,?,'sending',1,?) ON CONFLICT(event_id,discord_user_id) DO UPDATE SET status='sending',attempts=attempts+1,updated_at=excluded.updated_at WHERE status='failed' AND attempts<3 RETURNING event_id",[p.p_event_id,p.p_discord_user_id,now]);return !!rows.length}
 const m=await linked(p.p_discord_user_id);if(!m)return false;
 if(name==='discord_update_protect'){if(!['admin','owner','leader'].includes(m.access_role))return false;const latest=(await sql('SELECT updated_at FROM protect_updates WHERE squad_id=? ORDER BY updated_at DESC LIMIT 1',[m.squad_id]))[0];if(latest&&Date.now()-Date.parse(latest.updated_at)<30000)return false;await db('protect_updates',{method:'POST',body:{squad_id:m.squad_id,member_id:m.id,updated_at:now,effective_at:now}});return true}
 if(name==='discord_set_protect_start'){if(!['admin','owner','leader'].includes(m.access_role)||!/^\d\d:\d\d$/.test(p.p_start_time))return false;const last=(await sql('SELECT * FROM protect_updates WHERE squad_id=? ORDER BY updated_at DESC LIMIT 1',[m.squad_id]))[0];if(!last)return false;const day=new Intl.DateTimeFormat('en-CA',{timeZone:'Europe/Moscow',year:'numeric',month:'2-digit',day:'2-digit'}).format(new Date());const effective=new Date(Date.parse(day+'T'+p.p_start_time+':00+03:00')-3600000).toISOString();await sql('UPDATE protect_updates SET effective_at=? WHERE id=? AND squad_id=?',[effective,last.id,m.squad_id]);return true}
 if(name==='raise_discord_raid_alarm'){const old=(await sql('SELECT created_at FROM squad_raid_alarms WHERE squad_id=? ORDER BY created_at DESC LIMIT 1',[m.squad_id]))[0];if(old&&Date.now()-Date.parse(old.created_at)<300000)return false;const id=Date.now();await db('squad_raid_alarms',{method:'POST',body:{id,squad_id:m.squad_id,member_id:m.id,created_at:now}});await db('fury_alarm_channel_jobs',{method:'POST',body:{alarm_id:id,sent_count:0,updated_at:now}});return true}
 throw Error('Unsupported D1 RPC '+name);
 }
 async function upload(file,fileName,folder){if(!env.IMAGEKIT_PRIVATE_KEY)throw Error('ImageKit not configured');const form=new FormData();form.set('file',Buffer.from(file).toString('base64'));form.set('fileName',fileName);form.set('folder','/scum-hub/'+folder);const r=await fetcher('https://upload.imagekit.io/api/v1/files/upload',{method:'POST',headers:{Authorization:'Basic '+Buffer.from(env.IMAGEKIT_PRIVATE_KEY+':').toString('base64')},body:form});if(!r.ok)throw Error('ImageKit upload failed '+r.status);return (await r.json()).url}
 return {ready,db,rpc,upload,sql};
}
