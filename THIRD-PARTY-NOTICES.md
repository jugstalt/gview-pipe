# Third-Party Notices

gView.Pipe is licensed under the [Apache License 2.0](LICENSE). It uses the third-party components listed below, each under its own license. License and copyright information is taken from the NuGet package metadata. The full license texts for the license types used are at the end of this file.

## Components shipped with the application

These packages are part of the built application (`gView.Pipe.Shell` and the libraries it references). The column *Referenced* says whether a gView.Pipe project references the package directly, or which package pulls it in transitively.

| Component | Version | License | Copyright | Referenced | Project |
|---|---|---|---|---|---|
| MudBlazor | 9.12.0 | MIT | Copyright (c) 2021 MudBlazor | directly (Razor, Shell) | https://mudblazor.com |
| BlazorMonaco | 3.5.0 | MIT | Copyright (c) Serdar Ciplak | directly (Razor) | https://serdarciplak.github.io/BlazorMonaco/ |
| Monaco Editor (JavaScript, bundled with BlazorMonaco) | bundled | MIT | Copyright (c) Microsoft Corporation | transitively via BlazorMonaco; loaded by the code fields of the editor | https://github.com/microsoft/monaco-editor |
| Markdig | 0.37.0 | BSD-2-Clause | Copyright (c) Alexandre Mutel | directly (Portal) | https://github.com/lunet-io/markdig |
| Z.Blazor.Diagrams | 3.0.4.1 | MIT | Copyright (c) Haytam Zanid | directly (Razor) | https://github.com/Blazor-Diagrams/Blazor.Diagrams |
| Z.Blazor.Diagrams.Core | 3.0.4.1 | MIT | Copyright (c) Haytam Zanid | transitively via Z.Blazor.Diagrams | https://github.com/Blazor-Diagrams/Blazor.Diagrams |
| SvgPathProperties | 1.1.2 | MIT | Copyright (c) Haytam Zanid | transitively via Z.Blazor.Diagrams.Core | https://github.com/zHaytam/SvgPathProperties |
| Jint | 4.17.0 | BSD-2-Clause | Copyright (c) Sebastien Ros | directly (Core) | https://github.com/sebastienros/jint |
| Acornima | 1.7.0 | BSD-3-Clause | Copyright (c) Adam Simon | transitively via Jint | https://github.com/adams85/acornima |
| Cronos | 0.13.0 | MIT | Copyright (c) 2017 Hangfire OÜ | directly (Core) | https://github.com/HangfireIO/Cronos |
| Polly.Core, Polly.Extensions, Polly.RateLimiting | 8.4.2 | BSD-3-Clause | Copyright (c) 2024, App vNext | transitively via Microsoft.Extensions.Http.Resilience → Microsoft.Extensions.Resilience (ServiceDefaults) | https://github.com/App-vNext/Polly |
| OpenTelemetry.Extensions.Hosting, OpenTelemetry.Exporter.OpenTelemetryProtocol, OpenTelemetry.Instrumentation.AspNetCore, OpenTelemetry.Instrumentation.Http, OpenTelemetry.Instrumentation.Runtime | 1.20.1 | Apache-2.0 | Copyright The OpenTelemetry Authors | directly (ServiceDefaults) | https://opentelemetry.io |
| OpenTelemetry, OpenTelemetry.Api, OpenTelemetry.Api.ProviderBuilderExtensions | 1.20.1 | Apache-2.0 | Copyright The OpenTelemetry Authors | transitively via the OpenTelemetry packages above | https://opentelemetry.io |
| Microsoft.Extensions.ServiceDiscovery, Microsoft.Extensions.Http.Resilience | 10.11.0 | MIT | Copyright (c) Microsoft Corporation | directly (ServiceDefaults) | https://github.com/dotnet/extensions |
| Microsoft.AspNetCore.Authentication.OpenIdConnect, Microsoft.AspNetCore.OpenApi | 10.0.14 | MIT | Copyright (c) Microsoft Corporation | directly (AspNetCore) | https://github.com/dotnet/dotnet |
| Microsoft.Extensions.Hosting.WindowsServices | 10.0.14 | MIT | Copyright (c) Microsoft Corporation | directly (Shell) | https://github.com/dotnet/dotnet |
| Microsoft.Extensions.\*, Microsoft.AspNetCore.Components.Web, Microsoft.AspNetCore.DataProtection | 10.x | MIT | Copyright (c) Microsoft Corporation | directly, plus their own Microsoft.Extensions.\* dependencies | https://github.com/dotnet/dotnet |
| MailKit | 4.18.2 | MIT | Copyright (c) .NET Foundation and Contributors | directly (Plugins) | https://www.mimekit.net |
| MimeKit | 4.18.2 | MIT | Copyright (c) .NET Foundation and Contributors | transitively via MailKit | https://www.mimekit.net |
| BouncyCastle.Cryptography | 2.7.0 | MIT | Copyright (c) Legion of the Bouncy Castle Inc. | transitively via MimeKit | https://www.bouncycastle.org |
| System.Security.Cryptography.Pkcs | 10.0.14 | MIT | Copyright (c) Microsoft Corporation | directly (Plugins, Plugins.Database), lifting the vulnerable version MimeKit and SqlClient bring | https://github.com/dotnet/dotnet |

