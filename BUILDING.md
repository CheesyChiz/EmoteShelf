# Building and distributing Emote Shelf

This source snapshot includes the editable plugin, tests, relay, build/packaging script,
NoireLib PAP module and exact source archives for the bundled Luna / Penumbra.Api binaries.
No game assets, user mods, reports or credentials are required or included.

## Plugin

Use Windows, .NET SDK 10, a Dalamud API 15 development/runtime installation and
Penumbra 1.7.2.1 (API 5.19.2). Restore uses Dalamud.NET.Sdk 15.0.0 from NuGet.
`dotnet build EmoteShelf.csproj -c Release -p:PenumbraLibPath=C:/path/to/Penumbra`
builds a development DLL. If necessary also pass `-p:DalamudLibPath=C:/path/to/Dalamud/`.
The SDK's ordinary latest.zip is NOT the distribution artifact: it lacks our source bundle.

Run `dotnet run --project tests/PoseSlotTests.csproj` and
`python -B -m unittest discover -s relay -p "test_*.py"` for non-game tests.

For distribution run `./package-release.ps1 -PenumbraLibPath C:/path/to/Penumbra`.
It builds into a unique directory, checks dependency/source pins, and includes SOURCE.zip,
LICENSE and notices inside EmoteShelf-licensed.zip. Do not strip these from a release.
The included SOURCE.zip is the matching working-tree snapshot, including unpublished edits;
it does not depend on a mutable branch link. Publish the same archive/source together.
The script intentionally rejects different dependency binaries until their sources and pins
have been audited and updated. This gate is not a restriction on recipients building or
modifying the code: normal `dotnet build` works with their own rebuilt dependencies.

## Rebuilding bundled dependencies

Extract `third_party/source/Penumbra.Api.zip` to a directory named `Penumbra.Api`.
Extract `Luna.zip` into its sibling `Luna`, then extract `ImSharp.zip` into `Luna/ImSharp`.
Build `Penumbra.Api/Penumbra.Api.csproj -c Release` with the same SDK/Dalamud setup;
its project references build Luna and Luna.Generators. All source/build files from the
pinned revisions are included. NuGet restores their declared build dependencies.
Point Emote Shelf's PenumbraLibPath at the resulting API/Luna DLL directory.
Normal compiler/environment differences may change binary hashes; matching source is
provided, not a claim of bit-for-bit reproducibility across SDK versions.

## Relay

Relay Python source and deployment templates are under relay/. Existing production
deployments are not changed by building this plugin. When deploying a modified AGPL
relay, provide remote users a prominent free download of the exact deployed source,
including deployment/build instructions, under the same license. Do not point this
offer at a newer client branch or include production secrets/reports/mod packages.

## Provenance

The carrier feature and licensing integration were added to Emote Shelf on 2026-10-11.
Upstream NoireLib PAP sources are unchanged apart from possible line-ending normalization.
See THIRD-PARTY-NOTICES.md and third_party/NoireLib/ORIGIN.md for attribution and revisions.
