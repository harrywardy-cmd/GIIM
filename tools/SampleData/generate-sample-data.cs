// Generates FAKE test data for GIIM. No real people, serials or emails.
// Run from the repo root:  dotnet run tools/SampleData/generate-sample-data.cs
//
// Output (samples/):
//   people.csv                   1,500 tracked staff across 20 departments
//   departments-and-profiles.json  department -> onboarding profile (apps, hardware, groups)
//   application-catalogue.csv    managed app catalogue
//   legacy-asset-register.xlsx   mimics the current Excel sheet, including typical data-quality problems
//   sdp-assets-export.csv        mimics a ServiceDesk Plus asset export (partial, some conflicts)
//   intune-devices.json          mimics Graph /deviceManagement/managedDevices
//   EXPECTED-ISSUES.md           what reconciliation should find, so the importer can be verified

#:package ClosedXML@0.105.0
#:property TreatWarningsAsErrors=false
#:property AnalysisLevel=none
#:property PublishAot=false

using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;

var rng = new Random(20260928); // fixed seed so every run produces identical files
var outDir = Path.Combine(Directory.GetCurrentDirectory(), "samples");
Directory.CreateDirectory(outDir);
const string Domain = "giim-test.local";

// ---------------------------------------------------------------- departments
var departments = new (string Code, string Name, string Location)[]
{
    ("FIN", "Finance", "Support Office"), ("HR", "Human Resources", "Support Office"),
    ("IT", "Information Technology", "Support Office"), ("MKT", "Marketing", "Support Office"),
    ("LEG", "Legal", "Support Office"), ("PRC", "Procurement", "Support Office"),
    ("OPS", "Operations", "Support Office"), ("SAL", "Sales", "Support Office"),
    ("CS", "Customer Service", "Support Office"), ("PAY", "Payroll", "Support Office"),
    ("PROP", "Property", "Support Office"), ("SC", "Supply Chain", "Distribution Centre"),
    ("LOG", "Logistics", "Distribution Centre"), ("WHS", "Health & Safety", "Support Office"),
    ("RISK", "Risk & Compliance", "Support Office"), ("DATA", "Data & Analytics", "Support Office"),
    ("ECOM", "eCommerce", "Support Office"), ("MER", "Merchandise", "Support Office"),
    ("EXEC", "Executive", "Support Office"), ("FAC", "Facilities", "Distribution Centre"),
};

// ---------------------------------------------------------------- app catalogue
var apps = new (string Name, string Vendor, string Licence, int? Seats, string? OktaGroup)[]
{
    ("Microsoft 365 E3", "Microsoft", "PerUser", 1600, "LIC-M365-E3"),
    ("Microsoft Visio", "Microsoft", "PerUser", 60, "APP-Visio"),
    ("Microsoft Project", "Microsoft", "PerUser", 40, "APP-Project"),
    ("Power BI Pro", "Microsoft", "PerUser", 150, "APP-PowerBI-Pro"),
    ("Adobe Acrobat Pro", "Adobe", "PerUser", 200, "APP-Acrobat-Pro"),
    ("Adobe Creative Cloud", "Adobe", "PerUser", 25, "APP-Adobe-CC"),
    ("Xero", "Xero", "PerUser", 80, "APP-Xero"),
    ("SAP GUI", "SAP", "PerUser", 300, null),
    ("Salesforce", "Salesforce", "PerUser", 250, "APP-Salesforce"),
    ("Zendesk", "Zendesk", "PerUser", 120, "APP-Zendesk"),
    ("Workday", "Workday", "Site", null, "APP-Workday"),
    ("DocuSign", "DocuSign", "PerUser", 50, "APP-DocuSign"),
    ("Tableau Desktop", "Salesforce", "PerUser", 30, "APP-Tableau"),
    ("Snowflake", "Snowflake", "PerUser", 60, "APP-Snowflake"),
    ("Jira", "Atlassian", "PerUser", 200, "APP-Jira"),
    ("Confluence", "Atlassian", "PerUser", 400, "APP-Confluence"),
    ("Zoom", "Zoom", "PerUser", 300, "APP-Zoom"),
    ("Slack", "Salesforce", "PerUser", 500, "APP-Slack"),
    ("Manhattan WMS", "Manhattan", "PerUser", 150, null),
    ("Blue Yonder", "Blue Yonder", "PerUser", 40, null),
    ("Legal Matter Manager", "LexisNexis", "PerUser", 20, null),
    ("Figma", "Figma", "PerUser", 30, "APP-Figma"),
    ("Shopify Admin", "Shopify", "PerUser", 40, "APP-Shopify"),
    ("Azure Portal (Admin)", "Microsoft", "Free", null, "APP-Azure-Admins"),
    ("ServiceDesk Plus Technician", "ManageEngine", "PerUser", 60, "APP-SDP-Tech"),
};

