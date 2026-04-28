# Azure OpenAI Image CLI
<!-- markdownlint-configure-file { "MD013": false } -->

Azure OpenAI image-generation CLI built in C#.

This CLI is intentionally narrow:

- **Azure OpenAI only**
- **Microsoft Entra authentication via `DefaultAzureCredential`**
- **Generate and edit image workflows**
- **Local file output with manifest and JSON automation support**
- **Reusable config in `~/.azimg/config.json`**

It does **not** support direct OpenAI endpoints or API-key authentication.

## What it can do

- Generate one or more images from a text prompt
- Edit an existing image with a text prompt
- Apply an optional PNG mask during edits
- Control image count, size, quality, background, output format, and compression
- Save outputs to a configured or overridden directory
- Write a manifest JSON file describing saved outputs
- Emit machine-readable JSON for scripting
- Validate config, endpoint selection, output setup, and Azure auth with `doctor`

## Requirements

- .NET 10 SDK
- Azure CLI for the normal local workflow
- An Azure OpenAI resource
- A deployed Azure OpenAI image model such as `gpt-image-2`
- Azure RBAC permission for image generation, typically
  `Cognitive Services OpenAI User`

## Authentication

The CLI uses `DefaultAzureCredential`.

For local development, the expected path is:

1. Sign in with Azure CLI
2. Select the right subscription if needed
3. Run the CLI

```bash
az login
az account set --subscription "<subscription-name-or-id>"
```

Because the CLI uses `DefaultAzureCredential`, other credential-chain sources can
also work, but `az login` is the intended local setup.

### RBAC failure example

If Azure returns a 401 or 403 mentioning:

```text
Microsoft.CognitiveServices/accounts/OpenAI/images/generations/action
```

then authentication succeeded, but Azure RBAC denied the operation.

Assign the role on the Azure OpenAI resource:

```bash
az role assignment create \
  --assignee "<your-user-or-service-principal>" \
  --role "Cognitive Services OpenAI User" \
  --scope "/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.CognitiveServices/accounts/<resource>"
```

Then wait for propagation and try again.

## Running the CLI

The installed command name is:

```bash
azimg
```

If you are running from source during development, use:

```bash
dotnet run --project src/AzureOpenAI.ImageGen.Cli --
```

## Quick start

### 1. Create the default config

```bash
azimg config init
```

That writes:

```text
~/.azimg/config.json
```

### 2. Edit the config for your Azure resource

Example:

```json
{
  "schemaVersion": 1,
  "defaultProfile": "azure-default",
  "profiles": {
    "azure-default": {
      "deployment": "gpt-image-2",
      "endpoint": "https://your-resource.openai.azure.com/",
      "outputDirectory": "/Users/you/.azimg/output"
    }
  }
}
```

### 3. Verify config and auth

```bash
azimg doctor --verify-auth
```

### 4. Generate an image

```bash
azimg generate \
  "A simple test image of a blue glass sphere on a white background"
```

If your config already defines `defaultProfile`, you do **not** need
`--profile azure-default`.

## Configuration

### Default location

The default config path is:

```text
~/.azimg/config.json
```

### Config fields

| Field | Required | Meaning |
| --- | --- | --- |
| `schemaVersion` | No | Current config schema marker |
| `defaultProfile` | No | Profile used when `--profile` is omitted |
| `profiles.<name>.deployment` | Yes | Azure OpenAI deployment name |
| `profiles.<name>.endpoint` | Yes | Azure OpenAI endpoint |
| `profiles.<name>.outputDirectory` | No | Default save directory |

### Multiple profiles

Use multiple profiles when switching between resources, deployments, or output
folders.

```json
{
  "schemaVersion": 1,
  "defaultProfile": "dev",
  "profiles": {
    "dev": {
      "deployment": "gpt-image-2",
      "endpoint": "https://dev-resource.openai.azure.com/",
      "outputDirectory": "/Users/you/Pictures/azimg/dev"
    },
    "prod": {
      "deployment": "gpt-image-2",
      "endpoint": "https://prod-resource.openai.azure.com/",
      "outputDirectory": "/Users/you/Pictures/azimg/prod"
    }
  }
}
```

