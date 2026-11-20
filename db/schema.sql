-- Lakeshore Health facilities work orders
CREATE EXTENSION IF NOT EXISTS vector;

DROP TABLE IF EXISTS work_order_actions, work_orders, workers, vendors, locations, buildings, campuses CASCADE;

CREATE TABLE campuses (
    id          int PRIMARY KEY,
    name        text NOT NULL
);

CREATE TABLE buildings (
    id          int PRIMARY KEY,
    campus_id   int NOT NULL REFERENCES campuses(id),
    name        text NOT NULL,
    year_built  int NOT NULL
);

CREATE TABLE locations (
    id            int PRIMARY KEY,
    building_id   int NOT NULL REFERENCES buildings(id),
    floor         int NOT NULL,
    unit          text NOT NULL,          -- e.g. '4 West - Med/Surg'
    room          text NOT NULL,
    location_type text NOT NULL           -- Patient Room, ICU, Operating Room, ...
);

CREATE TABLE workers (
    id               int PRIMARY KEY,
    name             text NOT NULL,
    worker_category  text NOT NULL,       -- Plumber, Electrician, HVAC Technician, General Maintenance, Carpenter
    shift            text NOT NULL,       -- Day, Evening, Night
    campus_id        int NOT NULL REFERENCES campuses(id)
);

CREATE TABLE vendors (
    id     int PRIMARY KEY,
    name   text NOT NULL,
    trade  text NOT NULL
);

CREATE TABLE work_orders (
    id                  int PRIMARY KEY,
    wo_number           text NOT NULL UNIQUE,   -- WO-2026-004812
    title               text NOT NULL,
    description         text NOT NULL,
    priority            text NOT NULL,          -- P1 Emergency, P2 Urgent, P3 Routine, P4 Scheduled
    category            text NOT NULL,          -- Plumbing, Electrical, HVAC, General Maintenance, Carpentry
    subcategory         text NOT NULL,
    location_id         int NOT NULL REFERENCES locations(id),
    requested_by        text NOT NULL,          -- requesting department
    source              text NOT NULL,          -- Phone, Portal, Nurse Station, Preventive Maintenance
    status              text NOT NULL,          -- Open, In Progress, On Hold, Closed
    assigned_worker_id  int REFERENCES workers(id),
    opened_at           timestamptz NOT NULL,
    closed_at           timestamptz,
    embedding           vector(1536)            -- title+description embedding for the naive-RAG baseline
);

CREATE TABLE work_order_actions (
    id             bigint PRIMARY KEY,
    work_order_id  int NOT NULL REFERENCES work_orders(id),
    action_at      timestamptz NOT NULL,
    worker_id      int REFERENCES workers(id),
    action_type    text NOT NULL,   -- Assigned, Reassigned, Diagnosed, Parts Ordered, Awaiting Parts, Parts Received, Repair Completed, Note, Closed
    vendor_id      int REFERENCES vendors(id),
    hours_spent    numeric(5,2) NOT NULL DEFAULT 0,
    notes          text NOT NULL
);

CREATE INDEX ix_wo_opened   ON work_orders (opened_at);
CREATE INDEX ix_wo_category ON work_orders (category, opened_at);
CREATE INDEX ix_wo_location ON work_orders (location_id);
CREATE INDEX ix_woa_wo      ON work_order_actions (work_order_id, action_at);

-- Convenience view the query tool compiles against.
CREATE OR REPLACE VIEW work_order_facts AS
SELECT  wo.id,
        wo.wo_number,
        wo.title,
        wo.priority,
        wo.category,
        wo.subcategory,
        wo.requested_by,
        wo.source,
        wo.status,
        wo.opened_at,
        wo.closed_at,
        EXTRACT(EPOCH FROM (wo.closed_at - wo.opened_at)) / 86400.0                     AS days_to_close,
        (EXTRACT(HOUR FROM wo.opened_at AT TIME ZONE 'America/Chicago') NOT BETWEEN 7 AND 18) AS after_hours,
        c.name   AS campus,
        b.name   AS building,
        l.floor,
        l.unit,
        l.location_type,
        w.name             AS assigned_worker,
        w.worker_category  AS assigned_worker_category,
        (SELECT count(*) FROM work_order_actions a WHERE a.work_order_id = wo.id AND a.action_type = 'Reassigned')     AS reassignment_count,
        (SELECT count(*) FROM work_order_actions a WHERE a.work_order_id = wo.id AND a.action_type = 'Awaiting Parts') AS awaiting_parts_count,
        (SELECT v.name FROM work_order_actions a JOIN vendors v ON v.id = a.vendor_id
          WHERE a.work_order_id = wo.id AND a.action_type = 'Parts Ordered' ORDER BY a.action_at LIMIT 1)            AS parts_vendor,
        (SELECT coalesce(sum(a.hours_spent),0) FROM work_order_actions a WHERE a.work_order_id = wo.id)              AS labor_hours,
        (SELECT EXTRACT(EPOCH FROM (min(a.action_at) - wo.opened_at)) / 86400.0 FROM work_order_actions a
          WHERE a.work_order_id = wo.id AND a.action_type = 'Diagnosed' AND a.worker_id = wo.assigned_worker_id)      AS days_to_first_response,
        (SELECT EXTRACT(EPOCH FROM (max(a.action_at) FILTER (WHERE a.action_type = 'Parts Received')
                                  - min(a.action_at) FILTER (WHERE a.action_type = 'Parts Ordered'))) / 86400.0
           FROM work_order_actions a WHERE a.work_order_id = wo.id)                                                  AS days_waiting_on_parts
FROM work_orders wo
JOIN locations l ON l.id = wo.location_id
JOIN buildings b ON b.id = l.building_id
JOIN campuses  c ON c.id = b.campus_id
LEFT JOIN workers w ON w.id = wo.assigned_worker_id;
