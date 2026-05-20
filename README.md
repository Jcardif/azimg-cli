# 🖼️ AzImg CLI

AzImg CLI is a non-interactive command-line tool for Azure OpenAI image generation and image editing.
The executable command is `azimg`.

## ✨ Key features

- Non-interactive by default for agents, scripts, CI, and unattended shells.
- Azure OpenAI image generation and image editing workflows.
- Local config profiles for endpoint, deployment, and output directory defaults.
- Optional manifest files with saved-image paths, checksums, usage, deployment, and timestamp metadata.

## 📦 Installation

Supported release platforms:

- Windows x64: `win-x64`
- Windows ARM64: `win-arm64`
- macOS Apple Silicon: `osx-arm64`
- macOS Intel: `osx-x64`
- Linux x64: `linux-x64`

### macOS and Linux

```bash
curl -fsSL https://raw.githubusercontent.com/Jcardif/azimg-cli/main/install.sh | bash
```

### Windows PowerShell

```powershell
iwr https://raw.githubusercontent.com/Jcardif/azimg-cli/main/install.ps1 -UseB | iex
```

## 🔐 Authentication

AzImg CLI uses the Azure CLI credential provider, no built-in token storage, that is delegated to the Azure CLI.

```bash
# Login via Azure CLI first
az login

# Verify authentication with azimg doctor
azimg doctor --verify-auth
```

The authenticated identity must be authorized on the Azure OpenAI resource with the role `Cognitive Services OpenAI User` assigned.

To assign the role via Azure CLI:

```bash
az role assignment create \
  --assignee "<your-user-or-service-principal>" \
  --role "Cognitive Services OpenAI User" \
  --scope "/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.CognitiveServices/accounts/<resource>"
```

## 🚀 Usage examples

Generate one image and write a manifest:

```bash
azimg generate "A watercolor robot painting clouds" \
  --profile azure-default \
  --count 1 \
  --write-manifest
```

Generate multiple transparent PNG images into a specific directory:

```bash
azimg generate "Minimal app icon for an image CLI" \
  --count 3 \
  --background transparent \
  --output-format png \
  --output-directory ./out
```

Edit an existing image:

```bash
azimg edit input.png "Make the background transparent" --profile azure-default
```

Edit an image with a PNG mask:

```bash
azimg edit input.png "Replace the sky with sunset clouds" \
  --mask-file mask.png \
  --profile azure-default
```

Check for an update:

```bash
azimg update check
```

Install the latest update:

```bash
azimg update
```

Preview an update without changing files:

```bash
azimg update --dry-run
```

## ⚙️ Configuration and common settings

The default configuration file is `~/.azimg/config.json`.
Create a starter config with `azimg config init --force`, then edit the endpoint and deployment values.

```json
{
  "schemaVersion": 1,
  "defaultProfile": "azure-default",
  "profiles": {
    "azure-default": {
      "deployment": "gpt-image-2",
      "endpoint": "https://your-resource.openai.azure.com/",
      "outputDirectory": "~/.azimg/output"
    }
  }
}
```

## 📚 Command reference

Command responses are JSON by default. Add `--format text` when you want human-readable output.

| Command | What it does | Inputs and options |
| --- | --- | --- |
| `azimg --help` | Shows top-level help. | No required input. |
| `azimg <COMMAND> --help` | Shows help for one command. | Works with `generate`, `edit`, `doctor`, `config`, `update`, and `version`. |
| `azimg generate <PROMPT>` | Generates one or more images from a text prompt. | Required: `<PROMPT>`. Profile/options: `-p, --profile <NAME>`, `--config <PATH>`, `--deployment <NAME>`, `--endpoint <URL>`, `-o, --output-directory <PATH>`. Image/options: `--count <N>`, `--size <WIDTHxHEIGHT>`, `--quality <auto/low/medium/high>`, `--background <auto/opaque/transparent>`, `--output-format <png/jpeg/webp>`, `--output-compression <0-100>`, `--end-user-id <ID>`. File/options: `--name-template <TEMPLATE>`, `--write-manifest`. Output/options: `--format <json/text>`. |
| `azimg edit <INPUT_FILE> <PROMPT>` | Edits an existing image, optionally with a mask. | Required: `<INPUT_FILE>` and `<PROMPT>`. Edit/options: `--mask-file <PATH>`. Profile/options: `-p, --profile <NAME>`, `--config <PATH>`, `--deployment <NAME>`, `--endpoint <URL>`, `-o, --output-directory <PATH>`. Image/options: `--count <N>`, `--size <WIDTHxHEIGHT>`, `--quality <auto/low/medium/high>`, `--background <auto/opaque/transparent>`, `--output-format <png/jpeg/webp>`, `--output-compression <0-100>`, `--end-user-id <ID>`. File/options: `--name-template <TEMPLATE>`, `--write-manifest`. Output/options: `--format <json/text>`. |
| `azimg doctor` | Checks config, profile resolution, endpoint shape, output directory access, and optionally Azure CLI auth. | Profile/options: `-p, --profile <NAME>`, `--config <PATH>`, `--deployment <NAME>`, `--endpoint <URL>`, `-o, --output-directory <PATH>`. Auth/options: `--verify-auth`. Output/options: `--format <json/text>`. |
| `azimg config show` | Prints the resolved config file and configured profiles. | Options: `--path <PATH>`, `--format <json/text>`. This is also the default `config` action when no action is provided. |
| `azimg config init` | Writes a starter config file. | Options: `--path <PATH>`, `--force`, `--format <json/text>`. |
| `azimg config set-default-profile --profile <NAME>` | Changes the default profile in the config file. | Required: `--profile <NAME>`. Options: `--path <PATH>`, `--format <json/text>`. The action can also be supplied as `--action set-default-profile`. |
| `azimg update check` | Checks whether a newer release is available. | Options: `--version <TAG>`, `--manifest-url <URL>`, `--install-dir <PATH>`, `--force`, `--format <json/text>`. |
| `azimg update` | Installs the selected release; equivalent to `azimg update apply`. | Options: `--version <TAG>`, `--manifest-url <URL>`, `--install-dir <PATH>`, `--dry-run`, `--force`, `--format <json/text>`. The apply action can also be written as `azimg update apply` or `azimg update install`. |
| `azimg version` | Prints version information. | Options: `--format <json/text>`. |

## 🧑‍💻 Development

Start from a fresh clone, restore dependencies, build, run tests, and smoke-test the local CLI:

```bash
git clone https://github.com/Jcardif/azimg-cli.git
cd azimg-cli

dotnet restore AzImg.Cli.slnx
dotnet build AzImg.Cli.slnx --configuration Release --no-restore
dotnet test AzImg.Cli.slnx --configuration Release --no-build

dotnet run --project src/AzImg.Cli doctor
dotnet run --project src/AzImg.Cli version
```

## 📝 Release notes

Release notes live in `docs/release-notes/<tag>.md`.

## ⚖️ License

This project is licensed under the MIT License.