Select a non-default profile explicitly:

```bash
azimg generate \
  "A watercolor lighthouse at sunrise" \
  --profile prod
```

### Working without a config file

The config file is optional if you pass the required Azure values directly:

```bash
azimg generate \
  "An editorial portrait of a runner in motion" \
  --endpoint https://your-resource.openai.azure.com/ \
  --deployment gpt-image-2
```

If no output directory is configured or overridden, the CLI falls back to:

```text
./output
```

## Commands

| Command | Purpose |
| --- | --- |
| `generate` | Generate one or more new images from a prompt |
| `edit` | Edit an existing image, optionally using a mask |
| `doctor` | Check config, endpoint, output directory, and Azure auth |
| `config` | Initialize, inspect, or update config |
| `version` | Print CLI version information |

Built-in help:

```bash
azimg --help
azimg generate --help
azimg edit --help
azimg doctor --help
azimg config --help
```

## `generate`

Generate one or more images from a prompt.

### Generate syntax

```bash
azimg generate "<prompt>" [options]
```

### Generate options

| Option | Meaning | Notes |
| --- | --- | --- |
| `--profile`, `-p` | Profile name from config | Optional if a default profile exists |
| `--config` | Explicit config file path | Overrides the default config path |
| `--deployment` | Deployment override | Overrides the selected profile |
| `--endpoint` | Endpoint override | Overrides the selected profile |
| `--output-directory`, `-o` | Save directory | Overrides profile output directory |
| `--count` | Number of images | Allowed range: `1` to `10` |
| `--size` | Output size | Format: `WIDTHxHEIGHT` such as `1024x1024` |
| `--quality` | Image quality | `auto`, `low`, `medium`, `high` |
| `--background` | Background mode | `auto`, `opaque`, `transparent` |
| `--output-format` | Output file format | `png`, `jpeg`, `webp` |
| `--output-compression` | Compression level | Integer from `0` to `100` |
| `--end-user-id` | End-user identifier | Passed through to Azure |
| `--name-template` | Output file name template | Supports placeholders |
| `--write-manifest` | Write manifest JSON | Saves a sidecar manifest file |
| `--json` | Emit JSON output | Useful for scripts and automation |

### Generate examples

Generate four square images:

```bash
azimg generate \
  "A cinematic portrait of a fox in a blue suit" \
  --count 4 \
  --size 1024x1024 \
  --quality high
```

Generate a wide WebP image:

```bash
azimg generate \
  "A minimalist product shot of a silver wristwatch on stone" \
  --size 1536x1024 \
  --output-format webp \
  --output-compression 90
```

Generate with a manifest and machine-readable output:

```bash
azimg generate \
  "A matte painting of a futuristic harbor in rain" \
  --write-manifest \
  --json
```

## `edit`

Edit an existing image using a prompt, with an optional mask.

### Edit syntax

```bash
azimg edit <input-file> "<prompt>" [options]
```

### Edit options

| Option | Meaning | Notes |
| --- | --- | --- |
| `--mask-file` | Optional PNG mask | Limits the edited area |
| `--profile`, `-p` | Profile name from config | Optional if a default profile exists |
| `--config` | Explicit config path | Optional |
| `--deployment` | Deployment override | Optional |
| `--endpoint` | Endpoint override | Optional |
| `--output-directory`, `-o` | Save directory | Optional |
| `--count` | Number of edited outputs | Allowed range: `1` to `10` |
| `--size` | Output size | `WIDTHxHEIGHT` |
| `--quality` | Image quality | `auto`, `low`, `medium`, `high` |
| `--background` | Background mode | `auto`, `opaque`, `transparent` |
| `--output-format` | Output file format | `png`, `jpeg`, `webp` |
| `--output-compression` | Compression level | Integer from `0` to `100` |
| `--end-user-id` | End-user identifier | Passed through to Azure |
| `--name-template` | Output file name template | Supports placeholders |
| `--write-manifest` | Write manifest JSON | Saves a sidecar manifest file |
| `--json` | Emit JSON output | Useful for scripts and automation |

