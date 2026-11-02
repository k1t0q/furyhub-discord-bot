CREATE TABLE IF NOT EXISTS minimap_discord_routes (
 squad_id TEXT PRIMARY KEY, channel_id TEXT NOT NULL, enabled INTEGER NOT NULL DEFAULT 1
);
CREATE TABLE IF NOT EXISTS minimap_discord_outbox (
 id TEXT PRIMARY KEY, squad_id TEXT NOT NULL, actor_id TEXT NOT NULL, channel_id TEXT NOT NULL,
 kind TEXT NOT NULL, name TEXT NOT NULL, raw_coords TEXT, x REAL, y REAL,
 created_at TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'pending', attempts INTEGER NOT NULL DEFAULT 0,
 next_attempt_at TEXT NOT NULL DEFAULT '', lease_until TEXT NOT NULL DEFAULT '', claim_token TEXT,
 discord_message_id TEXT, last_error TEXT
);
CREATE INDEX IF NOT EXISTS minimap_discord_pending ON minimap_discord_outbox(status,next_attempt_at,lease_until);
INSERT INTO minimap_discord_routes(squad_id,channel_id) VALUES('b6305fbe-04a7-451f-920e-d347e3faf953','1552235129827762276')
 ON CONFLICT(squad_id) DO UPDATE SET channel_id=excluded.channel_id;
CREATE TRIGGER IF NOT EXISTS minimap_post_discord_insert AFTER INSERT ON squad_place_posts
 WHEN NEW.kind IN ('stash','base','other') AND instr(coalesce(NEW.comment,''),'Источник: Миникарта SCUM HUB')>0
 BEGIN
 INSERT OR IGNORE INTO minimap_discord_outbox(id,squad_id,actor_id,channel_id,kind,name,raw_coords,created_at)
 SELECT 'post:'||NEW.id,NEW.squad_id,NEW.created_by,r.channel_id,NEW.kind,
 coalesce(nullif(NEW.map_marker_name,''),substr(NEW.comment,1,instr(NEW.comment,char(10))-1),'Метка'),NEW.comment,NEW.created_at
 FROM minimap_discord_routes r JOIN members m ON m.id=NEW.created_by AND m.squad_id=NEW.squad_id AND m.is_active=1
 WHERE r.squad_id=NEW.squad_id AND r.enabled=1;
 END;
CREATE TRIGGER IF NOT EXISTS minimap_vehicle_discord_insert AFTER INSERT ON squad_map_markers
 WHEN NEW.type='vehicle' AND NEW.id GLOB 'minimap-*'
 BEGIN
 INSERT OR IGNORE INTO minimap_discord_outbox(id,squad_id,actor_id,channel_id,kind,name,x,y,created_at)
 SELECT 'marker:'||NEW.id,NEW.squad_id,NEW.created_by,r.channel_id,'car',coalesce(nullif(NEW.name,''),'Машина'),NEW.x,NEW.y,coalesce(NEW.updated_at,strftime('%Y-%m-%dT%H:%M:%fZ','now'))
 FROM minimap_discord_routes r JOIN members m ON m.id=NEW.created_by AND m.squad_id=NEW.squad_id AND m.is_active=1
 WHERE r.squad_id=NEW.squad_id AND r.enabled=1;
 END;
