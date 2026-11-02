import {createHash,randomUUID} from 'node:crypto';
import {mkdtemp,readFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
const run=promisify(execFile);
const kinds={stash:'📦 Схрон',car:'🚗 Машина',base:'⚔️ Вражеская база',other:'📍 Метка'};
const safe=text=>String(text||'').replace(/[\\`*_~|<>\[\]]/g,'').replace(/[\r\n]/g,' ').slice(0,100);
export function announcementPosition(job){
 let x,y;
 if(job.kind==='car'){
  if(!Number.isFinite(job.x)||!Number.isFinite(job.y)||job.x<0||job.x>1||job.y<0||job.y>1)throw Error('Invalid normalized coordinates');
  x=619646.8573-job.x*(619646.8573+905369.0266);y=619659.7258-job.y*(619659.7258+904357.527);
 }else{
  const text=String(job.raw_coords||'').replace(/,/g,'.');
  x=Number(text.match(/\bX\s*[:=]\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?)/i)?.[1]);
  y=Number(text.match(/\bY\s*[:=]\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?)/i)?.[1]);
 }
 if(!Number.isFinite(x)||!Number.isFinite(y)||x>619646.8573||x< -905369.0266||y>619659.7258||y< -904357.527)throw Error('Invalid map coordinates');
 return {x,y};
}
export async function renderScumMapPreview(x,y,env=process.env){
 if(!Number.isFinite(x)||!Number.isFinite(y))throw Error('Invalid preview coordinates');
 const folder=await mkdtemp(join(tmpdir(),'scum-preview-'));
 try{
  const output=join(folder,'preview.png');
  await run(env.SCUM_MAP_PYTHON||'/opt/scum-hub-bot/.venv/bin/python',['scripts/preview_scum_map.py',env.SCUM_MAP_REFERENCE_PATH||'/opt/scum-hub-bot/map-detector/reference_map.png',String(x),String(y),output],{cwd:process.cwd(),timeout:18000,maxBuffer:16384});
  return await readFile(output);
 }finally{await rm(folder,{recursive:true,force:true});}
}
export function announcementPayload(job,position){
 if(!kinds[job.kind])throw Error('Unknown marker kind');
 const x=String(Math.round(position.x*1000)/1000),y=String(Math.round(position.y*1000)/1000);
 return {content:`${kinds[job.kind]} · **${safe(job.name)}**\nАвтор: ${safe(job.actor_nick)||'Участник'}\n\`X=${x} Y=${y} Z=0\``,
  embeds:[{title:'Место на карте',image:{url:'attachment://scum-marker-preview.png'},footer:{text:'SCUM HUB · '+job.id}}],
  attachments:[{id:0,filename:'scum-marker-preview.png'}],allowed_mentions:{parse:[]},
  nonce:createHash('sha256').update(job.id).digest('hex').slice(0,24),enforce_nonce:true};
}
export function createMinimapAnnouncements({sql,env=process.env,fetcher=fetch,render=renderScumMapPreview,clock=()=>Date.now(),logger=console}){
 let busy=false;const channels=new Map();
 const request=async(path,options={})=>{const r=await fetcher('https://discord.com/api/v10/'+path,{...options,headers:{Authorization:'Bot '+env.DISCORD_BOT_TOKEN,...options.headers},signal:AbortSignal.timeout(20000)});if(!r.ok)throw Error('Discord '+r.status);return r.json();};
 const verifyChannel=async id=>{if(!/^\d{15,22}$/.test(id))throw Error('Invalid destination channel');if(channels.get(id)>clock())return;const channel=await request('channels/'+id);if(channel.guild_id!==env.DISCORD_GUILD_ID||![0,5,10,11,12].includes(channel.type))throw Error('Destination is outside the configured Discord server');channels.set(id,clock()+300000);};
 async function poll(){
  if(busy||env.DISCORD_TEST_MODE!=='false'||!env.DISCORD_BOT_TOKEN||!env.DISCORD_GUILD_ID)return;
  busy=true;
  try{
   const now=new Date(clock()).toISOString(),token=randomUUID(),lease=new Date(clock()+180000).toISOString();
   const jobs=await sql(`UPDATE minimap_discord_outbox SET status='sending',attempts=attempts+1,claim_token=?,lease_until=?
    WHERE id IN (SELECT o.id FROM minimap_discord_outbox o JOIN minimap_discord_routes r ON r.squad_id=o.squad_id AND r.channel_id=o.channel_id AND r.enabled=1
     JOIN members m ON m.id=o.actor_id AND m.squad_id=o.squad_id AND m.is_active=1
     WHERE o.attempts<5 AND ((o.status IN ('pending','failed') AND o.next_attempt_at<=?) OR (o.status='sending' AND o.lease_until<=?)) ORDER BY o.created_at LIMIT 5)
    RETURNING *`,[token,lease,now,now]);
   for(const job of jobs){
    try{
     const position=announcementPosition(job);await verifyChannel(job.channel_id);
     let message;
     if(job.attempts>1){const recent=await request('channels/'+job.channel_id+'/messages?limit=100');message=recent.find(m=>m.embeds?.some(e=>e.footer?.text==='SCUM HUB · '+job.id));}
     if(!message){
      const members=await sql('SELECT nick FROM members WHERE id=? AND squad_id=? AND is_active=1',[job.actor_id,job.squad_id]);job.actor_nick=members[0]?.nick||'Участник';
      const image=await render(position.x,position.y,env);const form=new FormData();form.append('payload_json',JSON.stringify(announcementPayload(job,position)));form.append('files[0]',new Blob([image],{type:'image/png'}),'scum-marker-preview.png');
      message=await request('channels/'+job.channel_id+'/messages',{method:'POST',body:form});
     }
     if(!/^\d{15,22}$/.test(String(message.id||'')))throw Error('Discord did not confirm the message');
     await sql("UPDATE minimap_discord_outbox SET status='sent',discord_message_id=?,lease_until='',last_error=NULL WHERE id=? AND claim_token=?",[String(message.id),job.id,token]);
     logger.log('[discord] Mini-map location published:',job.id);
    }catch(error){
     const retry=new Date(clock()+Math.min(900000,60000*2**Math.max(0,job.attempts-1))).toISOString();
     await sql("UPDATE minimap_discord_outbox SET status='failed',next_attempt_at=?,lease_until='',last_error=? WHERE id=? AND claim_token=?",[retry,String(error.message).slice(0,200),job.id,token]);
     logger.error('[discord] Mini-map location failed:',job.id,error.message);
    }
   }
  }catch(error){logger.error('[discord] Mini-map queue:',error.message);}finally{busy=false;}
 }
 return {poll};
}
