// Deterministic seed generator for Lakeshore Health facilities work orders.
// Planted story:
//   1. 2026-02-24: parts vendor switches from Great Lakes Plumbing Supply -> ACME Mechanical Supply.
//      ACME backorders the specialty fixtures used in North Campus Tower B (built 2019): sensor flush valves,
//      thermostatic mixing valves, medical-gas zone valves, backflow kits. Lead time goes from 1-2 days to 8-16 days.
//   2. 2026-04-01: After-Hours Dispatch SOP rev. 4 routes non-emergency after-hours calls to General Maintenance
//      on-call first. Plumbing tickets bounce: GM diagnoses "beyond scope", reassigns to a plumber next morning.
//   Red herrings: North Campus has the highest volume; Tower B's day plumber looks "slow".
using System.Text;
using Npgsql;
using NpgsqlTypes;

var conn = Environment.GetEnvironmentVariable("PG_CONN")
           ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=workorders";
var schemaPath = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "../../../../schema.sql");

var gen = new Generator(seed: 20261118);
gen.Generate();
Console.WriteLine($"Generated {gen.WorkOrders.Count:N0} work orders, {gen.Actions.Count:N0} actions.");

await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
await using (var cmd = new NpgsqlCommand(await File.ReadAllTextAsync(schemaPath), db)) await cmd.ExecuteNonQueryAsync();
await gen.WriteAsync(db);
Console.WriteLine("Done.");

record Campus(int Id, string Name);
record Building(int Id, int CampusId, string Name, int YearBuilt, double Weight);
record Location(int Id, int BuildingId, int Floor, string Unit, string Room, string Type);
record Worker(int Id, string Name, string Category, string Shift, int CampusId);
record Vendor(int Id, string Name, string Trade);
record WorkOrder(int Id, string Number, string Title, string Description, string Priority, string Category, string Subcategory,
    int LocationId, string RequestedBy, string Source, string Status, int? WorkerId, DateTime OpenedUtc, DateTime? ClosedUtc);
record WoAction(long Id, int WorkOrderId, DateTime AtUtc, int? WorkerId, string Type, int? VendorId, double Hours, string Notes);

class Generator(int seed)
{
    readonly Random R = new(seed);
    static readonly TimeZoneInfo Tz = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
    static readonly DateTime Start = new(2025, 6, 1);
    static readonly DateTime End = new(2026, 11, 14);
    static readonly DateTime Now = new(2026, 11, 16, 8, 0, 0);
    static readonly DateTime VendorSwitch = new(2026, 2, 24);
    static readonly DateTime SopRev4 = new(2026, 4, 1);

    public List<Campus> Campuses = [];
    public List<Building> Buildings = [];
    public List<Location> Locations = [];
    public List<Worker> Workers = [];
    public List<Vendor> Vendors = [];
    public List<WorkOrder> WorkOrders = [];
    public List<WoAction> Actions = [];
    long _actionId = 1;

    const int TowerB = 2;

