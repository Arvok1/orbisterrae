# Modelo de dados (esboço inicial)

## PostgreSQL (migrações `sqlx`)

```sql
CREATE EXTENSION IF NOT EXISTS postgis;

CREATE TABLE accounts (
  id UUID PRIMARY KEY, email TEXT UNIQUE NOT NULL, created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE kingdoms (
  id UUID PRIMARY KEY, owner_account UUID REFERENCES accounts(id),  -- NULL = IA
  name TEXT NOT NULL, culture TEXT NOT NULL, personality JSONB,
  created_at TIMESTAMPTZ NOT NULL, collapsed_at TIMESTAMPTZ
);

CREATE TABLE cities (
  id UUID PRIMARY KEY, kingdom_id UUID NOT NULL REFERENCES kingdoms(id),
  name TEXT NOT NULL, pos GEOGRAPHY(Point, 4326) NOT NULL,
  h3_shard BIGINT NOT NULL, tier SMALLINT NOT NULL, population JSONB NOT NULL,
  importance REAL NOT NULL DEFAULT 0, founded_at TIMESTAMPTZ NOT NULL
);
CREATE INDEX ON cities USING GIST (pos);
CREATE INDEX ON cities (h3_shard);

CREATE TABLE city_layouts (
  city_id UUID PRIMARY KEY REFERENCES cities(id), width INT, height INT,
  layers BYTEA NOT NULL,           -- RLE: terrain, roads, zones, buildings
  metrics JSONB NOT NULL, version BIGINT NOT NULL
);

CREATE TABLE territory_cells (
  h3_res7 BIGINT PRIMARY KEY, city_id UUID NOT NULL REFERENCES cities(id),
  influence REAL NOT NULL, contested BOOLEAN NOT NULL DEFAULT false
);

CREATE TABLE inventories (
  city_id UUID NOT NULL REFERENCES cities(id), good SMALLINT NOT NULL, qty DOUBLE PRECISION NOT NULL,
  PRIMARY KEY (city_id, good)
);

CREATE TABLE price_observations (   -- o que cada reino "sabe" sobre preços
  kingdom_id UUID, city_id UUID, good SMALLINT, price DOUBLE PRECISION, observed_at TIMESTAMPTZ,
  PRIMARY KEY (kingdom_id, city_id, good)
);

CREATE TABLE orders (               -- order book dos hubs
  id UUID PRIMARY KEY, hub_city UUID NOT NULL, kingdom_id UUID NOT NULL,
  good SMALLINT NOT NULL, side CHAR(1) NOT NULL, price DOUBLE PRECISION NOT NULL,
  qty DOUBLE PRECISION NOT NULL, created_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE entities (             -- caravanas, navios, exércitos...
  id UUID PRIMARY KEY, kind SMALLINT NOT NULL, kingdom_id UUID NOT NULL,
  shard BIGINT NOT NULL, state JSONB NOT NULL
);

CREATE TABLE scheduled_events (
  id UUID PRIMARY KEY, shard BIGINT NOT NULL, due_at TIMESTAMPTZ NOT NULL,
  kind SMALLINT NOT NULL, payload JSONB NOT NULL, idempotency_key TEXT UNIQUE NOT NULL,
  done_at TIMESTAMPTZ
);
CREATE INDEX ON scheduled_events (shard, due_at) WHERE done_at IS NULL;

CREATE TABLE event_log (            -- append-only
  seq BIGSERIAL PRIMARY KEY, shard BIGINT NOT NULL, at TIMESTAMPTZ NOT NULL,
  kind SMALLINT NOT NULL, payload JSONB NOT NULL
);

CREATE TABLE tech_mastery (
  city_id UUID, tech_id TEXT, mastery REAL NOT NULL, last_use TIMESTAMPTZ,
  PRIMARY KEY (city_id, tech_id)
);

CREATE TABLE religions (
  id UUID PRIMARY KEY, founder_kingdom UUID, name TEXT, dogmas JSONB, parent UUID, founded_at TIMESTAMPTZ
);
CREATE TABLE city_religion (city_id UUID, religion_id UUID, share REAL, PRIMARY KEY (city_id, religion_id));

-- Criação de itens únicos (docs/CRAFTING.md)
CREATE TABLE material_batches (
  id UUID PRIMARY KEY, class SMALLINT NOT NULL, source_id TEXT NOT NULL,
  origin GEOGRAPHY(Point, 4326), harvested_at TIMESTAMPTZ NOT NULL,
  props JSONB NOT NULL, defects JSONB NOT NULL, process_history JSONB NOT NULL,
  qty REAL NOT NULL, owner_city UUID REFERENCES cities(id), storage_building UUID
);

CREATE TABLE designs (
  id UUID PRIMARY KEY, kingdom_id UUID NOT NULL REFERENCES kingdoms(id),
  kind SMALLINT NOT NULL, name TEXT, params BYTEA NOT NULL, curves BYTEA NOT NULL,
  process_plan JSONB NOT NULL, created_by UUID, version INT NOT NULL DEFAULT 1,
  secret BOOLEAN NOT NULL DEFAULT true
);

CREATE TABLE design_knowledge (          -- quem conhece qual projeto e com que fidelidade
  kingdom_id UUID, design_id UUID, fidelity REAL NOT NULL, source TEXT NOT NULL,  -- own|reverse|espionage|license
  PRIMARY KEY (kingdom_id, design_id)
);

CREATE TABLE craftsmen (
  id UUID PRIMARY KEY, kingdom_id UUID, name TEXT, skills JSONB NOT NULL, quirks JSONB,
  school_id UUID, born_at TIMESTAMPTZ, died_at TIMESTAMPTZ
);

CREATE TABLE workshops (
  id UUID PRIMARY KEY, city_id UUID NOT NULL REFERENCES cities(id),
  kind SMALLINT NOT NULL, equipment JSONB NOT NULL, layout JSONB
);

CREATE TABLE styles (
  id UUID PRIMARY KEY, kingdom_id UUID, vector REAL[] NOT NULL, updated_at TIMESTAMPTZ
);

CREATE TABLE item_instances (
  id UUID PRIMARY KEY, genome_hash BYTEA NOT NULL, genome BYTEA NOT NULL,
  stats JSONB NOT NULL,                 -- valores reais (ocultos por padrão)
  owner_kingdom UUID, location JSONB, created_at TIMESTAMPTZ NOT NULL,
  designer_kingdom UUID, craftsman_id UUID, workshop_id UUID, name TEXT
);
CREATE INDEX ON item_instances (genome_hash);

CREATE TABLE item_history (
  id BIGSERIAL PRIMARY KEY, item_id UUID NOT NULL REFERENCES item_instances(id),
  at TIMESTAMPTZ NOT NULL, kind SMALLINT NOT NULL, payload JSONB NOT NULL   -- viagem, batalha, reparo, teste, ritual
);

CREATE TABLE item_knowledge (             -- o que cada reino sabe sobre as propriedades (incerteza)
  kingdom_id UUID, item_id UUID, estimate JSONB NOT NULL, uncertainty REAL NOT NULL,
  PRIMARY KEY (kingdom_id, item_id)
);

CREATE TABLE world_state (key TEXT PRIMARY KEY, value JSONB NOT NULL);  -- era atual, civ_index, versões de dados
```

