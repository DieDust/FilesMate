# Updates and fork builds

Official preview builds read the HTTPS feed configured in `src/FilesMate.App/Updates/UpdateTrust.cs`. The public key in that file is intended to be public. The RSA private signing key and deployment credentials are not part of the repository.

The client verifies the RSA-PSS/SHA-256 manifest signature and the installer's signed size and SHA-256 before running it. The release notes contain `zh-CN`, `en-US` and `ja-JP` translations. The updater verifies again before handing off to the installer. Users can disable automatic checks in Settings → About.

## Building

`scripts/package.ps1` builds the installer without a signing key. A locally built app retains the official update configuration unless you change it. Before distributing a fork, use a separate application identity, your own HTTPS feed and public key, and your own signing key. Otherwise an official update can replace your fork with the official build. Do not claim your fork's packages are official releases.

## Maintainer release sequence

1. Run the build/tests in an isolated checkout; update version and localized release notes.
2. Package and verify the installer. The update manifest signature is separate from Windows Authenticode signing.
3. Sign using `scripts/sign-update.ps1` with the protected key available only on the publisher's machine. Never put private keys in source, logs, issues or release assets.
4. Upload the versioned installer to a temporary name, verify its size and SHA-256 on the server, then publish it. Never replace an already published versioned installer.
5. Upload the signed manifest, preserve the prior manifest, verify that the live version has not changed concurrently, and atomically switch the manifest last.
6. Check the public signature with an older client, download the complete installer, verify its hash, and verify all release-note languages.

Operational server configuration and historical machine-local deployment records are deliberately excluded from the public source snapshot. GitHub Releases and the official feed distribute the same versioned installer.