    // ---------- reference data ----------
    void BuildReference()
    {
        Campuses = [new(1, "North Campus"), new(2, "South Campus"), new(3, "Riverside Campus")];
        Buildings =
        [
            new(1, 1, "Tower A", 1972, 0.13), new(2, 1, "Tower B", 2019, 0.20), new(3, 1, "Heart & Vascular Pavilion", 2008, 0.08),
            new(4, 1, "Central Plant", 1972, 0.03), new(5, 1, "North Medical Office Building", 1995, 0.05),
            new(6, 2, "South Hospital", 1988, 0.17), new(7, 2, "Women's & Children's Pavilion", 2011, 0.08), new(8, 2, "South Medical Office Building", 2001, 0.04),
            new(9, 3, "Riverside Hospital", 1965, 0.16), new(10, 3, "Riverside Outpatient Center", 2015, 0.06),
        ];
        var unitsByType = new Dictionary<string, string[]>
        {
            ["Patient Room"] = ["Med/Surg", "Telemetry", "Oncology", "Orthopedics", "Neuro", "Step-Down"],
            ["ICU"] = ["Medical ICU", "Surgical ICU", "Cardiac ICU", "Neuro ICU"],
            ["Operating Room"] = ["Surgical Services", "Cath Lab", "Endoscopy"],
            ["Emergency Department"] = ["Emergency Department"],
            ["Laboratory"] = ["Core Lab", "Microbiology"],
            ["Pharmacy"] = ["Inpatient Pharmacy"],
            ["Food Service"] = ["Main Kitchen", "Cafeteria"],
            ["Public Restroom"] = ["Lobby", "Public Corridor"],
            ["Mechanical Room"] = ["Mechanical"],
            ["Office"] = ["Administration", "Clinic Suite"],
        };
        int locId = 1;
        foreach (var b in Buildings)
        {
            var types = b.Name switch
            {
                "Central Plant" => new[] { "Mechanical Room", "Office" },
                var n when n.Contains("Medical Office") || n.Contains("Outpatient") => ["Office", "Public Restroom", "Laboratory", "Mechanical Room"],
                _ => unitsByType.Keys.ToArray(),
            };
            int floors = b.Name switch { "Tower B" => 12, "Tower A" => 9, "Central Plant" => 2, _ => 5 };
            for (int f = 1; f <= floors; f++)
            foreach (var t in types)
            {
                if (R.NextDouble() < 0.45 && t != "Patient Room") continue;
                var unitName = Pick(unitsByType[t]);
                int rooms = t == "Patient Room" ? 8 : 2;
                for (int i = 0; i < rooms; i++)
                    Locations.Add(new(locId++, b.Id, f, $"{f} {Pick(["North", "South", "East", "West"])} - {unitName}", $"{f}{R.Next(1, 60):00}", t));
            }
        }

        string[] first = ["Marcus", "Dana", "Luis", "Priya", "Tom", "Keisha", "Ray", "Megan", "Andre", "Hollis", "Sofia", "Gary", "Ines", "Derek", "Tasha", "Wes", "Nina", "Carl", "Joy", "Omar", "Bev", "Kurt", "Lena", "Sam", "Rita", "Victor", "Ellen", "Jamal", "Pat", "Frank", "Ana", "Hector", "Mia", "Ron", "Gwen", "Theo"];
        string[] last = ["Reyes", "Kowalski", "Patel", "Okafor", "Lindqvist", "Brennan", "Hughes", "Moreno", "Fischer", "Tran", "Sutton", "Delgado", "Novak", "Price", "Oyelaran", "Baird", "Castillo", "Mendez", "Walsh", "Hart"];
        int wid = 1;
        string NextName() => $"{first[(wid * 7) % first.Length]} {last[(wid * 3 + wid / 5) % last.Length]}";
        foreach (var c in Campuses)
        {
            void Add(string cat, string shift, int n) { for (int i = 0; i < n; i++) { Workers.Add(new(wid, NextName(), cat, shift, c.Id)); wid++; } }
            int scale = c.Id == 1 ? 2 : 1;
            Add("Plumber", "Day", 1 + scale); Add("Plumber", "Evening", 1); Add("Plumber", "Night", 1);
            Add("Electrician", "Day", 1 + scale); Add("Electrician", "Evening", 1);
            Add("HVAC Technician", "Day", 1 + scale); Add("HVAC Technician", "Evening", 1); Add("HVAC Technician", "Night", 1);
            Add("General Maintenance", "Day", 2 + scale); Add("General Maintenance", "Evening", 2); Add("General Maintenance", "Night", 2);
            Add("Carpenter", "Day", 1 + (scale - 1));
        }
        Vendors =
        [
            new(1, "Great Lakes Plumbing Supply", "Plumbing"), new(2, "ACME Mechanical Supply", "Plumbing"),
            new(3, "Northside Electric Supply", "Electrical"), new(4, "Polar HVAC Distributors", "HVAC"),
            new(5, "Lakeshore Building Supply", "General Maintenance"), new(6, "Lakeshore Building Supply", "Carpentry"),
        ];
    }

    // ---------- catalogs ----------
    record Sub(string Name, double Weight, double PartsProb, bool TowerBSpecialty, string[] Titles, string[] Descs, string[] Parts, string[]? Specialty = null);

