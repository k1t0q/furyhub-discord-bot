// D1 replacement for the former Supabase scheduled notification jobs.
export async function queueMigrationReminders(db,now=new Date()){
 const ms=now.getTime(),dayMs=86400000;
 const parts=Object.fromEntries(new Intl.DateTimeFormat('en-CA',{timeZone:'Europe/Moscow',year:'numeric',month:'2-digit',day:'2-digit',hour:'2-digit',hourCycle:'h23'}).formatToParts(now).map(p=>[p.type,p.value]));
 const date=parts.year+'-'+parts.month+'-'+parts.day;
 let count=0;
 const queue=async(squad,category,title,body,key)=>{
   const old=await db('squad_notification_events',{query:'?source_key=eq.'+encodeURIComponent(key)+'&select=id&limit=1'});
   if(old.length)return;
   await db('squad_notification_events',{method:'POST',query:'?on_conflict=source_key',body:{squad_id:squad,category,actor_id:null,title,body,source_key:key,created_at:now.toISOString()}});
   count++;
 };
 if(Number(parts.hour)===15){
   const members=await db('members',{query:'?select=squad_id'});
   for(const squad of new Set(members.map(m=>m.squad_id)))await queue(squad,'presence','УЧАСТИЕ','Отметь, будешь ли ты сегодня','presence:'+date+':'+squad);
 }
 const protects=await db('protect_updates',{query:'?select=id,squad_id,updated_at,effective_at&order=updated_at.desc&limit=1000'});
 const latest=new Map();for(const p of protects)if(!latest.has(p.squad_id))latest.set(p.squad_id,p);
 for(const p of latest.values()){
   const end=Date.parse(p.effective_at||p.updated_at)+32400000,remaining=end-ms;
   if(!Number.isFinite(remaining)||remaining<=0)continue;
   const stage=remaining<=900000?15:remaining<=3600000?60:null;
   if(stage)await queue(p.squad_id,'protect','ПРОТЕКТ СКОРО СПАДЁТ','До окончания протекта осталось не больше '+stage+' минут.','protect-warning:'+p.id+':'+stage);
 }
 const posts=await db('squad_place_posts',{query:'?kind=eq.stash&select=id,squad_id,created_at,stash_checked_at,map_marker_name&limit=1000'});
 for(const p of posts){
   const days=Math.ceil((Date.parse(p.stash_checked_at||p.created_at)+14*dayMs-ms)/dayMs);
   if(![5,2,1,0].includes(days))continue;
   const name=p.map_marker_name||'Схрон';
   await queue(p.squad_id,'stash',days===0?'СХРОН ПРОПАЛ':'СХРОН: ОБНОВИ ТАЙМЕР',name+(days===0?': срок 14 дней истёк.':': осталось '+days+' дн.'),'stash-expiry:'+p.id+':'+(p.stash_checked_at||p.created_at)+':'+days);
 }
 return count;
}