string[] deptApps(string code) => code switch
{
    "FIN" => ["Xero", "SAP GUI", "Power BI Pro", "Adobe Acrobat Pro"],
    "PAY" => ["Workday", "Xero", "Adobe Acrobat Pro"],
    "HR" => ["Workday", "DocuSign", "Adobe Acrobat Pro"],
    "IT" => ["Jira", "Confluence", "ServiceDesk Plus Technician", "Azure Portal (Admin)", "Microsoft Visio"],
    "MKT" => ["Adobe Creative Cloud", "Figma", "Salesforce"],
    "LEG" => ["Legal Matter Manager", "DocuSign", "Adobe Acrobat Pro"],
    "PRC" => ["SAP GUI", "DocuSign"],
    "OPS" => ["Microsoft Project", "Power BI Pro"],
    "SAL" => ["Salesforce", "Zoom"],
    "CS" => ["Zendesk", "Salesforce"],
    "SC" => ["Blue Yonder", "SAP GUI", "Power BI Pro"],
    "LOG" => ["Manhattan WMS", "SAP GUI"],
    "DATA" => ["Power BI Pro", "Tableau Desktop", "Snowflake", "Jira"],
    "ECOM" => ["Shopify Admin", "Figma", "Jira", "Confluence"],
    "MER" => ["SAP GUI", "Blue Yonder", "Power BI Pro"],
    "EXEC" => ["Power BI Pro", "DocuSign", "Adobe Acrobat Pro"],
    "PROP" => ["Microsoft Project", "DocuSign"],
    "RISK" => ["Power BI Pro", "Confluence"],
    _ => ["Confluence"],
};

// ---------------------------------------------------------------- people
string[] firstNames = ["Olivia","Jack","Charlotte","Noah","Amelia","William","Isla","Oliver","Mia","Leo","Ava","Henry","Grace","Lucas","Chloe","Thomas","Zoe","James","Ruby","Harrison","Sophie","Ethan","Emily","Liam","Harper","Mason","Ella","Cooper","Matilda","Hunter","Priya","Wei","Aisha","Mateo","Hiroshi","Fatima","Arjun","Mei","Nikos","Tane"];
string[] lastNames = ["Smith","Jones","Williams","Brown","Wilson","Taylor","Nguyen","Johnson","Martin","White","Anderson","Walker","Thompson","Harris","Lee","Ryan","Robinson","Kelly","King","Chen","Singh","Patel","Kaur","Wang","Murphy","Campbell","Clarke","Mitchell","Young","Scott"];
string[] titles = ["Analyst","Senior Analyst","Coordinator","Specialist","Manager","Team Leader","Officer","Advisor","Associate","Lead"];