    static readonly Dictionary<string, (double Weight, Sub[] Subs)> Catalog = new()
    {
        ["Plumbing"] = (0.20,
        [
            new("Toilet / Flush Valve", 0.22, 0.45, true,
                ["Toilet running continuously", "Toilet won't flush", "Flush valve leaking at base", "Sensor flush valve not activating"],
                ["Toilet in room {room} runs nonstop, patient complaint. Nursing asked for repair ASAP.", "Flushometer on toilet in {room} not activating, sensor light blinking red.", "Water pooling around base of toilet in {room}. EVS placed wet floor sign.", "Patient bathroom {room}: toilet won't flush, bucket flushing for now."],
                ["wax ring and closet bolts", "standard flushometer repair kit", "toilet flapper and fill valve"],
                ["Aqualine SV-200 solenoid module (AQL-SV200-SOL)", "Aqualine SV-200 diaphragm kit (AQL-SV200-DK)", "Aqualine SV-200 flush valve assembly (AQL-SV200-CMP)"]),
            new("Faucet / Fixture", 0.20, 0.40, true,
                ["Hand-wash sink faucet dripping", "Sink faucet no hot water", "Sensor faucet won't shut off", "Scrub sink low flow"],
                ["Hand hygiene sink in {room} dripping constantly; IP flagged for standing water.", "Sensor faucet at {room} runs and won't shut off.", "No hot water at sink in {room}, cold only.", "Low flow at scrub sink {room}, staff say it takes forever to fill."],
                ["faucet cartridge, single-lever", "vandal-resistant aerator", "standard sensor faucet solenoid"],
                ["Aqualine sensor faucet solenoid (AQL-SF-SOL)"]),
            new("Clogged Drain", 0.18, 0.05, false,
                ["Sink drain clogged", "Shower drain slow", "Floor drain backing up"],
                ["Sink in {room} draining very slowly, standing water.", "Shower in {room} backing up during patient shower.", "Floor drain in {room} gurgling and backing up."],
                ["drain snake rental"]),
            new("Leak", 0.15, 0.25, false,
                ["Water leak from ceiling", "Leak under sink", "Pipe leak in wall"],
                ["Water dripping from ceiling tile above {room}. Tile stained, bucket placed.", "Leak under sink cabinet in {room}, wet floor.", "Possible pipe leak behind wall in {room}, drywall soft and stained."],
                ["copper coupling and fittings", "PEX repair kit", "ceiling tiles"]),
            new("Water Heater / Mixing Valve", 0.08, 0.55, true,
                ["Water temperature too hot", "Lukewarm water on unit", "Mixing valve failure"],
                ["Water at {room} sink tested 128F, above safe limit. Risk of scalding.", "Unit reports lukewarm water at all sinks near {room}.", "Thermostatic mixing valve serving {unit} appears failed, temperature swinging."],
                ["standard mixing valve cartridge", "water heater thermostat"],
                ["ThermaMix TMV-50 mixing valve (TMX-TMV50)", "ThermaMix TMV-50 rebuild kit (TMX-TMV50-RK)"]),
            new("Medical Gas", 0.07, 0.60, true,
                ["Medical gas outlet leaking", "O2 outlet not latching", "Vacuum outlet low suction", "Zone valve alarm"],
                ["Oxygen outlet at headwall in {room} hissing when flowmeter connected.", "O2 outlet in {room} won't latch flowmeter. Respiratory notified.", "Medical vacuum at {room} low suction, staff using portable suction.", "Medical gas alarm panel shows zone valve fault for {unit}."],
                ["standard DISS O2 outlet", "vacuum outlet seal kit"],
                ["MedGuard MG-4 zone valve (MGD-MG4-ZV)", "MedGuard MG-4 O2 outlet latch assembly (MGD-MG4-O2LA)", "MedGuard MG-4 vacuum outlet rebuild kit (MGD-MG4-VRK)"]),
            new("Backflow Preventer", 0.05, 0.60, true,
                ["Backflow preventer failed test", "Backflow device leaking"],
                ["Annual test failed on backflow preventer serving {unit}. Must be repaired and retested.", "RPZ backflow device in {room} discharging water from relief valve."],
                ["standard backflow repair kit"],
                ["RPZ backflow repair kit (RPZ-RK-100)", "RPZ relief valve assembly (RPZ-RVA)"]),
            new("Sewer Backup", 0.05, 0.10, false,
                ["Sewage backup", "Toilet overflowing with sewage"],
                ["Sewage backing up through floor drain in {room}. Area closed.", "Toilet overflowing in {room}, sewage on floor. EVS on scene."],
                ["jetter rental"]),
        ]),
        ["HVAC"] = (0.22,
        [
            new("Room Too Hot", 0.35, 0.15, false, ["Room too hot", "Patient room warm"], ["Room {room} reading 79F, patient complaints.", "Unit says {room} is stuffy and warm all shift."], ["thermostat", "actuator"]),
            new("Room Too Cold", 0.25, 0.15, false, ["Room too cold", "Cold air blowing"], ["Room {room} at 64F, patient asking for blankets.", "Cold air pouring from diffuser in {room}."], ["VAV actuator"]),
            new("Pressure Alarm", 0.15, 0.20, false, ["Isolation room pressure alarm", "Negative pressure alarm"], ["Negative pressure monitor alarming at {room}.", "Isolation room {room} lost pressure differential."], ["pressure sensor"]),
            new("Noise / Vibration", 0.15, 0.10, false, ["Loud noise from vent", "Rattling diffuser"], ["Loud rattling from ceiling vent in {room}.", "Humming noise from air handler near {room}."], ["fan belt"]),
            new("Filter / PM", 0.10, 0.0, false, ["Filter change", "AHU preventive maintenance"], ["Scheduled filter change for units serving {unit}.", "Quarterly PM on air handler serving {unit}."], []),
        ]),
        ["Electrical"] = (0.18,
        [
            new("Lighting", 0.45, 0.20, false, ["Light out", "Flickering light"], ["Overhead light out in {room}.", "Fluorescent fixture flickering in {room}."], ["LED driver", "lamp"]),
            new("Outlet / Power", 0.30, 0.15, false, ["Outlet not working", "Red outlet dead"], ["Wall outlet near bed in {room} not working.", "Emergency (red) outlet in {room} has no power."], ["receptacle"]),
            new("Nurse Call", 0.15, 0.20, false, ["Nurse call not working", "Call light stuck on"], ["Nurse call button in {room} not registering at station.", "Call light over {room} stuck on."], ["nurse call pillow speaker"]),
            new("Generator / Transfer Test", 0.10, 0.0, false, ["Monthly generator test", "ATS test"], ["Monthly generator load test for {unit}.", "Transfer switch test serving {unit}."], []),
        ]),
        ["General Maintenance"] = (0.30,
        [
            new("Furniture / Fixtures", 0.25, 0.15, false, ["Broken chair", "Bed rail loose", "Cabinet door broken"], ["Recliner in {room} won't recline.", "Cabinet door in {room} hanging off hinge.", "Overbed table wobbly in {room}."], ["hinge", "caster"]),
            new("Doors / Locks", 0.20, 0.20, false, ["Door won't latch", "Badge reader issue", "Door closer broken"], ["Door to {room} won't latch.", "Door closer on {room} slamming.", "Med room door at {unit} not closing."], ["door closer", "strike plate"]),
            new("Walls / Ceiling", 0.20, 0.05, false, ["Ceiling tile stained", "Wall damage"], ["Stained ceiling tile above {room}.", "Hole in drywall in {room} from bed."], ["ceiling tiles"]),
            new("Moves / Setup", 0.20, 0.0, false, ["Move furniture", "Event setup"], ["Move two recliners into {room}.", "Set up tables in {unit} conference room."], []),
            new("Signage / Misc", 0.15, 0.05, false, ["Replace signage", "Hang whiteboard"], ["Replace room sign for {room}.", "Hang whiteboard in {room}."], ["signage"]),
        ]),
        ["Carpentry"] = (0.10,
        [
            new("Casework", 0.5, 0.30, false, ["Countertop damage", "Cabinet repair"], ["Countertop laminate peeling at {room}.", "Nurse station cabinet drawer broken at {unit}."], ["laminate", "drawer slides"]),
            new("Flooring", 0.5, 0.25, false, ["Floor tile cracked", "Trip hazard"], ["Cracked floor tile in {room}, trip hazard.", "Flooring seam lifting at {room}."], ["VCT tiles"]),
        ]),
    };

