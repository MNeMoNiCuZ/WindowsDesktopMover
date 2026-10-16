## Releases

- When asked to publish a release, update the source version automatically. Use the next patch version unless the change or user calls for a minor or major release.
- Keep `VersionPrefix` in `WindowsDesktopMover.csproj`, the identity version in `app.manifest`, the executable's embedded version, and the release tag in sync. Keep the executable filename `WindowsDesktopMover.exe`.
- Use `release.ps1` from a clean working tree to bump, commit, tag, and push. Use `-PrepareOnly` when including the version bump in a feature commit. GitHub Actions builds and publishes pushed `v*` tags and rejects mismatched versions.
- Verify the build and relevant behavior before publishing. Visually review menu changes in both themes, including submenus opening on either side.