var people = new List<(string EmployeeId, string First, string Last, string Upn, string Dept, string Title, string Location, string Status, DateOnly Start, DateOnly? End, string Track)>();
var usedUpns = new HashSet<string>();
for (int i = 0; i < 1500; i++)
{
    var dept = departments[rng.Next(departments.Length)];
    var first = firstNames[rng.Next(firstNames.Length)];
    var last = lastNames[rng.Next(lastNames.Length)];
    var upnBase = $"{first}.{last}".ToLowerInvariant();
    var upn = $"{upnBase}@{Domain}";
    for (int n = 2; !usedUpns.Add(upn); n++) upn = $"{upnBase}{n}@{Domain}";

    var start = new DateOnly(2016, 1, 1).AddDays(rng.Next(0, 3900));
    var roll = rng.NextDouble();
    var (status, end) = roll switch
    {
        < 0.04 => ("Pending", (DateOnly?)null),                                          // starters not yet started
        < 0.07 => ("Leaving", new DateOnly(2026, 10, 1).AddDays(rng.Next(0, 30))),         // notice given
        < 0.12 => ("Left", new DateOnly(2025, 1, 1).AddDays(rng.Next(0, 600))),            // already gone
        _ => ("Active", null),
    };
    if (status == "Pending") start = new DateOnly(2026, 10, 5).AddDays(rng.Next(0, 45));
    var track = dept.Location == "Distribution Centre" && rng.NextDouble() < 0.6 ? "Light" : "Full";

    people.Add(($"E{100000 + i}", first, last, upn, dept.Code, titles[rng.Next(titles.Length)], dept.Location, status, start, end, track));
}

// Give each department a manager (first Active person found).
var managers = departments.ToDictionary(d => d.Code,
    d => people.FirstOrDefault(p => p.Dept == d.Code && p.Status == "Active").Upn ?? "");

// ---------------------------------------------------------------- devices (ground truth)
var models = new (string Make, string Model, string Category, string SerialPrefix, bool InIntune)[]
{
    ("HP", "EliteBook 840 G10", "Laptop", "5CG", true),
    ("HP", "EliteBook 860 G11", "Laptop", "5CD", true),
    ("Lenovo", "ThinkPad T14 Gen 4", "Laptop", "PF", true),
    ("Lenovo", "ThinkPad X1 Carbon Gen 11", "Laptop", "PW", true),
    ("Dell", "Latitude 7440", "Laptop", "DL", true),
    ("HP", "Elite Mini 800 G9", "Desktop", "8CC", true),
    ("Apple", "iPhone 15", "Phone", "F2L", true),
    ("Samsung", "Galaxy S24", "Phone", "R5C", true),
    ("Apple", "iPad (10th gen)", "Tablet", "DMP", true),
    ("Dell", "P2723DE", "Monitor", "CN0", false),   // monitors and docks never appear in Intune
    ("HP", "E24 G5", "Monitor", "3CM", false),
    ("HP", "USB-C Dock G5", "Dock", "5CH", false),
    ("Lenovo", "ThinkPad Universal USB-C Dock", "Dock", "ZK", false),
};

string NewSerial(string prefix) =>
    prefix + new string(Enumerable.Range(0, 10 - prefix.Length)
        .Select(_ => "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789"[rng.Next(34)]).ToArray());

var devices = new List<Device>();
int tag = 10000;
foreach (var p in people.Where(p => p.Status != "Pending"))
{
    // Full track: laptop + phone + usually monitor/dock. Light track: sometimes a shared tablet/phone.
    IEnumerable<int> kit = p.Track == "Full"
        ? [rng.Next(0, 5), rng.Next(6, 8), 9 + rng.Next(0, 2), 11 + rng.Next(0, 2)]
        : rng.NextDouble() < 0.4 ? [8] : [];
    foreach (var m in kit)
    {
        if (p.Track == "Full" && models[m].Category is "Monitor" or "Dock" && rng.NextDouble() < 0.25) continue;
        var purchase = p.Start.AddDays(rng.Next(-30, 900));
        if (purchase > new DateOnly(2026, 9, 1)) purchase = new DateOnly(2026, 9, 1).AddDays(-rng.Next(0, 200));
        devices.Add(new Device($"AT{tag++}", NewSerial(models[m].SerialPrefix), models[m].Make, models[m].Model,
            models[m].Category, models[m].InIntune, p.Upn, p.Dept, p.Location, purchase, purchase.AddYears(3),
            p.Status == "Left" ? (rng.NextDouble() < 0.5 ? "NotReturned" : "Returned") : "Assigned"));
    }
}
// Spare stock in the IT store room.
for (int i = 0; i < 120; i++)
{
    var m = models[rng.Next(models.Length)];
    var purchase = new DateOnly(2025, 6, 1).AddDays(rng.Next(0, 450));
    devices.Add(new Device($"AT{tag++}", NewSerial(m.SerialPrefix), m.Make, m.Model, m.Category, m.InIntune,
        null, null, "IT Store Room", purchase, purchase.AddYears(3), "InStock"));
}

