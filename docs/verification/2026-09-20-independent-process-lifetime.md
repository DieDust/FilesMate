# Independent process lifetime

## Incident and evidence

The user reported that FilesMate, its resident search host and QQ disappeared together around 21:04 local time. Neither FilesMate's crash log nor Windows Application Error/.NET Runtime records contained a corresponding crash. Process exit auditing was unavailable, so the exact exit status and historical job membership of those three processes cannot be recovered.

The development desktop's old log ended during shutdown at 21:03:54; its replacement process started at 21:04:11. The local installation script had started FilesMate directly from the tool's PowerShell session at 20:40. This timing and launch path support job termination as the cause, but are not a historical process-exit trace.

The launch defect was reproduced using the installed 1.1.83 platform library and harmless temporary processes:

| Launch | Immediate job flags | Membership in the development desktop's outer closing job |
|---|---|---|
| Ordinary process creation | `0x2800` | Yes |
| Existing `DetachedProcess.Start` | `0x2000` | Yes |
| Desktop Explorer delegation | `0x1800` | No |

`CREATE_BREAKAWAY_FROM_JOB` succeeded for the inner job while leaving the child in an outer job that disallowed breakaway. The search host's `--detached` marker then bypassed its actual membership check. The manager had no startup isolation, and its file activation path directly inherited process lifetime. The old launch helper also retried without breakaway when independent launch failed.

Microsoft documents that nested breakaway stops at the first ancestor that forbids it, and that querying a null job handle reports only the immediate job:

- https://learn.microsoft.com/windows/win32/procthread/nested-jobs
- https://learn.microsoft.com/windows/win32/api/jobapi2/nf-jobapi2-queryinformationjobobject

## Changes

Executable launches are created suspended with breakaway requested. Before any application code runs, membership is checked. A child that remains in a job is discarded while still suspended, and launch is delegated to the desktop shell. The ordinary inherited-launch retry has been removed. Shell targets delegate whenever the caller belongs to any job, because the immediate job's flags do not describe all ancestors.

Both normal application entry points check closing-job membership before becoming resident. The attempt marker prevents a loop but no longer bypasses the membership check. Startup isolation failure is reported and does not silently continue with the launcher's lifetime. The manager's UI test builds and one-shot unregister command retain their harness lifetimes; search preview workers and maintenance commands likewise remain inline.

File activation keeps the background STA worker added in 1.1.83. New manager windows, preview links and CompactMate activation use the shared independent launcher. The change adds no resident monitor, restart loop, timer or background polling.

## Regression coverage

The isolated fixture creates an ordinary child, an independently launched executable and a shell-launched executable. Its broker belongs to a non-breakaway outer closing job and one of three inner jobs: non-breakaway closing, breakaway closing, or breakaway without a closing flag. Tests verify membership, close the job handles, confirm the ordinary child exits, and confirm both independently launched children survive. Only fixture processes are terminated. This replaces the old test that assigned the test runner itself to a job and checked only a single breakaway job.

Desktop delegation requires an interactive Explorer session; those cases cannot execute on a headless runner. They were executed on the user's Windows desktop. Incident evidence and detailed test results are stored locally under `artifacts/incident-20260920-2105` and are not published.

## Local verification results

- All 726 application tests and 172 Windows platform tests passed, including the three nested-job lifetime cases.
- Packaged and installed 1.1.84 locally. Installer SHA-256: `F9359DE617E8E6D6AA98385FF585F531D93EC2FEDA7163EA085879743EBA56E0`.
- Verified 1,519 installed files against the package and preserved all 17 existing JSON settings files.
- Both actual installed processes were launched by Explorer. Enumerating job handles confirmed neither was in the development desktop's or command runner's jobs.
- Closed the actual manager normally. The same search process remained responsive with its hotkey registered. Reopened the manager and rechecked that it had no membership in the tool's jobs.
- This local repair has not been uploaded to GitHub Releases or the update server.
