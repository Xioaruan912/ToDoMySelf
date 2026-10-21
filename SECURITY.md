# Security policy

## Supported versions

Security fixes are applied to the current `main` development line and the latest 4.0.x preview. Older feature branches and local downstream builds are not supported.

## Reporting a vulnerability

Do not open a public issue for a vulnerability. Use GitHub's private vulnerability reporting on this repository (Security → Advisories → Report a vulnerability). If that is unavailable, contact the repository owner through their GitHub profile and include only enough detail to establish a private channel.

Please include:

- affected version or commit, Windows version and .NET runtime;
- a minimal reproduction, including whether WebView2, plugins, MCP or WebDAV are involved;
- the impact and any known workaround;
- whether the issue also affects upstream [snownico0722/PaperTodo](https://github.com/snownico0722/PaperTodo).

Do not include real WebDAV passwords, user `data.json`, note contents, images or plugin state in a report. Reproduce with synthetic data.

## Scope

In scope: the desktop application, local data persistence, plugin host boundaries, MCP bridge, WebDAV synchronization and the local packaging scripts.

Out of scope: vulnerabilities in third-party plugins, the Microsoft Edge WebView2 Runtime, NuGet dependencies without a demonstrated impact in this project, and social-engineering or physical-access scenarios.

## Handling

Reports are acknowledged as soon as practical, reproduced against the current development branch, and fixed with a regression check where feasible. Credit is given unless the reporter asks to remain anonymous. Downstream-only issues are fixed here; upstream issues are reported upstream rather than silently patched around.