### Edit examples

Edit an image without a mask:

```bash
azimg edit \
  ./input.png \
  "Make this look like a watercolor painting" \
  --count 2 \
  --size 1024x1024 \
  --quality high
```

Edit using a mask:

```bash
azimg edit \
  ./input.png \
  "Replace only the sky with a dramatic sunset" \
  --mask-file ./mask.png \
  --output-format png \
  --write-manifest
```

Edit and override the target deployment:

```bash
azimg edit \
  ./product.png \
  "Put the product on a clean studio background" \
  --deployment gpt-image-2 \
  --endpoint https://your-resource.openai.azure.com/
```

## `doctor`

Validate configuration, endpoint selection, output directory setup, and Azure
credential acquisition.

### Doctor syntax

```bash
azimg doctor [options]
```

### Doctor options

| Option | Meaning |
| --- | --- |
| `--profile`, `-p` | Profile name from config |
| `--config` | Explicit config path |
| `--deployment` | Deployment override |
| `--endpoint` | Endpoint override |
| `--output-directory`, `-o` | Output directory override |
| `--verify-auth` | Perform a live `DefaultAzureCredential` token check |
| `--json` | Emit JSON output |

### Doctor examples

Check inline settings without a config file:

```bash
azimg doctor \
  --deployment gpt-image-2 \
  --endpoint https://your-resource.openai.azure.com/
```

Check config and perform a live token acquisition:

```bash
azimg doctor --verify-auth
```

Check a non-default profile:

```bash
azimg doctor --profile prod --verify-auth
```

### What `doctor` checks

- Whether the config file was loaded, or whether inline overrides make config
  optional
- Whether the output directory can be created
- Which deployment is selected
- Which Azure endpoint is selected
- Whether `DefaultAzureCredential` can get a token when `--verify-auth` is used

`doctor` exits with code `0` when all checks pass and `3` when any check fails.

## `config`

Initialize, inspect, or update the config file.

### Config syntax

```bash
azimg config [action] [options]
```

### Config actions

| Action | Meaning |
| --- | --- |
| `show` | Show the current config |
| `init` | Create a starter config |
| `set-default-profile` | Change the default profile |

### Config options

| Option | Meaning |
| --- | --- |
| `--action` | Explicit config action |
| `--path` | Explicit config file path |
| `--profile` | Profile name for `set-default-profile` |
| `--force` | Overwrite an existing config during `init` |
| `--json` | Emit JSON output when supported |

### Config examples

Create the default config:

```bash
azimg config init
```

Create the config at a custom path:

```bash
azimg config init --path ./azimg.json
```

Show the current config:

```bash
azimg config show
```

Show the current config as JSON:

```bash
azimg config show --json
```

Set the default profile:

```bash
azimg config set-default-profile --profile prod
```

## `version`

Print the CLI version:

```bash
azimg version
```

## Output behavior

### Output directory resolution

The output directory is chosen in this order:

1. `--output-directory`
2. `profiles.<name>.outputDirectory`
3. `./output`

### File naming

The default file-name template is:

```text
{id}-{index}
```

Supported placeholders:

| Placeholder | Meaning |
| --- | --- |
| `{timestamp}` | UTC timestamp like `20260428-193500-123` |
| `{id}` | Short per-run identifier |
| `{slug}` | Prompt slug |
| `{index}` | Zero-padded image index like `01` |
| `{profile}` | Profile name slug |

Example:

```bash
azimg generate \
  "A glass sphere on white" \
  --name-template "{profile}-{timestamp}-{index}"
```