## Protobuf (esboço, `packages/protocol/orbis.proto`)

```proto
syntax = "proto3";
package orbis.v1;

message LatLon { double lat = 1; double lon = 2; }

message Envelope { uint64 seq = 1; oneof body { Command command = 2; ServerMsg server = 3; } }

message Command {
  string request_id = 1;
  oneof kind {
    FoundCity found_city = 10; BuildCity build = 11; StartResearch research = 12;
    SendCaravan send_caravan = 13; PlaceOrder place_order = 14; MoveArmy move_army = 15;
    DeclareWar declare_war = 16; SetViewport viewport = 17;
  }
}

message MoveSegment {
  string entity_id = 1; LatLon from = 2; LatLon to = 3;
  double t_start_ms = 4; double speed_mps = 5; string path_id = 6;
}

// Criação de itens (docs/CRAFTING.md)
message DesignParam { string id = 1; double value = 2; string choice = 3; }
message CurvePoint { double x = 1; double y = 2; }
message DesignCurve { string id = 1; repeated CurvePoint points = 2; }
message CreateDesign { string kind = 1; string name = 2; repeated DesignParam params = 3; repeated DesignCurve curves = 4; }
message MaterialSlotRef { string slot = 1; string batch_id = 2; }
message StartCraft { string design_id = 1; string workshop_id = 2; string craftsman_id = 3; repeated MaterialSlotRef slots = 4; }
message ResolveCheckpoint { string craft_id = 1; string checkpoint_id = 2; repeated DesignParam params = 3; }

message ServerMsg {
  oneof kind {
    CommandResult result = 1; EntityUpsert upsert = 2; EntityRemove remove = 3;
    MoveSegment move = 4; TerritoryDelta territory = 5; WorldTick tick = 6;
    BattleReportRef battle = 7; EraChanged era = 8;
  }
}
```
