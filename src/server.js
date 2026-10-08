import http from 'node:http';
import { readFileSync } from 'node:fs';
import { verifyKey, InteractionType, InteractionResponseType } from 'discord-interactions';

const port = Number(process.env.PORT || 3000);
const publicKey = process.env.DISCORD_PUBLIC_KEY;
const allowedGuild = process.env.DISCORD_GUILD_ID;
const homepage = process.env.FURYHUB_URL || 'https://scumhub.layero.app/';
const json = (res, status, payload) => {
  res.writeHead(status, { 'content-type': 'application/json; charset=utf-8' });
  res.end(JSON.stringify(payload));
};
const button = (label, id, style = 2) => ({ type: 2, style, label, custom_id: id });
const panel = (publicPost = false) => ({
  type: InteractionResponseType.CHANNEL_MESSAGE_WITH_SOURCE,
  data: {
    ...(publicPost ? {} : { flags: 64 }),
    embeds: [{
      image: { url: 'https://furyhub-discord-bot-main.layero.app/banner.png' },
      author: { name: 'ДУБЛЬ · ШТАБ ОТРЯДА' },
      title: 'SCUM HUB',
      description: '**Управление отрядом в Discord**\nАктуальные данные из ДУБЛЬ. Выбери нужный раздел ниже.',
      color: 0xb32932,
      fields: [
        { name: 'СОСТАВ И ПРОТЕКТ', value: 'Отметки участников и время защиты базы.', inline: false },
        { name: 'РЕЙДЫ И РЕСУРСЫ', value: 'Планы рейдов, задачи и запасы взрывчатки.', inline: false },
        { name: 'ЭКСТРЕННАЯ ТРЕВОГА', value: 'Кнопка «НАС РЕЙДЯТ» для срочного оповещения.', inline: false }
      ],
      ...(process.env.DISCORD_TEST_MODE === 'false' ? {} : { footer: { text: 'Тестовый режим · изменение данных отключено · SCUM HUB' } })
    }],
    components: [
      row({ type: 3, custom_id: 'fury:menu', placeholder: 'Выберите нужное', min_values: 1, max_values: 1, options: [
        { label: 'Моё участие', value: 'fury:presence', description: 'Отметиться и посмотреть состав', emoji: { name: '👥' } },
        { label: 'Протект', value: 'fury:protect', description: 'Время и обновление протекта', emoji: { name: '🛡️' } },
        { label: 'Рейды', value: 'fury:raids', description: 'Ближайшие рейды и участие', emoji: { name: '🎯' } },
        { label: 'Задачи', value: 'fury:tasks', description: 'Список задач отряда', emoji: { name: '📋' } },
        { label: 'Взрыв', value: 'fury:explosives', description: 'Запасы взрывчатки', emoji: { name: '💥' } }
      ] }),
      row(button('НАС РЕЙДЯТ', 'fury:alarm', 4), { type: 2, style: 5, label: 'ОТКРЫТЬ ДУБЛЬ ↗', url: homepage })
    ]
  }
});