// ---------------------------------------------------------------- Intune export
var intune = new List<object>();
var intuneOnly = new List<string>();
var staleInIntune = new List<string>();
foreach (var d in devices.Where(d => d.InIntune && d.State != "InStock"))
{
    var lastSync = d.State == "NotReturned" && rng.NextDouble() < 0.7
        ? new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(rng.Next(0, 300))   // stale
        : new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero).AddMinutes(-rng.Next(0, 60 * 24 * 14));
    if (lastSync.Year == 2025) staleInIntune.Add(d.Serial);
    intune.Add(IntuneRow(d.Serial, d.Make, d.Model, d.Category, d.Owner, lastSync));
}
// Devices enrolled in Intune that no register knows about.
for (int i = 0; i < 45; i++)
{
    var m = models[rng.Next(0, 9)];
    var owner = people[rng.Next(people.Count)];
    var serial = NewSerial(m.SerialPrefix);
    intuneOnly.Add(serial);
    intune.Add(IntuneRow(serial, m.Make, m.Model, m.Category, owner.Upn,
        new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero).AddHours(rng.Next(0, 170))));
}

object IntuneRow(string serial, string make, string model, string category, string? upn, DateTimeOffset lastSync) => new
{
    id = SeededGuid(),
    deviceName = category is "Phone" or "Tablet" ? $"{upn?.Split('@')[0] ?? "shared"}_{model.Replace(" ", "")}" : $"GIIM-{serial[^6..]}",
    serialNumber = serial,
    manufacturer = make,
    model,
    operatingSystem = category switch { "Phone" or "Tablet" => make == "Apple" ? "iOS" : "Android", _ => "Windows" },
    userPrincipalName = upn,
    complianceState = rng.NextDouble() < 0.93 ? "compliant" : "noncompliant",
    lastSyncDateTime = lastSync,
    enrolledDateTime = lastSync.AddDays(-rng.Next(30, 900)),
};

// ---------------------------------------------------------------- legacy Excel register (messy on purpose)
var excelDevices = devices.Where(_ => rng.NextDouble() < 0.72).ToList();
var excelIssues = new Dictionary<string, List<string>>
{
    ["Duplicate rows (same serial twice)"] = [],
    ["Blank serial number"] = [],
    ["Serial with stray spaces / lowercase"] = [],
    ["Make spelled differently (e.g. Hewlett-Packard)"] = [],
    ["Assigned to a person who has left"] = [],
};

