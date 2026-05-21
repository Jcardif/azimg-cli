---
name: azimg
description: >
  Use for image creation and image editing only. Use `azimg generate` when the
  request asks to generate, create, or make a new image. Use `azimg edit` only
  when the request provides an input image to modify.
compatibility: Requires azimg on path
---

# AzImg CLI

Use `azimg` to satisfy image generation and image editing requests.
Everything in this skill should help produce, save, and report images.
Treat setup and diagnostics as unblockers only.

## Image-first workflow

1. Classify the request first: generation uses `azimg generate`; edits with an
   input image use `azimg edit`.
2. Use the existing default profile by omitting `--profile` unless the user
   names a profile.
3. Choose the output folder before running the image command. Strongly prefer
   passing `--output-directory <path>` explicitly.
4. Keep structured JSON output so the agent can parse saved paths.
5. Try the image command before setup commands. Use Minimal unblockers only
   when image creation is blocked or prerequisites are unknown.
6. Prefer `--write-manifest` when you need metadata, checksums, repeatable
   artifacts, or a durable list of output files.
7. Report saved image paths, manifest path if present, and any exact blocking
   error.

## Output folder guidance

Strongly prefer passing `--output-directory` on every `azimg generate` and
`azimg edit` command so images land in the right project or task folder. If the
user names a directory, use that exact directory.

If the user does not name a directory, choose a clear folder under the current
working directory. Use `./azimg-output` for general requests, or a descriptive
subfolder such as `./azimg-output/logo-concepts` for a specific task.

The CLI default output folder is `azimg-output`, resolved relative to the
directory where the command runs. Still pass `--output-directory ./azimg-output`
explicitly unless there is a better task-specific folder.

Do not put output folder choices in config profiles. Profiles are for Azure
connection settings; output location is a per-run decision.

## Generate new images

Use this when the user asks to generate, create, or make a new image.

```bash
azimg generate <prompt> [image options]
```

### Generate options

- `<prompt>`: Always required; quote the user's image prompt.
- `--count <n>`: Use for multiple variations; valid range is `1` to `10`.
- `--size <WxH>`: Use for requested dimensions, such as `1024x1024`.
- `--quality <value>`: Use for quality or speed tradeoffs. Allowed values are
  `auto`, `low`, `medium`, and `high`.
- `--background <value>`: Use for transparency or opaque background. Allowed
  values are `auto`, `opaque`, and `transparent`.
- `--output-format <value>`: Use for requested file type. Allowed values are
  `png`, `jpeg`, `jpg`, and `webp`.
- `--output-compression <0-100>`: Use when compression is requested and the
  format supports it.
- `--output-directory <path>` or `-o <path>`: Strongly recommended for every
  run. Use the user's directory or a clear project-local folder.
- `--name-template <template>`: Use for predictable file names.
- `--write-manifest`: Use for metadata, checksums, or a durable file list.
- `--profile <name>` or `-p <name>`: Use only when the user names a profile.
- `--config <path>`: Use when the user provides a non-default config file.
- `--deployment <name>` and `--endpoint <url>`: Use when the user provides
  inline Azure OpenAI settings.

Examples:

```bash
azimg generate "A watercolor robot painting clouds" \
  --count 1 \
  --output-directory ./azimg-output

azimg generate "Minimal app icon" \
  --count 3 \
  --background transparent \
  --output-format png \
  --output-directory ./azimg-output/icons \
  --write-manifest
```

## Edit existing images

Use this when the user provides an input image and asks to alter it.

```bash
azimg edit <input-file> <prompt> [image options]
```

### Edit options

- `<input-file>`: Always required; must be an existing local image path.
- `<prompt>`: Always required; quote the user's edit instruction.
- `--mask-file <path>`: Use when the user provides a mask for targeted edits.
- `--count <n>`: Use for multiple edited versions; valid range is `1` to `10`.
- `--size <WxH>`: Use when the user requests output dimensions.
- `--quality <value>`: Use for quality or speed tradeoffs. Allowed values are
  `auto`, `low`, `medium`, and `high`.
- `--background <value>`: Use for transparency or opaque background. Allowed
  values are `auto`, `opaque`, and `transparent`.
- `--output-format <value>`: Use for requested file type. Allowed values are
  `png`, `jpeg`, `jpg`, and `webp`.
- `--output-compression <0-100>`: Use when compression is requested and the
  format supports it.
- `--output-directory <path>` or `-o <path>`: Strongly recommended for every
  run. Use the user's directory or a clear project-local folder.
- `--name-template <template>`: Use for predictable file names.
- `--write-manifest`: Use for metadata, checksums, or a durable file list.
- `--profile <name>` or `-p <name>`: Use only when the user names a profile.
- `--config <path>`: Use when the user provides a non-default config file.
- `--deployment <name>` and `--endpoint <url>`: Use when the user provides
  inline Azure OpenAI settings.

Examples:

```bash
azimg edit input.png "Make the background transparent" \
  --output-directory ./azimg-output/edits

azimg edit input.png "Replace the sky with sunset clouds" \
  --mask-file mask.png \
  --output-directory ./azimg-output/edits \
  --write-manifest
```

## Minimal unblockers

Use this order only when an image request is blocked or prerequisites are
unknown:

1. If `azimg` availability is unknown, run `azimg --help`.
2. If config/profile status is unknown, run `azimg config show`; do not
   overwrite config.
3. If setup is still blocking image creation, run `azimg doctor`.
4. If Azure authentication is specifically blocking image creation, run
   `azimg doctor --verify-auth`.
5. If no config exists and the user wants setup, run `azimg config init`
   without `--force`.
6. Use `azimg config init --force` only when the user explicitly asks to
   overwrite or reset an existing config.

## Validation reminders

- `--count` must be `1` through `10`.
- `--size` must be `WIDTHxHEIGHT`; dimensions must be positive, divisible by
  `16`, and no larger than `4096`.
- `--output-compression <n>` is available for image commands when compression
  is needed; valid range is `0` to `100`.
- `--name-template` supports `{timestamp}`, `{id}`, `{slug}`, `{index}`, and
  `{profile}`.
- Use `--` before a prompt or path that begins with `-`.

## Reporting back

- State whether you generated new images or edited an existing image.
- Include `manifest: <path>` when a manifest was written.
- If blocked, report the exact error and the smallest safe next step.
