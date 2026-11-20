CREATE TABLE IF NOT EXISTS orders (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    reference varchar(40) NOT NULL UNIQUE,
    supplier varchar(120) NOT NULL,
    region varchar(24) NOT NULL CHECK (region IN ('BR-SC', 'US-EAST', 'EU-WEST')),
    status varchar(16) NOT NULL CHECK (status IN ('OPEN', 'DELIVERED', 'CANCELLED')),
    due_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_orders_due_at_status ON orders (due_at, status);
CREATE TABLE IF NOT EXISTS metric_snapshots (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    calculated_at timestamptz NOT NULL DEFAULT now(),
    open_orders integer NOT NULL CHECK (open_orders >= 0),
    late_orders integer NOT NULL CHECK (late_orders >= 0),
    due_soon_orders integer NOT NULL CHECK (due_soon_orders >= 0),
    healthy_orders integer NOT NULL CHECK (healthy_orders >= 0)
);
INSERT INTO orders (reference, supplier, region, status, due_at)
VALUES
    ('PO-BR-4001', 'Sul Components', 'BR-SC', 'OPEN', now() + interval '12 hours'),
    ('PO-US-4020', 'Atlantic Controls', 'US-EAST', 'OPEN', now() - interval '4 hours'),
    ('PO-EU-4011', 'Nordic Parts', 'EU-WEST', 'OPEN', now() + interval '4 days'),
    ('PO-BR-3988', 'Sul Components', 'BR-SC', 'DELIVERED', now() - interval '2 days')
ON CONFLICT (reference) DO NOTHING;