    // ---------- generation ----------
    public void Generate()
    {
        BuildReference();
        int id = 1;
        for (var day = Start; day <= End; day = day.AddDays(1))
        {
            double seasonal = 1 + 0.08 * Math.Sin((day.DayOfYear - 100) / 365.0 * 2 * Math.PI);
            double weekday = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 0.6 : 1.1;
            int n = Poisson(24.5 * seasonal * weekday);
            for (int i = 0; i < n; i++) MakeWorkOrder(id++, day);
        }
    }

    void MakeWorkOrder(int id, DateTime day)
    {
        var category = WeightedPick(Catalog.Keys.ToArray(), k => Catalog[k].Weight * (k == "HVAC" && day.Month is 6 or 7 or 8 ? 1.4 : 1));
        var building = WeightedPick(Buildings.ToArray(), b => b.Weight * (category == "Plumbing" && b.Id == TowerB ? 1.7 : 1));
        var sub = WeightedPick(Catalog[category].Subs, s => s.Weight);
        var candidates = Locations.Where(l => l.BuildingId == building.Id).ToArray();
        if (sub.Name == "Medical Gas") { var mg = candidates.Where(l => l.Type is "Patient Room" or "ICU" or "Operating Room" or "Emergency Department").ToArray(); if (mg.Length > 0) candidates = mg; }
        var loc = Pick(candidates);
        var campusId = building.CampusId;

        // hour of day: 72% business hours
        int hour = R.NextDouble() < 0.72 ? R.Next(7, 19) : Pick(new[] { 19, 20, 21, 22, 23, 0, 1, 2, 3, 4, 5, 6 });
        var openedLocal = day.AddHours(hour).AddMinutes(R.Next(60));
        bool afterHours = hour is < 7 or > 18;

        string priority = sub.Name switch
        {
            "Medical Gas" or "Sewer Backup" or "Pressure Alarm" => R.NextDouble() < 0.6 ? "P1 Emergency" : "P2 Urgent",
            "Filter / PM" or "Generator / Transfer Test" or "Moves / Setup" => "P4 Scheduled",
            _ => WeightedPick(new[] { "P1 Emergency", "P2 Urgent", "P3 Routine", "P4 Scheduled" }, p => p switch { "P1 Emergency" => 0.05, "P2 Urgent" => 0.30, "P3 Routine" => 0.55, _ => 0.10 }),
        };
        string source = priority == "P4 Scheduled" && sub.PartsProb == 0 ? "Preventive Maintenance"
            : WeightedPick(new[] { "Phone", "Portal", "Nurse Station" }, s => s switch { "Phone" => 0.3, "Portal" => 0.45, _ => 0.25 });
        string requestedBy = loc.Type switch
        {
            "Patient Room" or "ICU" => $"Nursing - {loc.Unit.Split(" - ")[1]}",
            "Operating Room" => "Surgical Services",
            "Emergency Department" => "Emergency Department",
            "Laboratory" => "Laboratory",
            "Pharmacy" => "Pharmacy",
            "Food Service" => "Food & Nutrition",
            "Public Restroom" => "Environmental Services",
            _ => source == "Preventive Maintenance" ? "Facilities PM" : "Facilities",
        };

        var roomLabel = $"{building.Name} {loc.Room}";
        var title = Pick(sub.Titles);
        var desc = Pick(sub.Descs).Replace("{room}", roomLabel).Replace("{unit}", loc.Unit);

        // ---------- action timeline (local time) ----------
        var acts = new List<(DateTime At, int? Worker, string Type, int? Vendor, double Hours, string Notes)>();
        var t = openedLocal;
        Worker trade = PickWorker(category == "General Maintenance" ? "General Maintenance" : TradeFor(category), campusId, ShiftFor(t), building.Id);
        int? currentWorker;

        bool sopMisroute = category == "Plumbing" && afterHours && priority != "P1 Emergency" && openedLocal >= SopRev4;
        if (sopMisroute)
        {
            var gm = PickWorker("General Maintenance", campusId, "Night", building.Id);
            t = t.AddMinutes(R.Next(5, 30));
            acts.Add((t, gm.Id, "Assigned", null, 0, "After-hours call routed to General Maintenance on-call per After-Hours Dispatch SOP."));
            t = t.AddHours(0.5 + R.NextDouble() * 2.5);
            acts.Add((t, gm.Id, "Diagnosed", null, 0.5 + Math.Round(R.NextDouble(), 1), Pick(new[]
            {
                "Beyond GM scope - requires licensed plumber. Shut off supply where possible and made area safe.",
                "Inspected. Needs plumber; GM not certified for this fixture. Placed sign, notified charge nurse.",
                "Unable to repair - specialty fixture, plumber needed. Bucket placed under leak.",
            })));
            // next morning day shift picks it up
            var morning = t.Date.AddDays(t.Hour >= 7 ? 1 : 0).AddHours(7);
            t = morning.AddHours(R.NextDouble() * 6) .AddDays(R.NextDouble() < 0.35 ? 1 : 0);
            trade = PickWorker("Plumber", campusId, "Day", building.Id);
            acts.Add((t, trade.Id, "Reassigned", null, 0, "Reassigned from General Maintenance to Plumbing."));
            currentWorker = trade.Id;
        }
        else
        {
            if (category == "Plumbing" && afterHours) trade = PickWorker("Plumber", campusId, "Night", building.Id);
            t = t.AddMinutes(R.Next(5, 40));
            acts.Add((t, trade.Id, "Assigned", null, 0, afterHours && category == "Plumbing" ? "Paged on-call plumber." : "Assigned."));
            currentWorker = trade.Id;
            if (category != "Plumbing" && R.NextDouble() < 0.03)
            {
                t = t.AddHours(2 + R.NextDouble() * 10);
                var other = PickWorker(TradeFor(category), campusId, ShiftFor(t), building.Id);
                acts.Add((t, other.Id, "Reassigned", null, 0, "Reassigned - original tech pulled to higher priority call."));
                currentWorker = other.Id;
            }
        }

        // response delay by priority
        double respondH = priority switch { "P1 Emergency" => 0.2 + R.NextDouble() * 0.8, "P2 Urgent" => 1 + R.NextDouble() * 4, "P3 Routine" => 4 + R.NextDouble() * 26, _ => 24 + R.NextDouble() * 96 };
        t = WorkHours(t.AddHours(respondH), priority);
        double diagHours = Math.Round(0.3 + R.NextDouble() * 1.2, 1);
        bool partsNeeded = R.NextDouble() < sub.PartsProb * (category == "Plumbing" && building.Id == TowerB && sub.TowerBSpecialty ? 1.35 : 1);
        bool specialtyPart = category == "Plumbing" && building.Id == TowerB && sub.Specialty is { Length: > 0 } && R.NextDouble() < 0.85;
        string part = specialtyPart ? Pick(sub.Specialty!) : sub.Parts.Length > 0 ? Pick(sub.Parts) : "";
        acts.Add((t, currentWorker, "Diagnosed", null, diagHours, partsNeeded ? $"Diagnosed. Needs {part}." : Pick(new[] { "On site, diagnosed.", "Inspected, starting repair.", "Diagnosed issue." })));

        if (partsNeeded)
        {
            int vendorId = category switch
            {
                "Plumbing" => openedLocal >= VendorSwitch ? 2 : 1,
                "Electrical" => 3, "HVAC" => 4, "Carpentry" => 6, _ => 5,
            };
            bool backorder = vendorId == 2 && specialtyPart; // ACME backorders Exhibit B (non-stocked specialty) items
            t = t.AddMinutes(R.Next(20, 120));
            bool inStock = !backorder && R.NextDouble() < 0.5;
            if (inStock)
            {
                acts.Add((t, currentWorker, "Note", null, 0, $"Pulled {part} from shop stock."));
            }
            else
            {
                acts.Add((t, currentWorker, "Parts Ordered", vendorId, 0.2, $"Ordered {part} from {Vendors[vendorId - 1].Name}."));
                double leadDays = vendorId switch
                {
                    1 => 0.8 + R.NextDouble() * 1.2,
                    2 when backorder => 10 + R.NextDouble() * 10,
                    2 => 1.5 + R.NextDouble() * 1.5,
                    _ => 1 + R.NextDouble() * 2,
                };
                var received = t.AddDays(leadDays);
                if (backorder)
                {
                    var check = t.AddDays(2 + R.NextDouble());
                    int k = 0;
                    while (check < received.AddDays(-1))
                    {
                        acts.Add((check, currentWorker, "Awaiting Parts", vendorId, 0, k++ == 0
                            ? $"ACME confirms {part} is backordered. No ETA yet."
                            : Pick(new[] { $"Still awaiting {part} from ACME. ETA pushed again.", $"Followed up with ACME rep - {part} still on backorder.", $"Unit called asking for status. Waiting on ACME for {part}.", "ACME says shipment from manufacturer delayed. Temporary fix holding." })));
                        check = check.AddDays(2.5 + R.NextDouble() * 1.5);
                    }
                    if (sub.Name is "Medical Gas" or "Toilet / Flush Valve" && R.NextDouble() < 0.4)
                        acts.Add((t.AddHours(1), currentWorker, "Note", null, 0.5, "Room taken out of service pending part."));
                }
                else if (leadDays > 2)
                {
                    acts.Add((t.AddDays(1.2), currentWorker, "Awaiting Parts", vendorId, 0, $"Waiting on {part}."));
                }
                t = WorkHours(received, "P3 Routine");
                acts.Add((t, currentWorker, "Parts Received", vendorId, 0, $"Received {part}."));
                t = WorkHours(t.AddHours(R.NextDouble() * 20), priority);
            }
        }

        double repairH = Math.Round(0.4 + R.NextDouble() * (category == "Plumbing" ? 2.6 : 2.0), 1);
        t = t.AddHours(repairH + R.NextDouble());
        acts.Add((t, currentWorker, "Repair Completed", null, repairH, Pick(new[] { "Repaired and tested.", "Repair complete, area cleaned.", "Completed. Verified with unit staff.", "Fixed and tested, OK." })));
        t = t.AddHours(R.NextDouble() * 4);
        acts.Add((t, currentWorker, "Closed", null, 0, "Work order closed."));

        // ---------- materialize, truncate to "now" ----------
        acts.Sort((a, b) => a.At.CompareTo(b.At));
        var kept = acts.Where(a => a.At <= Now).ToList();
        bool closed = kept.Count == acts.Count;
        string status = closed ? "Closed" : kept.Count == 0 ? "Open" : kept[^1].Type is "Awaiting Parts" or "Parts Ordered" ? "On Hold" : "In Progress";
        var opened = ToUtc(openedLocal);
        if (opened > ToUtc(Now)) return;

        WorkOrders.Add(new(id, $"WO-{openedLocal.Year}-{id:000000}", title, desc, priority, category, sub.Name, loc.Id, requestedBy, source, status,
            kept.Count > 0 ? currentWorker : null, opened, closed ? ToUtc(acts[^1].At) : null));
        foreach (var a in kept)
            Actions.Add(new(_actionId++, id, ToUtc(a.At), a.Worker, a.Type, a.Vendor, a.Hours, a.Notes));
    }

