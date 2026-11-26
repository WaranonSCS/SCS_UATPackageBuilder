# SCS UAT Package Builder

An experimental Windows desktop tool for preparing controlled build workspaces for Microsoft Dynamics 365 Business Central AL extensions.

**Status: early development / not release-qualified.** No signed executable or production deployment is available. This repository contains generic application source and synthetic test fixtures only.

## Current scope

- C# / .NET 10 / Windows Forms; Windows inbox SQLite for the legacy mock registry.
- Local JSON connection profiles with per-tenant and per-environment identity, atomic replacement, duplicate checks and exclusive file locking.
- Draft source workspace selection: GitHub HTTPS URL, exact main commit SHA, isolated checkout, AL project discovery and per-profile JSON selection.
- Offline validation examples. Live BC authentication/API, private GitHub authentication, AL compilation, release qualification and deployment are not implemented or qualified.
- No credentials should be entered into or stored by this prototype. A saved profile is not evidence of authorization.

## Build and tests

Prerequisites: Windows, .NET 10 SDK and Git for Windows.

    dotnet build Builder.csproj
    dotnet run --project Builder.csproj -- --self-test
    dotnet run --project Builder.csproj -- --profile-self-test
    dotnet run --project Builder.csproj -- --workspace-self-test

Earlier local prototype tests passed. The newest workspace module compiled on the development machine, but runtime execution was blocked by Windows application trust policy; its tests are not yet qualified. Do not use this code for live releases. Respect the host's security settings.

## Privacy and contributions

Keep real connection profiles, tenant and customer data, private AL repositories, application packages, build outputs, credentials and local logs out of Git. The root ignore file uses an explicit source allowlist; review each new file before including it. Do not use forced adds to publish private files. Example repository names and identifiers are synthetic.

Licensed under MIT. This is an independent project, not an official Microsoft product.
