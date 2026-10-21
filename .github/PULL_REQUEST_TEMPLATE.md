## Summary

<!-- What changed, and why? Keep it focused on one behavior. -->

## Type

- [ ] Bug fix
- [ ] New feature
- [ ] Refactor / internal cleanup
- [ ] Documentation or packaging
- [ ] Other:

## Verification

<!-- List the checks you actually ran. Say why if something could not run. -->

- [ ] `dotnet build PaPerMySelf.csproj -c Release`
- [ ] Targeted behavior checks:
- [ ] `./tools/Publish-Local.ps1` (if packaging changed)

## Data, compatibility and boundaries

- [ ] No user data, credentials, plugin state, local SDK or build output included
- [ ] `StateStore` remains the only core state writer
- [ ] External writes still go through `PaperCommandService`
- [ ] Edge Capsule ownership and presenter/queue boundaries are preserved
- [ ] User-visible text updated in all required languages
- [ ] `CHANGELOG.md` / `CHANGELOG.zh.md` updated under `Unreleased` when appropriate

## Screenshots or recordings

<!-- For visible UI changes; redact personal data. -->
