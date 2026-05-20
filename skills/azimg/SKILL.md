---
name: azimg
description: >
   Use for image creation and image editing only. Use `azimg generate` when the request asks to generate, create, or make a new image. Use `azimg edit` only when the request provides an input image to modify.
compatibility: Requires azimg on path
---

# AzImg CLI

Use `azimg` to satisfy image generation and image editing requests.
Everything in this skill should help produce, save, and report images; treat setup and diagnostics as unblockers only.

## Image-first workflow

1. Classify the request first: generation intent uses `azimg generate`; edit intent with an input image uses `azimg edit`.
2. Use `azimg generate` for new images and `azimg edit` for edits.
3. Use the existing default profile by omitting `--profile` unless the user names a profile.
4. Keep the default structured JSON output for every workflow so the agent can parse results and decide how to present them.
5. Try the image command before setup commands; follow the Minimal unblockers order only when image creation is blocked or prerequisites are unknown.
6. Prefer using `--write-manifest` when you need the metadata, checksums, repeatable artifacts, or a durable list of output files.
7. Report the saved image paths, manifest path if present, and any exact blocking error.

## Generate new images

Use this when the user asks to generate, create, or make a new image.

```bash
azimg generate <prompt> [image options]
```

### Generate options

| Option | Use when |
| --- | --- |
| `<prompt>` | Always required; quote the user's image prompt. |
| `--count <n>` | The user asks for multiple variations; valid range is `1` to `10`. |
| `--size <WxH>` | The user requests dimensions, for example `1024x1024`. |
| `--quality <value>` | The user requests quality or speed tradeoffs; allowed values are `auto`, `low`, `medium`, and `high`. |
| `--background <value>` | The user asks for transparency or opaque background; allowed values are `auto`, `opaque`, and `transparent`. |
| `--output-format <value>` | The user asks for a specific file type; allowed values are `png`, `jpeg`, `jpg`, and `webp`. |
| `--output-compression <0-100>` | The user asks for compression and the format supports it. |
| `--output-directory <path>` or `-o <path>` | The user specifies where to save images. |
| `--name-template <template>` | The user needs predictable file names. |
| `--write-manifest` | The user needs metadata, checksums, or a durable file list. |
| `--profile <name>` or `-p <name>` | The user names a profile; otherwise omit it. |
| `--config <path>` | The user provides a non-default config file. |
| `--deployment <name>` and `--endpoint <url>` | The user provides inline Azure OpenAI settings. |

Examples:

```bash
azimg generate "A watercolor robot painting clouds" --count 1
azimg generate "Minimal app icon" --count 3 --background transparent --output-format png --write-manifest
```

## Edit existing images

Use this when the user provides an input image and asks to alter it.

```bash
azimg edit <input-file> <prompt> [image options]
```

### Edit options

| Option | Use when |
| --- | --- |
| `<input-file>` | Always required; must be an existing local image path. |
| `<prompt>` | Always required; quote the user's edit instruction. |
| `--mask-file <path>` | The user provides a mask for a targeted edit. |
| `--count <n>` | The user asks for multiple edited versions; valid range is `1` to `10`. |
| `--size <WxH>` | The user requests output dimensions. |
| `--quality <value>` | The user requests quality or speed tradeoffs; allowed values are `auto`, `low`, `medium`, and `high`. |
| `--background <value>` | The user asks for transparency or opaque background; allowed values are `auto`, `opaque`, and `transparent`. |
| `--output-format <value>` | The user asks for a specific file type; allowed values are `png`, `jpeg`, `jpg`, and `webp`. |
| `--output-compression <0-100>` | The user asks for compression and the format supports it. |
| `--output-directory <path>` or `-o <path>` | The user specifies where to save edited images. |
| `--name-template <template>` | The user needs predictable file names. |
| `--write-manifest` | The user needs metadata, checksums, or a durable file list. |
| `--profile <name>` or `-p <name>` | The user names a profile; otherwise omit it. |
| `--config <path>` | The user provides a non-default config file. |
| `--deployment <name>` and `--endpoint <url>` | The user provides inline Azure OpenAI settings. |

Examples:

```bash
azimg edit input.png "Make the background transparent"
azimg edit input.png "Replace the sky with sunset clouds" --mask-file mask.png --write-manifest
```

## Minimal unblockers

Use this order only when an image request is blocked or prerequisites are unknown:

1. If `azimg` availability is unknown, run `azimg --help`.
2. If config/profile status is unknown, run `azimg config show`; do not overwrite config.
3. If setup is still blocking image creation, run `azimg doctor`.
4. If Azure authentication is specifically blocking image creation, run `azimg doctor --verify-auth`.
5. If no config exists and the user wants setup, run `azimg config init` without `--force`.
6. Use `azimg config init --force` only when the user explicitly asks to overwrite or reset an existing config.

## Validation reminders

- `--count` must be `1` through `10`.
- `--size` must be `WIDTHxHEIGHT`; dimensions must be positive, divisible by `16`, and no larger than `4096`.
- `--output-compression <n>` is available for image commands when compression is needed; valid range is `0` to `100`.
- `--name-template` supports `{timestamp}`, `{id}`, `{slug}`, `{index}`, and `{profile}`.
- Use `--` before a prompt or path that begins with `-`.

## Reporting back

- State whether you generated new images or edited an existing image.
- Include `manifest: <path>` when a manifest was written.
- If blocked, report the exact error and the smallest safe next step.
