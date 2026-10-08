# FURY HUB BOT — test v0.1

Discord Interactions HTTP service for deployment on Layero. The current version provides a **read-only test panel** via `/fury`. Buttons acknowledge actions but do **not** modify Supabase or send raid alerts.

## Deploy

1. Deploy this repository as a **Node.js web service** on Layero. Start command: `npm start`. Node.js >= 20.
2. Set environment variables: `DISCORD_PUBLIC_KEY` (the **public key**, not bot token), `DISCORD_GUILD_ID` (test Discord server ID), optional `FURYHUB_URL`. Layero should set `PORT` automatically.
3. Check `https://YOUR-LAYERO-DOMAIN/health` responds with `{"ok":true,...}`.
4. Discord Developer Portal > General Information > Public Key: copy into Layero variable `DISCORD_PUBLIC_KEY`.
5. Discord Developer Portal > General Information > Interactions Endpoint URL: `https://YOUR-LAYERO-DOMAIN/interactions`. Save; Discord validates the signature handling.
6. Register `/fury` as a test-guild command by running `node scripts/register-command.js` in a trusted environment with `DISCORD_BOT_TOKEN`, `DISCORD_APPLICATION_ID`, and `DISCORD_GUILD_ID`. **Never** put the token in Git or screenshots.

## Security

- Every Discord request is verified using the Ed25519 signature and timestamp headers.
- `DISCORD_GUILD_ID` restricts the test panel to the chosen server.
- No Supabase access yet, and there are no data-changing actions.
- Do not configure or use production service-role keys until server-side authorization and a test database are ready.
