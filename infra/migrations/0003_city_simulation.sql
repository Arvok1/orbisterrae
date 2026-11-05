CREATE TABLE city_layouts (
  city_id UUID PRIMARY KEY REFERENCES cities(id) ON DELETE CASCADE,
  state JSONB NOT NULL,
  version BIGINT NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE construction_orders (
  id UUID PRIMARY KEY,
  city_id UUID NOT NULL REFERENCES cities(id) ON DELETE CASCADE,
  state JSONB NOT NULL,
  due_at TIMESTAMPTZ NOT NULL,
  done_at TIMESTAMPTZ,
  canceled_at TIMESTAMPTZ
);
CREATE INDEX construction_orders_city_queue ON construction_orders(city_id,due_at)
  WHERE done_at IS NULL AND canceled_at IS NULL;
