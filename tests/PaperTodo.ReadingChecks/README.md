# Reading checks

Covers schema 1/2 migration, feedback attempts, progress/note associations, pause/snooze, manual next-step deduplication and title-only capsules and retained reading paper names. UI mode also checks retained drafts and wide-title editing and native compact-idle/full-title hover.

Run with the locally prepared SDK on Windows:

```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet-papertodo-dev"
& "$env:DOTNET_ROOT\dotnet.exe" run --project tests/PaperTodo.ReadingChecks -c Release
```

The default suite is offline: generation, real completion dates, tombstones, deterministic merge, persistence compatibility, DPAPI, fake-HTTP exclusive locks/conditional writes/retries, invalid documents, empty initialization recovery, and stalled response-body cancellation. It does not contact the user's WebDAV server or read existing Todo data.

`--render <png-path>` creates a WPF test controller in this test binary's output directory, resets its reading domain to synthetic examples and verifies narrow layout and live template-draft reconciliation. It never uses the development app's directory or original Desktop/Todo. The PNG contains synthetic data.

`--bootstrap <development-output-directory>` is an explicit integration/setup mode. Stop only the development app first. It reads a JSON object from standard input with ServerUrl, Username, Folder and Password, tests the connection, performs two locked synchronization round trips against the supplied account, then uses StateStore to save encrypted local settings. It writes the actual specified remote document; never pass passwords on a command line or store that input in source. This mode is not run by default.