using (var wb = new XLWorkbook())
{
    var ws = wb.Worksheets.Add("Asset Register");
    string[] headers = ["Asset Tag", "Serial No.", "Make", "Model", "Type", "Assigned To", "Department", "Location", "Purchase Date", "Warranty End", "Status", "Notes"];
    for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];

    int row = 2;
    foreach (var d in excelDevices)
    {
        var serial = d.Serial;
        var make = d.Make;
        var roll = rng.NextDouble();
        if (roll < 0.02) { excelIssues["Blank serial number"].Add(d.AssetTag); serial = ""; }
        else if (roll < 0.06) { excelIssues["Serial with stray spaces / lowercase"].Add(d.Serial); serial = $" {serial.ToLowerInvariant()} "; }
        if (make == "HP" && rng.NextDouble() < 0.15) { excelIssues["Make spelled differently (e.g. Hewlett-Packard)"].Add(d.Serial); make = "Hewlett-Packard"; }

        var owner = d.Owner is null ? "" : people.First(p => p.Upn == d.Owner) is var o ? $"{o.First} {o.Last}" : "";
        if (d.State == "NotReturned") excelIssues["Assigned to a person who has left"].Add(d.Serial);

        void WriteRow()
        {
            ws.Cell(row, 1).Value = d.AssetTag;
            ws.Cell(row, 2).Value = serial;
            ws.Cell(row, 3).Value = make;
            ws.Cell(row, 4).Value = d.Model;
            ws.Cell(row, 5).Value = d.Category;
            ws.Cell(row, 6).Value = owner;
            ws.Cell(row, 7).Value = d.Dept ?? "";
            ws.Cell(row, 8).Value = d.Location;
            // Real sheets mix true dates and typed text; reproduce that.
            if (rng.NextDouble() < 0.85) ws.Cell(row, 9).Value = d.Purchase.ToDateTime(TimeOnly.MinValue);
            else ws.Cell(row, 9).Value = d.Purchase.ToString("d/MM/yy", CultureInfo.InvariantCulture);
            ws.Cell(row, 10).Value = d.Warranty.ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, 11).Value = d.State switch { "InStock" => "Spare", "Returned" => "Returned", _ => "In Use" };
            ws.Cell(row, 12).Value = rng.NextDouble() < 0.05 ? "screen cracked - check warranty" : "";
            row++;
        }

        WriteRow();
        if (serial != "" && rng.NextDouble() < 0.015) { excelIssues["Duplicate rows (same serial twice)"].Add(d.Serial); WriteRow(); }
    }

    ws.Column(9).Style.DateFormat.Format = "dd/mm/yyyy";
    ws.Column(10).Style.DateFormat.Format = "dd/mm/yyyy";
    ws.Row(1).Style.Font.Bold = true;
    ws.SheetView.FreezeRows(1);
    ws.Columns().AdjustToContents();
    wb.SaveAs(Path.Combine(outDir, "legacy-asset-register.xlsx"));
}

// ---------------------------------------------------------------- SDP asset export (partial, with conflicts)
var sdpConflicts = new List<string>();
var sdp = new StringBuilder("Asset ID,Name,Product,Product Type,Serial Number,Asset Tag,Asset State,User,Department,Site\n");
int sdpId = 300000;
foreach (var d in devices.Where(_ => rng.NextDouble() < 0.5))
{
    var user = d.Owner ?? "";
    if (user != "" && rng.NextDouble() < 0.03)
    {
        user = people[rng.Next(people.Count)].Upn;   // SDP disagrees with the real owner
        sdpConflicts.Add(d.Serial);
    }
    var state = d.State switch { "InStock" => "In Store", "Returned" => "In Store", _ => "In Use" };
    sdp.AppendLine(string.Join(',', sdpId++, $"GIIM-{d.Serial[^6..]}", Csv($"{d.Make} {d.Model}"), d.Category,
        d.Serial, d.AssetTag, state, user, Csv(departments.FirstOrDefault(x => x.Code == d.Dept).Name ?? ""), Csv(d.Location)));
}

// ---------------------------------------------------------------- other files
var peopleCsv = new StringBuilder("EmployeeId,FirstName,LastName,UserPrincipalName,DepartmentCode,JobTitle,Location,Status,StartDate,EndDate,Track,ManagerUpn\n");
foreach (var p in people)
    peopleCsv.AppendLine(string.Join(',', p.EmployeeId, p.First, p.Last, p.Upn, p.Dept, Csv(p.Title), Csv(p.Location), p.Status,
        p.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), p.End?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
        p.Track, managers[p.Dept] == p.Upn ? "" : managers[p.Dept]));

var appsCsv = new StringBuilder("Name,Vendor,LicenceModel,TotalSeats,OktaGroup\n");
foreach (var a in apps) appsCsv.AppendLine(string.Join(',', Csv(a.Name), Csv(a.Vendor), a.Licence, a.Seats?.ToString(CultureInfo.InvariantCulture) ?? "", a.OktaGroup ?? ""));