### Database plugin

`gView.Pipe.Plugins.Database` is installed as an external plugin (`plugins/gView.Pipe.Plugins.Database/`) and brings these components:

| Component | Version | License | Copyright | Referenced | Project |
|---|---|---|---|---|---|
| Microsoft.Data.SqlClient (+ .Extensions.Abstractions, .Internal.Logging) | 7.1.1 | MIT | Copyright (c) Microsoft Corporation | directly | https://aka.ms/sqlclientproject |
| Microsoft.Data.SqlClient.SNI.runtime | 7.1.0 | Microsoft Software License Terms (redistributable object code) | Copyright (c) Microsoft Corporation | transitively via Microsoft.Data.SqlClient | https://aka.ms/sqlclientproject |
| Microsoft.SqlServer.Server | 1.0.0 | MIT | Copyright (c) Microsoft Corporation | transitively via Microsoft.Data.SqlClient | https://aka.ms/sqlclientproject |
| Microsoft.IdentityModel.\*, System.IdentityModel.Tokens.Jwt | 8.16.0 | MIT | Copyright (c) Microsoft Corporation | transitively via Microsoft.Data.SqlClient | https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet |
| Microsoft.Bcl.Cryptography, System.Configuration.ConfigurationManager, System.Diagnostics.EventLog, System.Security.Cryptography.ProtectedData, System.Threading.RateLimiting, Microsoft.Extensions.Caching.\* | 9.0.18 | MIT | Copyright (c) Microsoft Corporation | transitively via Microsoft.Data.SqlClient | https://github.com/dotnet/dotnet |
| Npgsql | 10.0.3 | PostgreSQL License | Copyright (c) The Npgsql Development Team | directly | https://github.com/npgsql/npgsql |
| Microsoft.Data.Sqlite, Microsoft.Data.Sqlite.Core | 10.0.14 | MIT | Copyright (c) Microsoft Corporation | directly / via Microsoft.Data.Sqlite | https://learn.microsoft.com/dotnet/standard/data/sqlite/ |
| SQLitePCLRaw.bundle_e_sqlite3, .core, .provider.e_sqlite3, .lib.e_sqlite3 | 2.1.13 | Apache-2.0 | Copyright (c) 2014-2024 SourceGear, LLC | transitively via Microsoft.Data.Sqlite | https://github.com/ericsink/SQLitePCL.raw |
| SQLite (native e_sqlite3) | bundled | Public domain | — | transitively via SQLitePCLRaw.lib.e_sqlite3 | https://sqlite.org/copyright.html |

The PostgreSQL License is a permissive license similar to MIT/BSD: https://opensource.org/license/postgresql.

### Office plugin

`gView.Pipe.Plugins.Office` is installed as an external plugin (`plugins/gView.Pipe.Plugins.Office/`) and brings one component:

| Component | Version | License | Copyright | Referenced | Project |
|---|---|---|---|---|---|
| MiniExcel | 1.46.0 | Apache-2.0 | Copyright (c) Wei Lin, 2021 onwards | directly | https://github.com/mini-software/MiniExcel |

### AI plugin

`gView.Pipe.Plugins.AI` is installed as an external plugin (`plugins/gView.Pipe.Plugins.AI/`) and brings these components:

| Component | Version | License | Copyright | Referenced | Project |
|---|---|---|---|---|---|
| Anthropic | 13.6.0 | Apache-2.0 | Copyright 2023 Anthropic, PBC. | directly | https://github.com/anthropics/anthropic-sdk-csharp |
| Microsoft.Extensions.AI.Abstractions | 10.5.1 | MIT | Copyright (c) Microsoft Corporation | transitively via Anthropic | https://github.com/dotnet/extensions |
| Azure.Identity, Azure.Core, System.ClientModel, System.Memory.Data, Microsoft.Bcl.AsyncInterfaces | 1.21.0, 1.53.0, 1.10.0, 10.0.3, 10.0.3 | MIT | Copyright (c) Microsoft Corporation | directly (managed identity for Azure OpenAI) / via Azure.Identity | https://github.com/Azure/azure-sdk-for-net |
| Microsoft.Identity.Client (+ .Extensions.Msal), Microsoft.IdentityModel.Abstractions, System.Security.Cryptography.ProtectedData | 4.83.1, 8.14.0, 4.5.0 | MIT | Copyright (c) Microsoft Corporation | transitively via Azure.Identity | https://github.com/AzureAD/microsoft-authentication-library-for-dotnet |

### Azure Storage plugin

`gView.Pipe.Plugins.AzureStorage` is installed as an external plugin (`plugins/gView.Pipe.Plugins.AzureStorage/`) and brings these components:

| Component | Version | License | Copyright | Referenced | Project |
|---|---|---|---|---|---|
| Azure.Storage.Blobs, Azure.Storage.Common, Azure.Identity | 12.30.1, 12.29.0, 1.21.0 | MIT | Copyright (c) Microsoft Corporation | directly / via Azure.Storage.Blobs | https://github.com/Azure/azure-sdk-for-net |
| Azure.Core, System.ClientModel, System.Memory.Data, System.IO.Hashing, Microsoft.Bcl.AsyncInterfaces | 1.60.0, 1.14.0, 10.0.9, 10.0.9, 10.0.9 | MIT | Copyright (c) Microsoft Corporation | transitively via the Azure SDK | https://github.com/Azure/azure-sdk-for-net |
| Microsoft.Identity.Client (+ .Extensions.Msal), Microsoft.IdentityModel.Abstractions, System.Security.Cryptography.ProtectedData | 4.84.2, 8.14.0, 4.5.0 | MIT | Copyright (c) Microsoft Corporation | transitively via Azure.Identity | https://github.com/AzureAD/microsoft-authentication-library-for-dotnet |
| Apache.Arrow | 22.1.0 | Apache-2.0 | Copyright (c) 2016-2025 The Apache Software Foundation | transitively via the Azure SDK | https://arrow.apache.org/ |

### SFTP plugin

`gView.Pipe.Plugins.Sftp` is installed as an external plugin (`plugins/gView.Pipe.Plugins.Sftp/`) and brings these components:

| Component | Version | License | Copyright | Referenced | Project |
|---|---|---|---|---|---|
| SSH.NET | 2026.0.0 | MIT | Copyright (c) Renci 2010-2026 | directly | https://github.com/sshnet/SSH.NET |
| BouncyCastle.Cryptography | 2.7.0 | MIT | Copyright (c) Legion of the Bouncy Castle Inc. | transitively via SSH.NET | https://www.bouncycastle.org |

### Secrets plugin

`gView.Pipe.Plugins.Secrets` is installed as an external plugin (`plugins/gView.Pipe.Plugins.Secrets/`) and brings these components:

| Component | Version | License | Copyright | Referenced | Project |
|---|---|---|---|---|---|
| Azure.Identity, Azure.Security.KeyVault.Secrets | 1.21.0, 4.11.2 | MIT | Copyright (c) Microsoft Corporation | directly | https://github.com/Azure/azure-sdk-for-net |
| Azure.Core, System.ClientModel, System.Memory.Data, Microsoft.Identity.Client (+ .Extensions.Msal) | 1.54.0, 1.9.0, 8.0.1, 4.83.1 | MIT | Copyright (c) Microsoft Corporation | transitively via Azure.Identity | https://github.com/Azure/azure-sdk-for-net |
| IdentityServerNET.Clients, IdentityServerNET.Extensions | 7.26.2901 | Apache-2.0 | Copyright (c) 2026 Jürgen Gstaltmaier | directly / via IdentityServerNET.Clients | https://github.com/jugstalt/identityservernet |
| Duende.IdentityModel | 8.1.0 | Apache-2.0 | Copyright (c) Duende Software | transitively via IdentityServerNET.Clients | https://github.com/DuendeSoftware/foss |
| Newtonsoft.Json | 13.0.4 | MIT | Copyright (c) James Newton-King | transitively via IdentityServerNET.Clients | https://www.newtonsoft.com/json |
| Microsoft.IdentityModel.\*, System.IdentityModel.Tokens.Jwt | 8.19.2 | MIT | Copyright (c) Microsoft Corporation | transitively via Azure.Identity and IdentityServerNET.Clients | https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet |

