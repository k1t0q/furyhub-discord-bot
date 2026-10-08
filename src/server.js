import http from 'node:http';
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
const panel = () => ({
  type: InteractionResponseType.CHANNEL_MESSAGE_WITH_SOURCE,
  data: {
    flags: 64,
    embeds: [{
      title: 'ДУБЛЬ | SQUAD CONTROL',
      description: 'Тестовая панель FURY HUB. Интеграция с базой ещё не подключена.',
      color: 0xb32932,
      fields: [{ name: 'Режим', value: 'Тестовый сервер — данные отряда не изменяются.' }]
    }],
    components: [
      { type: 1, components: [button('Моё участие', 'fury:presence', 3), button('Протект', 'fury:protect'), button('Рейды', 'fury:raids')] },
      { type: 1, components: [button('Задачи', 'fury:tasks'), button('ВЗРЫВ', 'fury:explosives'), button('Нас рейдят', 'fury:alarm', 4)] },
      { type: 1, components: [{ type: 2, style: 5, label: 'Открыть FURYHUB', url: homepage }] }
    ]
  }
});
function response(interaction) {
  if (allowedGuild && interaction.guild_id !== allowedGuild) {
    return { type: 4, data: { flags: 64, content: 'Бот пока доступен только на тестовом сервере.' } };
  }
  if (interaction.type === InteractionType.PING) return { type: InteractionResponseType.PONG };
  if (interaction.type === InteractionType.APPLICATION_COMMAND && interaction.data?.name === 'fury') return panel();
  if (interaction.type === InteractionType.MESSAGE_COMPONENT && String(interaction.data?.custom_id || '').startsWith('fury:')) {
    const labels = { presence: 'Моё участие', protect: 'Протект', raids: 'Рейды', tasks: 'Задачи', explosives: 'ВЗРЫВ', alarm: 'Нас рейдят' };
    const section = String(interaction.data.custom_id).split(':')[1];
    return { type: 4, data: { flags: 64, content: '**' + (labels[section] || 'FURY HUB') + '**\nТестовая панель работает. Подключение к Supabase будет следующим этапом; никаких изменений пока не сделано.' } };
  }
  return { type: 4, data: { flags: 64, content: 'Неизвестная команда.' } };
}
http.createServer(async (req, res) => {
  if (req.method === 'GET' && req.url === '/health') return json(res, 200, { ok: true, service: 'furyhub-discord-bot' });
  if (req.method !== 'POST' || req.url !== '/interactions') return json(res, 404, { error: 'Not found' });
  if (!publicKey) return json(res, 503, { error: 'Missing Discord public key' });
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
  if (typeof signature !== 'string' || typeof timestamp !== 'string' || !verifyKey(body, signature, timestamp, publicKey)) {
    return json(res, 401, { error: 'Invalid signature' });
  }
  try { return json(res, 200, response(JSON.parse(body.toString('utf8')))); }
  catch (error) { console.error('Interaction error', error); return json(res, 500, { error: 'Internal error' }); }
}).listen(port, '0.0.0.0', () => console.log('FURY HUB Discord interactions listening on port', port));
