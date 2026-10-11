# Licensing and source

Emote Shelf: Copyright (c) 2026 CheesyChiz. Distributed under GNU AGPL version 3; see LICENSE. No warranty is provided, to the extent permitted by law. Recipients may use, modify and redistribute under that license.

## Included NoireLib PAP module

Copyright (c) 2026 Aspher0 and NoireLib contributors.
Source: https://github.com/Aspher0/NoireLib
Pinned revision: 4d50c3495855d76a1a7b0666c98844bbbd099340 (NoireLib 2.0.1 package source revision).
Included directory: NoireLib/Animations/PapFormat, copied into third_party/NoireLib/PapFormat.
License: GNU AGPL version 3, supplied in LICENSE and third_party/NoireLib/LICENSE.

Imported 2026-10-11. The module is compiled directly into Emote Shelf rather than linking the full NoireLib package. No behavioral changes were made to the imported module; line endings may be normalized. Emote Shelf's carrier orchestration, independent-copy creation and validation are separate integration code. This is not an endorsement by the upstream authors.

Synastry was examined as a behavioral reference; its conversion implementation is not included. NoireLib's upstream files and notices are retained.

## Existing external dependencies

Dalamud and FFXIVClientStructs are provided by the user's Dalamud installation, not redistributed in the licensed release package. Other Dalamud-provided DLLs copied by MSBuild are likewise excluded by package-release.ps1.

- Luna: Ottermandias and contributors, AGPL-3.0; revision ff4467bd0014294fd2c53c0e538b899781e84430. Exact source in third_party/source/Luna.zip; license in third_party/licenses/Luna.txt. Source: https://github.com/Ottermandias/Luna
- ImSharp, compiled into Luna: copyright 2025 Ottermandias, MIT; pinned Luna submodule revision fcf41fc2636df0fe9937ccda64726d4927f7dc0a. Exact source in third_party/source/ImSharp.zip; license in third_party/licenses/ImSharp.txt. Source: https://github.com/Ottermandias/ImSharp
- Penumbra.Api 5.19.2: Ottermandias, MIT as declared by its project; revision 45749c89e80ab9f7a7a0557b9c87770980048051. Exact source in third_party/source/Penumbra.Api.zip; attribution and standard MIT text in third_party/licenses/Penumbra.Api.txt. Source: https://github.com/Ottermandias/Penumbra.Api
- Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Logging.Abstractions and System.IO.Hashing: .NET Foundation and Contributors, MIT; text in third_party/licenses/dotnet.txt. Source: https://github.com/dotnet/runtime

This file does not relicense third-party code. Binary and source archive hashes are checked by package-release.ps1. BUILDING.md describes reconstruction of the dependency tree and builds from source.

## Corresponding source and release gate

The repository is https://github.com/CheesyChiz/EmoteShelf. Every distributed binary must be accompanied by the matching source archive (including third_party, project/build files, tests and relay sources), license and notices. An unpushed working tree or a link to a different revision is not matching source. Do not publish latest.zip alone. The source also includes the vendored NoireLib module, avoiding reliance on the continued availability of its upstream download.

If the included relay is deployed as an AGPL modified service, offer its matching source to remote users as required by section 13. Do not include credentials, user reports, mod packages or game assets in a source release. A source offer must point to the version actually deployed, not merely the latest client branch.
