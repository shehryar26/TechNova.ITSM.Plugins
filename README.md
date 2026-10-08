# TechNova ITSM – Dataverse Plugins

C# plugins for the **TechNova ITSM** practice project, built on Microsoft Dataverse / Power Platform. The project simulates an IT Service Management system with **asset assignment**, **asset audit history**, and **ticket SLA calculation**.

> Practice project built while learning Power Platform. The code is shared for learning and portfolio purposes.

## Plugins at a glance

| # | Plugin | Table | What it does | Status |
|---|---|---|---|---|
| 1 | `AssetAssignmentValidationPlugin` | `tn_assetassignments` | Validates a new assignment: blocks Retired/Disposed assets and assets that already have an active assignment | Done, tested |
| 2 | `AssetAssignmentProcessingPlugin` | `tn_assetassignments` | Updates the Asset status on assign/return and writes records to Asset Audit History | Implemented, Done, Tested |
| 3 | `TicketSlaCalculationPlugin` | `tn_ticket` | Finds the active SLA Policy for the ticket priority and sets the SLA Policy and SLA Due Date on the ticket | Implemented |

---

## Plugin 1: AssetAssignmentValidationPlugin

**Trigger:** `Create` on `tn_assetassignments` (PreValidation)

Runs before the assignment is saved and throws a user-facing error if any rule fails:

1. **Asset is required.** The `tn_asset` lookup must be filled.
2. **Asset must be assignable.** The asset's status (`tn_assetstatus`) must not be *Retired* or *Disposed*.
3. **No double assignment.** If the asset already has an assignment with status *Active*, the new assignment is rejected. The existing one must be returned first.

| Check | Error message (summary) |
|---|---|
| Asset missing | "Asset is required for an Asset Assignment." |
| Retired / Disposed | "This asset cannot be assigned because its status is Retired or Disposed." |
| Active assignment exists | "This asset already has an active assignment. Please return the existing assignment before assigning this asset again." |

---

## Plugin 2: AssetAssignmentProcessingPlugin

**Trigger:** `Create` and `Update` on `tn_assetassignments` (PostOperation)

Keeps the Asset record and the audit trail in sync with the assignment status.

| Assignment status | Asset status | Audit History record |
|---|---|---|
| **Active** (on create, or changed to Active) | Available → **Assigned** | Action = Assigned, New Employee = assignment employee |
| **Returned** (status changed to Returned) | Assigned → **Available** | Action = Returned, Previous Employee = assignment employee |
| **Transferred** | Not handled yet | Planned enhancement |

Every audit record also gets **Performed By** = the user who triggered the action and **Action Date** = current UTC time.
On create, **Assigned By** is auto-filled with the current user if it is empty.

Implementation notes:
- On `Update`, the plugin compares the **pre-image** and target status, so it only acts when the status really changes.
- The Update step uses a **filtering attribute** (`tn_assignstatus`) so it does not run on unrelated edits.
- Audit record names come from an **autonumber** primary column, so the plugin does not set a name.

---

## Plugin 3: TicketSlaCalculationPlugin

**Trigger:** `Create` and `Update` on `tn_ticket` (PreOperation, because it writes to the Target)

Calculates the SLA for a ticket based on its priority:

1. Reads the ticket **Priority** (`tn_priority`). On update, if it is not in the Target, the existing value is retrieved.
2. Maps the ticket priority to the SLA Policy priority.
3. Looks up the **active** SLA Policy (`tn_slapolicy`) for that priority. It throws an error if none, or more than one, is found.
4. Reads the policy's **Resolution Time** and **Time Unit** and calculates the due date from the current UTC time.
5. Sets **SLA Policy** (`tn_slapolicy`) and **SLA Due Date** (`tn_sladuedate`) on the ticket.

**Priority mapping**

| Ticket priority | Value | → | SLA Policy priority | Value |
|---|---|---|---|---|
| Critical | 833450003 | → | Urgent | 192350000 |
| High | 833450002 | → | Important | 192350001 |
| Medium | 833450001 | → | Medium | 192350002 |
| Low | 833450000 | → | Low | 192350003 |