    // ---------- helpers ----------
    static string TradeFor(string category) => category switch
    {
        "Plumbing" => "Plumber", "Electrical" => "Electrician", "HVAC" => "HVAC Technician", "Carpentry" => "Carpenter", _ => "General Maintenance",
    };
    static string ShiftFor(DateTime local) => local.Hour switch { >= 7 and < 15 => "Day", >= 15 and < 23 => "Evening", _ => "Night" };

    Worker PickWorker(string category, int campusId, string shift, int buildingId)
    {
        var pool = Workers.Where(w => w.Category == category && w.CampusId == campusId && w.Shift == shift).ToArray();
        if (pool.Length == 0) pool = Workers.Where(w => w.Category == category && w.CampusId == campusId && w.Shift == "Day").ToArray();
        if (pool.Length == 0) pool = Workers.Where(w => w.Category == category && w.CampusId == campusId).ToArray();
        // Tower B day plumbing mostly goes to the first North day plumber (the "slow tech" red herring)
        if (category == "Plumber" && buildingId == TowerB && shift == "Day" && R.NextDouble() < 0.75) return pool[0];
        return Pick(pool);
    }

    // non-emergency work happens 7:00-17:00 weekdays
    static DateTime WorkHours(DateTime t, string priority)
    {
        if (priority is "P1 Emergency" or "P2 Urgent") return t;
        while (t.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || t.Hour < 7 || t.Hour >= 17)
        {
            if (t.Hour >= 17 || t.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) t = t.Date.AddDays(1).AddHours(7).AddMinutes(t.Minute);
            else t = t.Date.AddHours(7).AddMinutes(t.Minute);
        }
        return t;
    }