### Saved file metadata

Each saved file record includes:

- image index
- full path
- SHA-256 hash
- size in bytes

### Manifest files

When `--write-manifest` is used, the CLI writes a sidecar manifest ending in:

```text
.manifest.json
```

The manifest includes:

- prompt
- service name
- deployment name
- creation time
- token usage when available
- saved file metadata

### JSON command output

When `--json` is used:

- `generate` and `edit` emit `configPath`, `profile`, `deployment`, `files`,
  `manifest`, and `usage`
- `doctor` emits `configPath`, `profileName`, `checks`, and `isHealthy`
- `config show` emits `path`, `defaultProfile`, and `profiles`
- `config set-default-profile` emits `path` and `defaultProfile`

## Validation rules

The CLI validates requests before sending them.

### Prompt

- `generate` requires a prompt
- `edit` requires both an input file and a prompt

### Count

- Minimum: `1`
- Maximum: `10`

### Size

- Must use `WIDTHxHEIGHT`
- Width and height must be positive
- Width and height must not exceed `4096`
- Width and height must be divisible by `16`

Examples:

- `1024x1024`
- `1536x1024`
- `1024x1536`

### Quality

Allowed values:

- `auto`
- `low`
- `medium`
- `high`

### Output format

Allowed values:

- `png`
- `jpeg`
- `webp`

### Compression

Allowed range:

- `0` to `100`

### Background

Allowed values:

- `auto`
- `opaque`
- `transparent`

### Input files

For `edit`:

- the input image must exist
- the mask image must exist when `--mask-file` is provided

## Azure behavior notes

- Azure image generation can take a long time
- The CLI allows up to **20 minutes** for the network request
- The CLI does **not** set `response_format`, because Azure rejects that
  parameter
- The service may return image bytes or image URLs; the CLI handles both and
  downloads URL-based responses when necessary
- Output files are written atomically to reduce partially written results

## Build and publish

Build the solution:

```bash
dotnet build AzureOpenAI.ImageGen.Cli.slnx
```

Run the test suite:

```bash
dotnet test AzureOpenAI.ImageGen.Cli.slnx
```

Publish a framework-dependent release build:

```bash
dotnet publish src/AzureOpenAI.ImageGen.Cli/AzureOpenAI.ImageGen.Cli.csproj -c Release
```

Publish a self-contained build for a specific runtime:

```bash
dotnet publish src/AzureOpenAI.ImageGen.Cli/AzureOpenAI.ImageGen.Cli.csproj \
  -c Release \
  -r osx-arm64 \
  --self-contained true
```

The published executable name is `azimg`.

### Native AOT publish

The current project package versions are:

- `Azure.AI.OpenAI` `2.9.0-beta.1`
- `Azure.Identity` `1.21.0`
- `OpenAI` `2.10.0`

With those versions, Native AOT currently works for `osx-arm64`:

```bash
dotnet publish src/AzureOpenAI.ImageGen.Cli/AzureOpenAI.ImageGen.Cli.csproj \
  -c Release \
  -r osx-arm64 \
  -p:PublishAot=true
```

Observed `osx-arm64` result:

- standalone executable: `publish/azimg`
- executable size: about `14 MB`
- publish directory size: about `69 MB`

### Platform-specific binaries

Ship one binary per operating system and architecture.

| Target users | Build target | Current status |
| --- | --- | --- |
| macOS Apple Silicon | `osx-arm64` | Tested and working with Native AOT |
| Windows x64 | `win-x64` | Build on Windows or a Windows CI runner |
| Linux x64 | `linux-x64` | Build on Linux or a Linux CI runner |

Native AOT is not a good cross-OS packaging story. In practice:

- build macOS binaries on macOS
- build Windows binaries on Windows
- build Linux binaries on Linux

### Native AOT caveats

Strict dependency verification still fails with:

```bash
dotnet publish src/AzureOpenAI.ImageGen.Cli/AzureOpenAI.ImageGen.Cli.csproj \
  -c Release \
  -r osx-arm64 \
  -p:PublishAot=true \
  -p:VerifyReferenceAotCompatibility=true
```

The current Azure and MSAL dependency chain is not fully annotated as
AOT-compatible by .NET's reference-verification rules, even though the plain
`PublishAot=true` `osx-arm64` build succeeds.

### Release automation

This repository now includes:

```text
.github/workflows/release-and-publish.yml
```

The workflow:

1. runs the test suite
2. builds Native AOT release artifacts on macOS, Linux, and Windows runners
3. creates or updates a GitHub release
4. uploads release assets
5. optionally updates Homebrew and WinGet

You can trigger it in either of these ways:

1. Push a tag like `v0.1.0`
2. Run the workflow manually with a version input

Release assets currently published by the workflow:

- `azimg-darwin-arm64.tar.gz`
- `azimg-linux-x64.tar.gz`
- `azimg-windows-x64.exe`
- `checksums.txt`

The workflow pins the GitHub Actions it uses to the latest releases that were
checked when the workflow was written.

#### Repository configuration for package managers

| Name | Type | Required for | Purpose |
| --- | --- | --- | --- |
| `HOMEBREW_TAP_REPOSITORY` | Variable | Homebrew | Target tap repo, for example `Jcardif/homebrew-tap` |
| `HOMEBREW_TAP_TOKEN` | Secret | Homebrew | Personal access token with write access to the tap repo |
| `WINGET_CREATE_MODE` | Variable | WinGet | Use `new` for the first submission, then `update` afterwards |
| `WINGET_PACKAGE_IDENTIFIER` | Variable | WinGet update mode | Existing package identifier, for example `Jcardif.AzureOpenAIImageCLI` |
| `WINGET_CREATE_GITHUB_TOKEN` | Secret | WinGet | GitHub token used by `wingetcreate` to submit to `winget-pkgs` |

#### Homebrew behavior

When `HOMEBREW_TAP_REPOSITORY` and `HOMEBREW_TAP_TOKEN` are configured, the
workflow updates `Formula/azimg.rb` in your tap repo and points it at the latest
macOS and Linux release archives.

#### WinGet behavior

When `WINGET_CREATE_GITHUB_TOKEN` is configured, the workflow uses
`wingetcreate`.

- For the first submission, set `WINGET_CREATE_MODE=new`
- After the package exists in WinGet, set `WINGET_CREATE_MODE=update`
- In `update` mode, also set `WINGET_PACKAGE_IDENTIFIER`

The workflow uses the Windows release asset:

```text
azimg-windows-x64.exe
```

## Troubleshooting

### No configuration file found

Create one:

```bash
azimg config init
```

Or pass `--endpoint` and `--deployment` directly.

### I do not want to pass `--profile`

Set `defaultProfile` in the config and omit `--profile`.

### `doctor --verify-auth` fails

Try:

```bash
az login
az account show
```

Then make sure the selected identity has access to the Azure OpenAI resource.

### Image generation returns 401 or 403

Most likely causes:

- wrong Azure account selected
- missing Azure RBAC role
- correct account, wrong resource scope

The required action for image generation is:

```text
Microsoft.CognitiveServices/accounts/OpenAI/images/generations/action
```

### Image generation takes a long time

That can be normal. Azure image generation may take several minutes and can
approach 15 minutes.

### Validation error for size

Use a value like:

- `1024x1024`
- `1536x1024`
- `1024x1536`

Make sure both dimensions are divisible by `16`.

### Validation error for output compression

Use a value between `0` and `100`.

### Validation error for background

Use one of:

- `auto`
- `opaque`
- `transparent`

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Success |
| `1` | Usage error |
| `2` | Validation error |
| `3` | Configuration error |
| `4` | Authentication or authorization error |
| `5` | I/O error |
| `130` | Cancelled |
| `255` | Unhandled failure |