var profiles = departments.Select(d => new
{
    department = new { d.Code, d.Name },
    profiles = new object[]
    {
        new
        {
            name = $"{d.Name} - Default",
            track = "Full",
            items = new object[]
            {
                new { type = "LicenceGroup", description = "Microsoft 365 E3", groupName = "LIC-M365-E3" },
                new { type = "OktaGroup", description = "Department group", groupName = $"DEPT-{d.Code}" },
                new { type = "Hardware", description = "Standard laptop", hardwareCategory = "Laptop" },
                new { type = "Hardware", description = "24\" monitor", hardwareCategory = "Monitor" },
                new { type = "Hardware", description = "USB-C dock", hardwareCategory = "Dock" },
                new { type = "Hardware", description = "Mobile phone", hardwareCategory = "Phone" },
            }
            .Concat(deptApps(d.Code).Select(a => (object)new
            {
                type = "Application",
                description = a,
                groupName = apps.First(x => x.Name == a).OktaGroup,
            }))
            .Append(new { type = "ManualTask", description = "Book building access card with Facilities" })
            .ToArray(),
        },
    }
    .Concat(d.Location == "Distribution Centre"
        ? [new
          {
              name = $"{d.Name} - Floor Staff",
              track = "Light",
              items = new object[]
              {
                  new { type = "OktaGroup", description = "Department group", groupName = $"DEPT-{d.Code}" },
                  new { type = "LicenceGroup", description = "Microsoft 365 F3", groupName = "LIC-M365-F3" },
                  new { type = "Application", description = "Manhattan WMS", groupName = (string?)null },
              },
          }]
        : Array.Empty<object>())
    .ToArray(),
});

var json = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
File.WriteAllText(Path.Combine(outDir, "people.csv"), peopleCsv.ToString());
File.WriteAllText(Path.Combine(outDir, "application-catalogue.csv"), appsCsv.ToString());
File.WriteAllText(Path.Combine(outDir, "departments-and-profiles.json"), JsonSerializer.Serialize(profiles, json));
File.WriteAllText(Path.Combine(outDir, "sdp-assets-export.csv"), sdp.ToString());
File.WriteAllText(Path.Combine(outDir, "intune-devices.json"), JsonSerializer.Serialize(new { value = intune }, json));

// ---------------------------------------------------------------- expected issues
var md = new StringBuilder();
md.AppendLine("# Expected reconciliation results (generated)");
md.AppendLine();
md.AppendLine("Generated by `tools/SampleData/generate-sample-data.cs` with a fixed seed. All data is fake.");
md.AppendLine("The importer and reconciliation report should find these problems.");
md.AppendLine();
md.AppendLine("| Check | Count |");
md.AppendLine("|---|---|");
md.AppendLine($"| People | {people.Count} |");
md.AppendLine($"| Devices (ground truth) | {devices.Count} |");
md.AppendLine($"| Rows in Excel register (devices) | {excelDevices.Count} |");
md.AppendLine($"| Devices in SDP export | {sdpId - 300000} |");
md.AppendLine($"| Devices in Intune export | {intune.Count} |");
foreach (var (issue, list) in excelIssues) md.AppendLine($"| Excel: {issue} | {list.Count} |");
md.AppendLine($"| SDP: assigned user conflicts with true owner | {sdpConflicts.Count} |");
md.AppendLine($"| Intune only (not in any register) | {intuneOnly.Count} |");
md.AppendLine($"| Intune: not synced for 90+ days (likely leaver kit) | {staleInIntune.Count} |");
md.AppendLine($"| Monitors/docks (never in Intune, so register-only is expected) | {devices.Count(d => !d.InIntune)} |");
md.AppendLine();
md.AppendLine("## Intune-only serials (first 10)");
foreach (var s in intuneOnly.Take(10)) md.AppendLine($"- `{s}`");
File.WriteAllText(Path.Combine(outDir, "EXPECTED-ISSUES.md"), md.ToString());

Console.WriteLine(md.ToString());

Guid SeededGuid() { var b = new byte[16]; rng.NextBytes(b); return new Guid(b); }

static string Csv(string value) => value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

record Device(string AssetTag, string Serial, string Make, string Model, string Category, bool InIntune,
    string? Owner, string? Dept, string Location, DateOnly Purchase, DateOnly Warranty, string State);