    static DateTime ToUtc(DateTime local)
    {
        var l = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (Tz.IsInvalidTime(l)) l = l.AddHours(1); // DST spring-forward gap
        return TimeZoneInfo.ConvertTimeToUtc(l, Tz);
    }
    T Pick<T>(IReadOnlyList<T> items) => items[R.Next(items.Count)];
    T WeightedPick<T>(IReadOnlyList<T> items, Func<T, double> w)
    {
        double total = items.Sum(w), x = R.NextDouble() * total;
        foreach (var i in items) { x -= w(i); if (x <= 0) return i; }
        return items[^1];
    }
    int Poisson(double lambda) { double l = Math.Exp(-lambda), p = 1; int k = 0; do { k++; p *= R.NextDouble(); } while (p > l); return k - 1; }

    // ---------- write ----------
    public async Task WriteAsync(NpgsqlConnection db)
    {
        await Copy(db, "COPY campuses (id, name) FROM STDIN (FORMAT BINARY)", Campuses, (w, c) => { w.Write(c.Id); w.Write(c.Name); });
        await Copy(db, "COPY buildings (id, campus_id, name, year_built) FROM STDIN (FORMAT BINARY)", Buildings, (w, b) => { w.Write(b.Id); w.Write(b.CampusId); w.Write(b.Name); w.Write(b.YearBuilt); });
        await Copy(db, "COPY locations (id, building_id, floor, unit, room, location_type) FROM STDIN (FORMAT BINARY)", Locations, (w, l) => { w.Write(l.Id); w.Write(l.BuildingId); w.Write(l.Floor); w.Write(l.Unit); w.Write(l.Room); w.Write(l.Type); });
        await Copy(db, "COPY workers (id, name, worker_category, shift, campus_id) FROM STDIN (FORMAT BINARY)", Workers, (w, x) => { w.Write(x.Id); w.Write(x.Name); w.Write(x.Category); w.Write(x.Shift); w.Write(x.CampusId); });
        await Copy(db, "COPY vendors (id, name, trade) FROM STDIN (FORMAT BINARY)", Vendors, (w, v) => { w.Write(v.Id); w.Write(v.Name); w.Write(v.Trade); });
        await Copy(db, "COPY work_orders (id, wo_number, title, description, priority, category, subcategory, location_id, requested_by, source, status, assigned_worker_id, opened_at, closed_at) FROM STDIN (FORMAT BINARY)",
            WorkOrders, (w, o) =>
            {
                w.Write(o.Id); w.Write(o.Number); w.Write(o.Title); w.Write(o.Description); w.Write(o.Priority); w.Write(o.Category); w.Write(o.Subcategory);
                w.Write(o.LocationId); w.Write(o.RequestedBy); w.Write(o.Source); w.Write(o.Status);
                if (o.WorkerId is int wid) w.Write(wid); else w.WriteNull();
                w.Write(DateTime.SpecifyKind(o.OpenedUtc, DateTimeKind.Utc), NpgsqlDbType.TimestampTz);
                if (o.ClosedUtc is DateTime c) w.Write(DateTime.SpecifyKind(c, DateTimeKind.Utc), NpgsqlDbType.TimestampTz); else w.WriteNull();
            });
        await Copy(db, "COPY work_order_actions (id, work_order_id, action_at, worker_id, action_type, vendor_id, hours_spent, notes) FROM STDIN (FORMAT BINARY)",
            Actions, (w, a) =>
            {
                w.Write(a.Id); w.Write(a.WorkOrderId); w.Write(DateTime.SpecifyKind(a.AtUtc, DateTimeKind.Utc), NpgsqlDbType.TimestampTz);
                if (a.WorkerId is int wid) w.Write(wid); else w.WriteNull();
                w.Write(a.Type);
                if (a.VendorId is int vid) w.Write(vid); else w.WriteNull();
                w.Write((decimal)Math.Round(a.Hours, 2), NpgsqlDbType.Numeric); w.Write(a.Notes);
            });
    }

    static async Task Copy<T>(NpgsqlConnection db, string sql, IEnumerable<T> rows, Action<NpgsqlBinaryImporter, T> write)
    {
        await using var w = await db.BeginBinaryImportAsync(sql);
        foreach (var r in rows) { await w.StartRowAsync(); write(w, r); }
        await w.CompleteAsync();
    }
}
