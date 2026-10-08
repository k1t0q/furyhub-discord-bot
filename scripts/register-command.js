const token = process.env.DISCORD_BOT_TOKEN;
const appId = process.env.DISCORD_APPLICATION_ID;
const guildId = process.env.DISCORD_GUILD_ID;
if (!token || !appId || !guildId) throw new Error('Set DISCORD_BOT_TOKEN, DISCORD_APPLICATION_ID and DISCORD_GUILD_ID');
const url = `https://discord.com/api/v10/applications/${appId}/guilds/${guildId}/commands`;
const result = await fetch(url, { method: 'POST', headers: { Authorization: `Bot ${token}`, 'Content-Type': 'application/json' }, body: JSON.stringify({ name: 'fury', description: 'Открыть панель управления FURY HUB', type: 1 }) });
if (!result.ok) throw new Error(`Discord API ${result.status}: ${await result.text()}`);
console.log('Guild command /fury registered');
