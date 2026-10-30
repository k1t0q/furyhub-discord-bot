This branch supports D1 + ImageKit without changing main or the running bot.

D1 mode requires CF_ACCOUNT_ID, CF_D1_DATABASE_ID, CF_D1_API_TOKEN (D1 edit, restricted to the migration database), and IMAGEKIT_PRIVATE_KEY. FURYHUB_URL must point to https://scum-hub-migration.nikitinrf.workers.dev/.

Keep DISCORD_TEST_MODE enabled until a separate test Discord application/guild is configured. Do not run two gateway instances with the production Discord token. Do not invoke the main Timeweb deployment workflow from this branch.

Implemented: D1 database transport, query filters, presence upsert, private link-code consumption, delivery claims, protect updates, alarm receipt/channel jobs, ImageKit photo uploads. The existing Discord handlers and panels use this transport when the D1 environment is configured.

Production activation requires coordinating the Discord endpoint/gateway switch. It has deliberately not been performed while the existing site must remain unchanged.