**Time units:** Minutes = 126820000, Hours = 126820001, Days = 126820002.

**Validation errors:** missing priority, unsupported priority, no active policy, multiple active policies, resolution time ≤ 0, missing time unit.

---

## Data model (Dataverse)

| Table | Logical name | Columns used |
|---|---|---|
| Asset Assignment | `tn_assetassignments` | `tn_asset`, `tn_employee`, `tn_assignstatus`, `tn_assignedby` |
| Asset | `tn_asset` | `tn_assetstatus` |
| Asset Audit History | `tn_assetaudithistory` | `tn_asset`, `tn_action`, `tn_previousemployee`, `tn_newemployee`, `tn_performedby`, `tn_actiondate`, `tn_description` |
| Ticket | `tn_ticket` | `tn_priority`, `tn_slapolicy`, `tn_sladuedate` |
| SLA Policy | `tn_slapolicy` | `tn_policyname`, `tn_priority`, `tn_responsetime`, `tn_resolutiontime`, `tn_timeunit`, `tn_isactive` |

**Choice values used**

| Choice | Values |
|---|---|
| Asset Status | Available 126820000, Assigned 126820001, Maintenance 126820002, Lost 126820003, Retired 126820004, Disposed 126820005 |
| Assignment Status | Active 126820000, Returned 126820001, Transferred 126820002 |
| Audit Action | Assigned 126820001, Returned 126820002, Transferred 126820003 |

> Choice values depend on the solution publisher prefix and are specific to this environment. If you import the solution elsewhere, verify them first.

---

## Plugin step registration

Registered with the **Plugin Registration Tool** (PRT).

| Plugin | Message | Table | Stage | Mode | Notes |
|---|---|---|---|---|---|
| Validation | Create | `tn_assetassignments` | PreValidation | Sync | |
| Processing | Create | `tn_assetassignments` | PostOperation | Sync | |
| Processing | Update | `tn_assetassignments` | PostOperation | Sync | Filtering attribute: `tn_assignstatus`. Image named `Image` (Pre + Post) with `tn_asset`, `tn_employee`, `tn_assignstatus` |
| SLA Calculation | Create | `tn_ticket` | PreOperation | Sync | |
| SLA Calculation | Update | `tn_ticket` | PreOperation | Sync | Filtering attribute: `tn_priority` |

---

## Tech stack

- C#, .NET Framework class library (Dataverse plugin)
- Microsoft.CrmSdk.CoreAssemblies
- Dataverse, Plugin Registration Tool, Plug-in Trace Log for debugging

## Build and deploy

1. Open the solution in Visual Studio and restore NuGet packages.
2. **Strong-name key:** the `.snk` file is *not* included in this repo. Generate your own via *Project Properties → Signing → Sign the assembly → New key*.
3. Build in **Release** mode.
4. Open the Plugin Registration Tool and connect to your environment.
5. Register (or update) `TechNova.ITSM.Plugins.dll`, then register the steps listed above.
6. Add the assembly and steps to your solution.

## Testing

**Plugin 1 (validation)**
- Assign a **Retired** or **Disposed** asset → blocked.
- Create a second **Active** assignment for an asset that already has one → blocked.

**Plugin 2 (processing)**
- Create an **Active** assignment for an **Available** asset → Asset becomes **Assigned**, and an *Assigned* audit record is created.
- Change the assignment to **Returned** → Asset becomes **Available**, and a *Returned* audit record is created.

**Plugin 3 (SLA)**
- Create a ticket with each priority → SLA Policy and SLA Due Date are populated according to the active policy.
- Deactivate the policy for a priority and create a ticket → error about no active SLA Policy.

Enable *Plug-in trace log* (Power Platform admin center → Environment → Settings → Audit and logs) to see the trace output of each plugin.

## Roadmap

- [ ] Handle the **Transferred** status (previous/new employee in the audit record)
- [ ] SLA **response time** calculation (currently only resolution time is used)
- [ ] Business-hours aware SLA due dates
- [ ] Unit tests with a mocked organization service

## Author

[Your name] – [GitHub / LinkedIn link]