const supabaseUrl = process.env.SUPABASE_URL;
const serviceKey = process.env.SUPABASE_SERVICE_ROLE_KEY;
const apiReady = () => Boolean(supabaseUrl && serviceKey);
const ephem = (content, components = []) => ({ type: 4, data: { flags: 64, content: String(content).slice(0,1900), components } });
const row = (...buttons) => ({ type: 1, components: buttons });
async function db(table, options = {}) {
  if (!apiReady()) throw Error('Supabase server configuration missing');
  const { method = 'GET', query = '', body } = options;
  const res = await fetch(supabaseUrl.replace(/\/$/, '') + '/rest/v1/' + table + query, {
    method, headers: { apikey: serviceKey, Authorization: 'Bearer ' + serviceKey, 'Content-Type': 'application/json', Prefer: 'return=representation,resolution=merge-duplicates' },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }), signal: AbortSignal.timeout(8000)
  });
  if (!res.ok) throw Error('Database request failed (' + res.status + ')');
  const text = await res.text(); return text ? JSON.parse(text) : [];
}
async function rpc(name, payload) {
  if (!apiReady()) throw Error('Supabase not configured');
  const res = await fetch(supabaseUrl.replace(/\/$/,'') + '/rest/v1/rpc/' + name, {
    method: 'POST',
    headers: { apikey: serviceKey, Authorization: 'Bearer ' + serviceKey, 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
    signal: AbortSignal.timeout(8000)
  });
  if (!res.ok) { const detail = (await res.text()).slice(0,800); throw Error('RPC ' + name + ' failed (' + res.status + '): ' + detail); }
  return res.json();
}
async function linked(interaction) {
  const id = interaction.member?.user?.id || interaction.user?.id;
  if (!id || !/^\d{15,22}$/.test(id)) return null;
  const links = await db('discord_member_links', { query: '?discord_user_id=eq.' + id + '&select=member_id' });
  if (!links.length) return null;
  const members = await db('members', { query: '?id=eq.' + links[0].member_id + '&select=id,squad_id,nick,access_role' });
  return members[0] || null;
}
const admin = m => ['admin','owner','leader'].includes(String(m?.access_role || '').toLowerCase());
const moscowDay = () => { const parts = Object.fromEntries(new Intl.DateTimeFormat('en-GB', { timeZone: 'Europe/Moscow', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date()).filter(p => p.type !== 'literal').map(p => [p.type, p.value])); return `${parts.year}-${parts.month}-${parts.day}`; };
const fmt = x => x ? new Date(x).toLocaleString('ru-RU',{timeZone:'Europe/Moscow',day:'2-digit',month:'2-digit',hour:'2-digit',minute:'2-digit'}) : '—';

const dmColors = { alarm: 0xd02b37, protect: 0xe2a13a, protect_updated: 0xe2a13a, raids: 0xb32932, tasks: 0x458ac9, stash: 0xd69d41, presence: 0x458ac9, posts: 0x458ac9 };
const protectClock = value => new Date(value).toLocaleTimeString('ru-RU',{timeZone:'Europe/Moscow',hour:'2-digit',minute:'2-digit'});
async function protectDMContent(event) {
  const rows = await db('protect_updates',{query:'?squad_id=eq.'+event.squad_id+'&select=updated_at,effective_at,member_id&order=updated_at.desc&limit=1'});
  const last = rows[0];
  if (!last) return null;
  const authors = last.member_id ? await db('members',{query:'?id=eq.'+last.member_id+'&select=nick&limit=1'}) : [];
  const base = new Date(last.effective_at || last.updated_at);
  if (!Number.isFinite(base.getTime())) return null;
  return {
    title: 'ПРОТЕКТ',
    description: 'Последнее обновление:\n**'+(authors[0]?.nick || 'Участник')+' · '+protectClock(base)+'**\nНачало: **'+protectClock(base.getTime()+3600000)+'**\nСпадёт: **'+protectClock(base.getTime()+32400000)+'**'
  };
}
async function sendDiscordDM(userId, event) {
  const token = process.env.DISCORD_BOT_TOKEN;
  if (!token) throw Error('Bot token missing');
  const headers = { Authorization: 'Bot ' + token, 'Content-Type': 'application/json' };
  const channel = await fetch('https://discord.com/api/v10/users/@me/channels', {
    method: 'POST', headers, body: JSON.stringify({ recipient_id: userId }), signal: AbortSignal.timeout(8000)
  });
  if (!channel.ok) throw Error('Cannot open DM (' + channel.status + ')');
  const { id } = await channel.json();
  const category = String(event.category || 'general').toLowerCase();
  const protect = category === 'protect_updated' ? await protectDMContent(event) : null;
  const msg = await fetch('https://discord.com/api/v10/channels/' + id + '/messages', {
    method: 'POST', headers,
    body: JSON.stringify({
      embeds: [{ title: String(protect?.title || event.title || 'SCUM HUB · Уведомление').slice(0,256), description: String(protect?.description || event.body || 'Новое событие отряда').slice(0,4000), color: dmColors[category] || 0xb32932, footer: { text: 'SCUM HUB | ДУБЛЬ' }, timestamp: event.created_at || new Date().toISOString() }],
      components: [{ type: 1, components: [{ type: 2, style: 5, label: 'Открыть ДУБЛЬ', url: homepage }] }]
    }), signal: AbortSignal.timeout(8000)
  });
  if (!msg.ok) throw Error('DM delivery failed (' + msg.status + ')');
}
let discordDmPolling = false;
async function processDiscordNotifications() {
  if (discordDmPolling) return;
  const blockers = [
    !apiReady() && 'Supabase not configured',
    !process.env.DISCORD_BOT_TOKEN && 'bot token missing',
    process.env.DISCORD_DM_NOTIFICATIONS !== 'true' && 'notifications disabled',
    process.env.DISCORD_TEST_MODE !== 'false' && 'test mode enabled'
  ].filter(Boolean);
  if (blockers.length) {
    console.warn('[discord] DM skipped:', blockers.join(', '));
    return;
  }
  const launchAt = process.env.DISCORD_DM_START_AT;
  if (!launchAt || !Number.isFinite(Date.parse(launchAt))) {
    console.warn('[discord] DM skipped: invalid DISCORD_DM_START_AT');
    return;
  }
  discordDmPolling = true;
  const stats = { events: 0, links: 0, enabled: 0, candidates: 0, claimed: 0, sent: 0, failed: 0, ignoredActor: 0, ignoredCategory: 0, ignoredSquad: 0, unclaimed: 0 };
  try {
    const events = await db('squad_notification_events',{query:'?created_at=gte.'+encodeURIComponent(new Date(launchAt).toISOString())+'&select=id,squad_id,category,actor_id,title,body,created_at&order=id.desc&limit=60'});
    stats.events = events.length;
    if (!events.length) return;
    const links = await db('discord_member_links',{query:'?select=discord_user_id,member_id'});
    const members = await db('members',{query:'?select=id,squad_id'});
    const prefs = await db('discord_dm_preferences',{query:'?select=member_id,enabled,categories'});
    stats.links = links.length;
    stats.enabled = prefs.filter(p=>p.enabled === true).length;
    const memberById = new Map(members.map(m=>[m.id,m]));
    const prefById = new Map(prefs.map(p=>[p.member_id,p]));
    for (const event of events.reverse()) {
      for (const link of links) {
        const member = memberById.get(link.member_id);
        if (!member || member.squad_id !== event.squad_id) { stats.ignoredSquad++; continue; }
        if (member.id === event.actor_id) { stats.ignoredActor++; continue; }
        const pref = prefById.get(member.id);
        if (!pref || pref.enabled !== true || pref.categories?.[event.category] !== true) { stats.ignoredCategory++; continue; }
        stats.candidates++;
        const claimed = await rpc('claim_discord_dm_delivery',{p_event_id:event.id,p_discord_user_id:link.discord_user_id});
        if (claimed !== true) { stats.unclaimed++; continue; }
        stats.claimed++;
        try {
          await sendDiscordDM(link.discord_user_id,event);
          await db('discord_dm_deliveries',{method:'PATCH',query:'?event_id=eq.'+event.id+'&discord_user_id=eq.'+link.discord_user_id,body:{status:'sent',delivered_at:new Date().toISOString(),updated_at:new Date().toISOString()}});
          stats.sent++;
        } catch (e) {
          stats.failed++;
          console.warn('[discord] DM delivery failed:',event.id,String(e.message).slice(0,120));
          await db('discord_dm_deliveries',{method:'PATCH',query:'?event_id=eq.'+event.id+'&discord_user_id=eq.'+link.discord_user_id,body:{status:'failed',last_error:String(e.message).slice(0,150),updated_at:new Date().toISOString()}});
        }
      }
    }
  } catch (error) {
    console.error('[discord] DM polling error:', String(error?.message || error).slice(0,250));
  } finally {
    console.log('[discord] DM poll summary:', JSON.stringify(stats));
    discordDmPolling = false;
  }
}

async function sectionProtect(interaction,m) {
  const p=await db('protect_updates',{query:'?squad_id=eq.'+m.squad_id+'&select=updated_at,effective_at,member_id&order=updated_at.desc&limit=1'});
  const last=p[0];
  const buttons=admin(m)?[row(button('Обновить протект','fury:protect:update',3),button('Изменить время обновления','fury:protect:set-start',2))]:[];
  if(!last)return ephem('**ПРОТЕКТ**\nПока нет обновлений.',buttons);
  const author=last.member_id?await db('members',{query:'?id=eq.'+last.member_id+'&select=nick&limit=1'}):[];
  const base=new Date(last.effective_at||last.updated_at);
  const clock=minutes=>new Date(base.getTime()+minutes*60000).toLocaleTimeString('ru-RU',{timeZone:'Europe/Moscow',hour:'2-digit',minute:'2-digit'});
  return ephem('**ПРОТЕКТ**\nПоследнее обновление:\n**'+(author[0]?.nick||'Участник')+' · '+base.toLocaleTimeString('ru-RU',{timeZone:'Europe/Moscow',hour:'2-digit',minute:'2-digit'})+'**\nНачало: **'+clock(60)+'**\nСпадёт: **'+clock(540)+'**',buttons);
}
async function handle(interaction) {
  if (interaction.type === InteractionType.PING) return { type: InteractionResponseType.PONG };
  if (allowedGuild && interaction.guild_id !== allowedGuild) return ephem('Бот доступен только на сервере отряда.');
  const name = interaction.data?.name;
  if (interaction.type === InteractionType.APPLICATION_COMMAND && name === 'fury') return panel();
  if (interaction.type === InteractionType.APPLICATION_COMMAND && name === 'status') {
    const perms = BigInt(interaction.member?.permissions || '0');
    if ((perms & 0x8n) === 0n && (perms & 0x20n) === 0n) return ephem('Диагностика доступна только администраторам сервера.');
    if (!apiReady()) return ephem('**SCUM HUB · диагностика**\nSupabase: переменные отсутствуют.');
    try {
      await db('members', { query: '?select=id&limit=1' });
      return ephem('**SCUM HUB · диагностика**\nDiscord: подключён\nSupabase: подключён (запрос выполнен)\nИзменение данных: '+(process.env.DISCORD_TEST_MODE === 'false' ? 'разрешено' : 'тестовый режим')+'\nЛичные уведомления: '+(process.env.DISCORD_DM_NOTIFICATIONS === 'true' && process.env.DISCORD_TEST_MODE === 'false' && Number.isFinite(Date.parse(process.env.DISCORD_DM_START_AT || '')) ? 'включены' : 'отключены'));
    } catch (error) {
      return ephem('**SCUM HUB · диагностика**\nSupabase: ошибка соединения (' + String(error.message).slice(0,100) + ')');
    }
  }
  if (interaction.type === InteractionType.APPLICATION_COMMAND && name === 'setup') {
    const p = BigInt(interaction.member?.permissions || '0');
    if ((p & 0x8n) === 0n && (p & 0x20n) === 0n) return ephem('Недостаточно прав.');
    return panel(true);
  }
  if (!apiReady()) return ephem('Интеграция с сайтом ещё не настроена. Администратор должен добавить SUPABASE_URL и SUPABASE_SERVICE_ROLE_KEY в Layero.');
  if (interaction.type === InteractionType.APPLICATION_COMMAND && name === 'link') {
    const code = String(interaction.data?.options?.find(x=>x.name==='code')?.value || '').trim().toLowerCase();
    if (!/^[a-f0-9]{36}$/.test(code)) return ephem('Неверный формат кода. Получи новый код в настройках сайта.');
    const { createHash } = await import('node:crypto');
    const hash = createHash('sha256').update(code).digest('hex');
    const uid = interaction.member?.user?.id || interaction.user?.id;
    if (!/^\d{15,22}$/.test(String(uid||''))) return ephem('Не удалось определить Discord-аккаунт.');
    const redeemed = await rpc('consume_discord_link_code', { p_code_hash: hash, p_discord_user_id: uid });
    return redeemed === true
      ? ephem('Аккаунт ДУБЛЬ успешно привязан. Теперь можно пользоваться панелью SCUM HUB.')
      : ephem('Код недействителен, истёк или аккаунт уже привязан. Создай новый код на сайте.');

  }
  if (interaction.type === InteractionType.MESSAGE_COMPONENT && ['fury:presence:Буду','fury:presence:change-time'].includes(interaction.data?.custom_id)) {
    if (process.env.DISCORD_TEST_MODE !== 'false') return ephem('Тестовый режим: изменение данных отключено.');
    return {type:9,data:{custom_id:'fury:presence:save-time',title:'Время захода в SCUM',components:[{type:1,components:[{type:4,custom_id:'join_time',label:'Время захода (МСК, ЧЧ:ММ)',style:1,min_length:5,max_length:5,required:true,placeholder:'20:30'}]}]}};
  }
  if (interaction.type === 5 && interaction.data?.custom_id === 'fury:presence:save-time') {
    if (process.env.DISCORD_TEST_MODE !== 'false') return ephem('Тестовый режим: изменение данных отключено.');
    const member=await linked(interaction);
    if (!member) return ephem('Сначала привяжи Discord к аккаунту ДУБЛЬ.');
    const time=String(interaction.data?.components?.[0]?.components?.[0]?.value||'').trim();
    if (!/^(?:[01]\d|2[0-3]):[0-5]\d$/.test(time)) return ephem('Введите время ЧЧ:ММ, например 20:30.');
    await db('presence_history',{method:'POST',query:'?on_conflict=member_id,day',body:{squad_id:member.squad_id,member_id:member.id,day:moscowDay(),status:'Буду',join_time:time,updated_at:new Date().toISOString()}});
    return ephem('**Моё участие · сегодня**\nСтатус: **Буду**\nВремя захода: **'+time+' МСК**\nСохранено на сайте ДУБЛЬ.',[row(button('Изменить время','fury:presence:change-time'))]);
  }
  if (interaction.type === InteractionType.MESSAGE_COMPONENT && interaction.data?.custom_id === 'fury:protect:set-start') {
    if (process.env.DISCORD_TEST_MODE !== 'false') return ephem('Тестовый режим: изменение данных отключено.');
    return {type:9,data:{custom_id:'fury:protect:set-start',title:'Изменить время обновления',components:[{type:1,components:[{type:4,custom_id:'start_time',label:'Время обновления (МСК, ЧЧ:ММ)',style:1,min_length:5,max_length:5,required:true,placeholder:'08:45'}]}]}};
  }
  const customId=String(interaction.data?.custom_id||'');
  if (interaction.type === 5 && customId === 'fury:protect:set-start') {
    if (process.env.DISCORD_TEST_MODE !== 'false') return ephem('Тестовый режим: изменение данных отключено.');
    const member=await linked(interaction);
    if (!member || !admin(member)) return ephem('Изменять время протекта может только администратор с привязанным аккаунтом.');
    const time=String(interaction.data?.components?.[0]?.components?.[0]?.value||'').trim();
    if (!/^(?:[01]\d|2[0-3]):[0-5]\d$/.test(time)) return ephem('Введите время в формате ЧЧ:ММ, например 08:45.');
    const ok=await rpc('discord_set_protect_start',{p_discord_user_id:interaction.member?.user?.id||interaction.user?.id,p_start_time:time});
    if(ok!==true)return ephem('Не удалось изменить время обновления протекта.');
    return await sectionProtect(interaction,member);
  }
  if (interaction.type !== InteractionType.MESSAGE_COMPONENT || !customId.startsWith('fury:')) return ephem('Неизвестная команда.');
  const m = await linked(interaction);
  if (!m) return ephem('Сначала привяжи аккаунт: открой сайт ДУБЛЬ → Настройки → Привязать Discord, затем используй /link.');
  const selection = interaction.data.custom_id === 'fury:menu' ? interaction.data.values?.[0] : interaction.data.custom_id;
  if (!/^fury:(presence|protect|raids|tasks|explosives|alarm)(?::|$)/.test(String(selection || ''))) return ephem('Неизвестный раздел.');
  const [,section,action] = selection.split(':');
  if (action && process.env.DISCORD_TEST_MODE !== 'false') return ephem('Тестовый режим: изменение данных пока отключено.');
  const squad = '?squad_id=eq.' + m.squad_id;
  if (section === 'presence') {
    if (['Буду','Под вопросом','Не буду'].includes(action)) {
      const date = moscowDay();
      const old = await db('presence_history',{query:'?member_id=eq.'+m.id+'&day=eq.'+date+'&select=join_time'});
      await db('presence_history',{method:'POST',query:'?on_conflict=member_id,day',body:{squad_id:m.squad_id,member_id:m.id,day:date,status:action,join_time:action==='Буду'?(old[0]?.join_time||null):null,updated_at:new Date().toISOString()}});
      return ephem('Статус обновлён: **'+action+'**.');
    }
    const today=await db('presence_history',{query:squad+'&day=eq.'+moscowDay()+'&select=member_id,status,join_time'});
    const members=await db('members',{query:squad+'&select=id,nick'});
    const lines=today.map(p=>(members.find(x=>x.id===p.member_id)?.nick||'Участник')+' — '+p.status+(p.join_time?' ('+p.join_time.slice(0,5)+')':''));
    return ephem('**Моё участие · сегодня**\n'+(lines.join('\n')||'Пока нет отметок.'),[row(button('Буду','fury:presence:Буду',3),button('Под вопросом','fury:presence:Под вопросом'),button('Не буду','fury:presence:Не буду',4)),row(button('Изменить время','fury:presence:change-time'))]);
  }
  if (section==='protect') {
    if(action==='set-start') {
      if(!admin(m)) return ephem('Изменять время протекта может только администратор.');
      return {type:9,data:{custom_id:'fury:protect:set-start',title:'Изменить время обновления',components:[{type:1,components:[{type:4,custom_id:'start_time',label:'Время обновления (МСК, ЧЧ:ММ)',style:1,min_length:5,max_length:5,required:true,placeholder:'08:45'}]}]}};
    }
    if(action==='update') {
      if(!admin(m)) return ephem('Обновление протекта доступно только администратору.');
      const updated=await rpc('discord_update_protect',{p_discord_user_id:interaction.member?.user?.id||interaction.user?.id});
      if(updated!==true)return ephem('Протект уже обновляли за последние 30 секунд или недостаточно прав.');
    }
    return await sectionProtect(interaction,m);
  }
  if (section==='raids') {
    const raids=await db('raids',{query:squad+'&select=id,title,raid_date,raid_time&order=created_at.desc&limit=8'});
    if(action?.startsWith('join-')) {
      const id=action.slice(5);if(!/^\d+$/.test(id))return ephem('Неверный рейд.');
      const found=raids.find(x=>String(x.id)===id);
      if(!found)return ephem('Рейд не найден в списке.');
      await db('raid_participants',{method:'POST',body:{raid_id:Number(id),member_id:m.id}});
      return ephem('Ты записан на рейд «'+found.title+'».');
    }
    return ephem('**Рейды**\n'+(raids.map(x=>'#'+x.id+' · '+x.title+' · '+(x.raid_date||'')+' '+(x.raid_time||'').slice(0,5)).join('\n')||'Рейдов пока нет.')+'\nДля создания и редактирования рейдов используй сайт.',raids.slice(0,4).map(x=>row(button('Участвовать #'+x.id,'fury:raids:join-'+x.id,3))));
  }
  const taskActive = t => {
    if (t.status === 'Выполнено' || t.status === 'Выполнена' || t.status === 'Просрочено') return false;
    const d = String(t.deadline || '').trim();
    const m = d.match(/^(\d{4}-\d{2}-\d{2})[ T](\d{2}:\d{2})(?::\d{2}(?:\.\d+)?)?(Z|[+-]\d{2}(?::?\d{2})?)?$/i);
    if (!m) return true;
    const timestamp = Date.parse(m[1] + 'T' + m[2] + ':00' + (m[3] || '+03:00'));
    return !Number.isFinite(timestamp) || Date.now() <= timestamp;
  };
  if (section==='tasks') {
    const tasks=(await db('tasks',{query:squad+'&select=id,title,status,assignee_id,creator_id,quantity,unit,deadline&order=created_at.desc&limit=100'})).filter(taskActive).slice(0,8);
    const [members,contributions]=await Promise.all([
      db('members',{query:squad+'&select=id,nick'}),
      db('task_contributions',{query:'?select=task_id,amount&task_id=in.('+ (tasks.map(t=>t.id).join(',')||'0') +')'})
    ]);
    const names=new Map(members.map(x=>[x.id,x.nick]));
    const collected=new Map();
    for(const c of contributions)collected.set(c.task_id,(collected.get(c.task_id)||0)+Number(c.amount||0));
    const unitLabel=(n,u)=>{
      if(String(u||'').toLowerCase()==='стак'){
        const a=Math.abs(n)%100,b=a%10;
        return a>=11&&a<=14?'стаков':b===1?'стак':b>=2&&b<=4?'стака':'стаков';
      }
      return u||'шт.';
    };
    const description=tasks.map(t=>{
      const need=Number(t.quantity||0),done=collected.get(t.id)||0;
      const deadline=String(t.deadline||'').trim().replace('T',' ').slice(0,16);
      return '**#'+t.id+' · '+t.title+'**\nСоздал: **'+(names.get(t.creator_id)||'Не указан')+'**\nНужно: '+need+' '+unitLabel(need,t.unit)+' · Собрано: '+done+' '+unitLabel(done,t.unit)+'\nДо: '+(deadline||'Не указан');
    }).join('\n\n');
    return ephem('**Активные задачи**\n\n'+(description||'Нет задач.'),[row({type:2,style:5,label:'ПЕРЕЙТИ К ЗАДАЧАМ ↗',url:homepage.replace(/\/$/,'')+'/?section=tasks'})]);
  }
  if (section==='explosives') {
    const items=await db('explosives_inventory',{query:squad+'&select=item,quantity'});
    const order=['С4','РПГ','ГРАНАТА','ДИНАМИТ','ПАЙПА','40'];
    const stock=new Map(items.map(x=>[x.item==='C4'?'С4':x.item==='Граната'?'ГРАНАТА':x.item,Number(x.quantity||0)]));
    const lines=order.map(name=>'**'+name+':** '+(stock.get(name)||0));
    const damage=(stock.get('С4')||0)*400+(stock.get('РПГ')||0)*200+(stock.get('ГРАНАТА')||0)*100+(stock.get('ДИНАМИТ')||0)*200;
    const walls=Math.floor(damage/1200);
    const word=(n)=>{const m=n%100,a=n%10;return m>=11&&m<=14?'СТЕН':a===1?'СТЕНУ':a>=2&&a<=4?'СТЕНЫ':'СТЕН';};
    return ephem('**ВЗРЫВ · ЗАПАСЫ**\n'+lines.join('\n')+'\n\nМОЖНО ВЗОРВАТЬ **'+walls+' '+word(walls)+'**',[row({type:2,style:5,label:'ПЕРЕЙТИ К ВЗРЫВУ ↗',url:homepage.replace(/\/$/,'')+'/?section=explosives'})]);
  }
  if (section==='alarm') {
    if(action==='confirm') {
      const sent = await rpc('raise_discord_raid_alarm',{p_discord_user_id:interaction.member?.user?.id || interaction.user?.id});
      if (sent !== true) return ephem('Тревога уже отправлялась за последние 5 минут. Повторная отправка заблокирована.');
      return ephem('**Тревога отправлена!** Отряд получит уведомление через действующую систему сайта.');
    }
    return ephem('**НАС РЕЙДЯТ**\nПодтверди отправку тревоги всему отряду.',[row(button('Подтвердить тревогу','fury:alarm:confirm',4))]);
  }
  return ephem('Раздел не найден.');
}
// Register the test-guild slash command on startup, without exposing the bot token.
async function registerGuildCommand() {
  const token = process.env.DISCORD_BOT_TOKEN;
  const appId = process.env.DISCORD_APPLICATION_ID;
  const guildId = process.env.DISCORD_GUILD_ID;
  if (!token || !appId || !guildId) {
    console.warn('[discord] /fury registration skipped: missing environment variables', {
      DISCORD_BOT_TOKEN: Boolean(token),
      DISCORD_APPLICATION_ID: Boolean(appId),
      DISCORD_GUILD_ID: Boolean(guildId)
    });
    return;
  }
  try {
    const url = `https://discord.com/api/v10/applications/${encodeURIComponent(appId)}/guilds/${encodeURIComponent(guildId)}/commands`;
    const result = await fetch(url, {
      method: 'PUT',
      headers: { Authorization: `Bot ${token}`, 'Content-Type': 'application/json' },
      body: JSON.stringify([{ name: 'fury', description: 'Открыть панель управления SCUM HUB', type: 1 }, { name: 'setup', description: 'Опубликовать постоянную панель SCUM HUB (администратор)', type: 1, default_member_permissions: '32' }, { name: 'link', description: 'Привязать аккаунт ДУБЛЬ к Discord', type: 1, options: [{ name: 'code', description: 'Одноразовый код из настроек сайта', type: 3, required: true }] }, { name: 'status', description: 'Проверить состояние интеграции SCUM HUB', type: 1 }]),
      signal: AbortSignal.timeout(12000)
    });
    if (!result.ok) {
      const detail = await result.text();
      console.error('[discord] /fury registration failed', { status: result.status, detail: detail.slice(0, 500) });
      return;
    }
    console.log('[discord] /fury and /setup guild commands registered successfully');
  } catch (error) {
    console.error('[discord] /fury registration request failed', error?.message || 'Unknown error');
  }
}
http.createServer(async (req, res) => {
  if (req.method === 'GET' && req.url === '/banner.png') {
    try {
      const png = readFileSync(new URL('../Алый закат над укреплённой долиной.png', import.meta.url));
      res.writeHead(200, { 'content-type': 'image/png', 'cache-control': 'public, max-age=3600' });
      return res.end(png);
    } catch (error) {
      console.error('[banner] Unable to read image:', error.message);
      return json(res, 404, { error: 'Banner unavailable' });
    }
  }
  if (req.method === 'GET' && req.url === '/health') return json(res, 200, { ok: true, service: 'furyhub-discord-bot' });
  if (req.method !== 'POST' || req.url !== '/interactions') return json(res, 404, { error: 'Not found' });
  if (!publicKey) { console.error('[discord] DISCORD_PUBLIC_KEY missing'); return json(res, 503, { error: 'Missing Discord public key' }); }
  const chunks = [];
  let length = 0;
  for await (const chunk of req) {
    length += chunk.length;
    if (length > 1024 * 1024) return json(res, 413, { error: 'Request too large' });
    chunks.push(chunk);
  }
  const body = Buffer.concat(chunks);
  const signature = req.headers['x-signature-ed25519'];
  const timestamp = req.headers['x-signature-timestamp'];
  let signatureValid = false;
  try {
    if (typeof signature === 'string' && typeof timestamp === 'string') {
      signatureValid = (await verifyKey(body, signature, timestamp, publicKey)) === true;
    }
  } catch (error) {
    console.warn('[discord] malformed signature rejected', { error: error?.name || 'Error' });
  }
  if (!signatureValid) {
    console.warn('[discord] rejected signature', { signaturePresent: typeof signature === 'string', timestampPresent: typeof timestamp === 'string', keyLength: publicKey.length });
    return json(res, 401, { error: 'Invalid signature' });
  }
  try {
    const interaction = JSON.parse(body.toString('utf8'));
    const isDeferred = (interaction.type === 5 && ['fury:protect:set-start','fury:presence:save-time'].includes(interaction.data?.custom_id)) ||
      (interaction.type === InteractionType.MESSAGE_COMPONENT && !['fury:protect:set-start','fury:presence:Буду','fury:presence:change-time'].includes(interaction.data?.custom_id)) ||
      (interaction.type === InteractionType.APPLICATION_COMMAND && ['link', 'status'].includes(interaction.data?.name));
    if (!isDeferred) {
      const reply = await handle(interaction);
      return json(res, 200, reply);
    }
    // Acknowledge within Discord's 3-second deadline before any database request.
    const appId = process.env.DISCORD_APPLICATION_ID;
    const token = interaction.token;
    if (!appId || !token) return json(res, 200, ephem('Интеграция не настроена: отсутствует ID приложения.'));
    json(res, 200, { type: 5, data: { flags: 64 } });
    void (async () => {
      let reply;
      try {
        reply = await handle(interaction);
      } catch (error) {
        console.error('[discord] deferred interaction failed:', error?.message || 'Unknown error');
        reply = ephem('Не удалось выполнить действие. Проверь логи бота в Layero.');
      }
      const data = reply?.data || { content: 'Не удалось получить ответ.' };
      try {
        const url = 'https://discord.com/api/v10/webhooks/' + encodeURIComponent(appId) + '/' + encodeURIComponent(token) + '/messages/@original';
        const response = await fetch(url, {
          method: 'PATCH',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ content: data.content || '', embeds: data.embeds || [], components: data.components || [] }),
          signal: AbortSignal.timeout(8000)
        });
        if (!response.ok) console.error('[discord] deferred response failed:', response.status);
      } catch (error) {
        console.error('[discord] deferred response transport:', error?.message || 'Unknown error');
      }
    })();
  } catch (error) {
    console.error('Interaction error', error?.message || 'Unknown error');
    return json(res, 200, ephem('Ошибка обработки команды.'));
  }
}).listen(port, '0.0.0.0', () => {
  console.log('FURY HUB Discord interactions listening on port', port);
  void registerGuildCommand();
});

if (process.env.DISCORD_DM_NOTIFICATIONS === 'true' && process.env.DISCORD_TEST_MODE === 'false' && process.env.DISCORD_DM_START_AT && Number.isFinite(Date.parse(process.env.DISCORD_DM_START_AT))) {
  void processDiscordNotifications().catch(err => console.error('[discord] DM polling:', err.message));
  setInterval(() => processDiscordNotifications().catch(err => console.error('[discord] DM polling:', err.message)), 60000).unref();
  console.log('[discord] DM notifications enabled');
}