### Scripts plugin

`gView.Pipe.Plugins.Scripts` contains no third-party components. For Git repository sources it calls the git command line installed on the host; git is not part of gView.Pipe and keeps its own license (GPLv2, https://git-scm.com/about/free-and-open-source).

Polly participates in the [Open Source Maintenance Fee](https://thepollyproject.org/2026/07/14/polly-osmf-announcement.html) from the first major version released after November 16, 2026. The fee applies only to direct references to Polly, not to Polly as a dependency of Microsoft.Extensions.Http.Resilience, and not to existing versions such as 8.x. gView.Pipe does not reference Polly directly; keep it that way, or check the fee before adding a direct reference.

MudBlazor includes the Material Design icons by Google as SVG paths. They are licensed under the Apache License 2.0 (https://github.com/google/material-design-icons).

The Shell loads the Roboto font from Google Fonts at runtime; it is not part of this repository. Roboto is licensed under the SIL Open Font License 1.1 (https://fonts.google.com/specimen/Roboto/license).

## Development and test only

These packages are used to orchestrate, build or test the solution. They are not part of the shipped application.

| Component | Version | License | Copyright | Referenced | Project |
|---|---|---|---|---|---|
| Aspire (Aspire.AppHost.Sdk, Aspire.Hosting.AppHost) | 13.6.1 | MIT | Copyright (c) Microsoft Corporation | directly (AppHost) | https://github.com/microsoft/aspire |
| MessagePack, MessagePack.Annotations | 2.5.307 | MIT | Copyright (c) Yoshifumi Kawai and contributors | transitively via Aspire.Hosting and StreamJsonRpc; MessagePack is pinned directly in the AppHost to a version without known vulnerabilities | https://github.com/MessagePack-CSharp/MessagePack-CSharp |
| xunit.v3 | 4.0.2 | Apache-2.0 | Copyright (c) .NET Foundation | directly (tests), pulls in the other xunit.v3.\* and xunit.analyzers packages | https://github.com/xunit/xunit |
| xunit.runner.visualstudio | 4.0.1 | Apache-2.0 | Copyright (c) .NET Foundation | directly (tests) | https://github.com/xunit/visualstudio.xunit |
| bunit | 2.13.3 | MIT | Copyright (c) Egil Hansen | directly (Razor.Tests) | https://bunit.egilhansen.com |
| Microsoft.Playwright | 1.64.1 | MIT | Copyright (c) Microsoft Corporation | directly (Docs.Screenshots, a build tool); downloads Chromium on first use, which is not part of this repository | https://github.com/microsoft/playwright-dotnet |
| CommunityToolkit.Aspire.Hosting.MailPit | 13.6.1 | MIT | Copyright (c) .NET Foundation and Contributors | directly (AppHost), optional dev container | https://github.com/CommunityToolkit/Aspire |
| IdentityServerNET.Aspire.Hosting | 7.26.2902 | Apache-2.0 | Copyright (c) 2026 Jürgen Gstaltmaier | directly (AppHost), optional dev container | https://github.com/jugstalt/identityservernet |
| Microsoft.NET.Test.Sdk, Microsoft.AspNetCore.TestHost | 18.11.0 / 10.0.14 | MIT | Copyright (c) Microsoft Corporation | directly (tests) | https://github.com/microsoft/vstest |

Aspire.Hosting pulls in further packages transitively, among them gRPC, Google.Protobuf, KubernetesClient, Newtonsoft.Json, StreamJsonRpc, YamlDotNet and its own OpenTelemetry (1.17.0) and Polly.Core. Their licenses are listed in the `THIRD-PARTY-NOTICES.TXT` of the Aspire packages.

---

## License texts

### MIT License

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### BSD 2-Clause License

```
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

### BSD 3-Clause License

```
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its
   contributors may be used to endorse or promote products derived from
   this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

### Apache License 2.0

The full text is in this repository's [LICENSE](LICENSE) file and at https://www.apache.org/licenses/LICENSE-2.0.

### SIL Open Font License 1.1

https://openfontlicense.org/open-font-license-official-text/
