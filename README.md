# linux-helper

**Quick Linux command help over HTTP — made for `curl`.**

- A ASP.NET Core Web API that returns plain-text summaries, examples, and package-manager cheatsheets for common Linux commands. 
- One endpoint, no database, no client install: ideal when you need a refresh and do not want to open a browser or dig through a full man page.

| | |
|---|---|
| **Hosted service** | [https://linux-helper.com](https://linux-helper.com) |
| **Source code** | [https://github.com/airamez/linux-helper](https://github.com/airamez/linux-helper) |

```bash
# Use the public instance — no install required
curl 'https://linux-helper.com/'
curl 'https://linux-helper.com/?q=ls'
curl 'https://linux-helper.com/?q=disk'
curl 'https://linux-helper.com/?list=full'
```

---

## Motivation

- Working on Linux often means the same cycle:
  - forget a flag
  - open a new tab
  - search Stack Overflow
  - skim a long man page
  - copy an example
  - close the tab.
- That is friction you feel constantly on remote servers, in containers, or on locked-down hosts where tools are missing.

**linux-helper** targets that gap:

- **No browser** or AI tools available
- **No `man` available** — minimal containers, recovery shells, restricted environments  
- **No desire for a full manual** — you want the *common* usage, not every option  
- **Terminal-first workflow** — you are already in a shell; `curl` should be enough  
- **Cross-distro package confusion** — apt vs dnf vs pacman for the same task  

The project stays intentionally small: load JSON at startup, answer HTTP GET with readable text, make adding a command a data change rather than a feature project.

---

## Hosted instance

- A public deployment is available at **[linux-helper.com](https://linux-helper.com)**.
- Point `curl` (or any HTTP client) at it from any machine with network access — useful on jump hosts, CI runners, and containers where you do not want to install a local help client.
- Examples
 
```bash
  curl 'https://linux-helper.com/'
  curl 'https://linux-helper.com/?q=chmod'
  curl 'https://linux-helper.com/?q=permission'
  curl 'https://linux-helper.com/?q=pacman'
  ```

>You can still **self-host** a private copy (see [Run locally](#run-locally)) when you need offline access, custom content, or traffic that must stay inside your network.

---

## What it does

| Capability | Description |
|------------|-------------|
| **Command list** | Grouped list of common tools with a one-line summary and a typical example |
| **Basic vs full** | Default home shows everyday commands; `?list=full` shows the complete catalog |
| **Detail pages** | Synopsis, options, examples, related commands for a single tool |
| **Search** | Match by command name, tag (`disk`, `network`, …), or any word in the description |
| **Package cheatsheet** | At the bottom of the home page: update / install / remove per major distro |
| **Plain text** | Designed for terminals — no HTML required, no JSON parsing needed |

- Content is stored as JSON (one detail file per command), loaded once at startup, and kept in memory.
- There is no database and no external API dependency for the core help text.

---

## Similar projects — and why this stays simple

There are excellent tools in this space. linux-helper does not try to replace all of them; it optimizes for **HTTP + curl + self-host + minimal surface area**.

| Project | What it is | Trade-off vs linux-helper |
|---------|------------|---------------------------|
| **[man](https://man7.org/linux/man-pages/)** | Official system manuals | Complete, but dense; often missing or incomplete in containers |
| **[tldr](https://tldr.sh/) / tealdeer** | Community example-style pages | Excellent UX; usually a **local client** + page cache to install/update |
| **[cheat.sh](https://cheat.sh/)** (`cht.sh`) | Huge cheat sheet service over HTTP/curl | Very powerful; broader and heavier, depends on a public service (or a larger self-host) |
| **[explainshell](https://explainshell.com/)** | Explains a full command line in the browser | Great for learning complex pipelines; web UI, not a tiny self-hosted API |
| **[bropages](http://bropages.org/)** | Example-centric pages | Similar spirit; different delivery model and ecosystem |
| **Distro wikis / blogs** | Deep guides | Not a uniform, scriptable interface |

**Why prefer linux-helper when you want simplicity:**

1. **One GET endpoint** — `GET /` and `GET /?q=…`; no multi-route API surface to learn  
2. **Plain text out of the box** — paste into a terminal; no browser, JS, or `jq` required  
3. **Self-contained data** — JSON files in the repo; edit and restart, no CMS or DB migrations  
4. **Public host + self-host** — use [linux-helper.com](https://linux-helper.com) immediately, or run your own  
5. **Works offline** after a private deploy — no phone-home required for the help corpus  
6. **Package management included** — distro update/install/remove in one fixed footer section  
7. **Easy to extend** — add a command by writing JSON (or regenerating from the Python helper); no C# change  

Use **tldr** when you want a polished local CLI. Use **cheat.sh** when you want the largest possible corpus. Use **linux-helper** when you want a **small, curl-friendly help service** — hosted for free at [linux-helper.com](https://linux-helper.com), or owned and customized on your own infrastructure.

---

## Run locally

**Requirements:** [.NET 10 SDK](https://dotnet.microsoft.com/download)

```bash
git clone https://github.com/airamez/linux-helper.git
cd linux-helper
dotnet run --project src/LinuxHelper
```

The API listens on `http://localhost:5000` by default (see `appsettings.json` / launch settings).

```bash
curl 'http://localhost:5000/'
curl 'http://localhost:5000/?list=full'
curl 'http://localhost:5000/?q=ls'
curl 'http://localhost:5000/?q=permission'
```

Self-hosting is useful for air-gapped networks, custom command sets for your team, or development against a local checkout.

### Docker (local)

```bash
docker build -t linux-helper .
docker run --rm -e PORT=8080 -p 8080:8080 linux-helper

curl 'http://localhost:8080/?q=ls'
```

### Deploy to Google Cloud Run

The repo includes a multi-stage `Dockerfile` (SDK build → ASP.NET runtime). It does **not** use Native AOT — a normal framework-dependent publish that runs with `dotnet LinuxHelper.dll`.

**1. Prerequisites**

- [Google Cloud SDK](https://cloud.google.com/sdk) (`gcloud`) installed and logged in  
- A GCP project with billing enabled  
- APIs: Cloud Run, Artifact Registry (or Container Registry), Cloud Build  

```bash
gcloud config set project YOUR_PROJECT_ID
gcloud services enable run.googleapis.com artifactregistry.googleapis.com cloudbuild.googleapis.com
```

**2. Build and deploy (Cloud Build → Cloud Run)**

From the repository root:

```bash
# One-shot deploy (builds the Dockerfile and deploys)
gcloud run deploy linux-helper \
  --source . \
  --region us-central1 \
  --allow-unauthenticated \
  --port 8080 \
  --memory 512Mi \
  --cpu 1 \
  --min-instances 0 \
  --max-instances 3
```

`--source .` uses the Dockerfile in the repo. Cloud Run sets `PORT`; the app binds to `http://0.0.0.0:$PORT`.

**3. Or build a local image and push**

```bash
# Example: Artifact Registry
REGION=us-central1
PROJECT_ID=YOUR_PROJECT_ID
REPO=linux-helper
IMAGE=$REGION-docker.pkg.dev/$PROJECT_ID/$REPO/linux-helper:latest

gcloud artifacts repositories create $REPO \
  --repository-format=docker \
  --location=$REGION \
  2>/dev/null || true

gcloud builds submit --tag "$IMAGE" .

gcloud run deploy linux-helper \
  --image "$IMAGE" \
  --region $REGION \
  --allow-unauthenticated \
  --port 8080 \
  --memory 512Mi
```

**4. Map a custom domain** (e.g. linux-helper.com)

In Cloud Run → your service → **Manage custom domains**, or:

```bash
gcloud run domain-mappings create \
  --service linux-helper \
  --domain linux-helper.com \
  --region us-central1
```

Then add the DNS records Google shows (often a CNAME or A/AAAA at your DNS host).

**Cloud Run notes**

| Topic | Detail |
|-------|--------|
| **PORT** | Injected by Cloud Run; the app listens on `0.0.0.0:$PORT` |
| **Cold starts** | `min-instances 0` saves cost; first request after idle may be slower |
| **Memory** | 512 Mi is a reasonable start for this API |
| **Auth** | `--allow-unauthenticated` for a public curl API |

---

## HTTP API

Single entry point: **`GET /`**

| Parameter | Aliases | Description |
|-----------|---------|-------------|
| `q` | `query` | Command name, tag, or search word |
| `list` | — | `basic` (default) or `full` |
| — | `full=1` / `all=1` | Shorthand for full list |

| Request | Response |
|---------|----------|
| `GET /` | Basic command list (grouped) + package management cheatsheet |
| `GET /?list=full` | All non-package commands + package cheatsheet |
| `GET /?q=ls` | Full detail for `ls` |
| `GET /?q=disk` | Commands tagged `disk` |
| `GET /?q=permission` | Free-text search across names and descriptions |

Lookup order for `q`:

1. Exact command name or alias  
2. Exact tag name  
3. Free-text search (summary, description, examples, options, …)  

Responses are **`text/plain`**.

### Example list line

```
ls     List directory contents  →  ls -lia
```

### Example home structure

```
LIST:  basic  (N commands)
      Use ?list=full for every command.

DIRECTORY/FILES
----------------------------------------------------
...

DISK
----------------------------------------------------
lsblk  List disks, partitions, and mount points  →  lsblk -f
df     Report file system disk space usage  →  df -h
...

Total: N.  ?q=<name> for details.

PACKAGE MANAGEMENT
----------------------------------------------------
Ubuntu                   apt
  update   sudo apt update && sudo apt upgrade
  install  sudo apt install <pkg>
  remove   sudo apt remove <pkg>
...
```

Command sections are sorted alphabetically. **Package management always appears last.**

---

## Contributing

The most valuable contributions are **content**: new commands, better examples, clearer descriptions, and package-manager notes for additional distros.

Repository: **[github.com/airamez/linux-helper](https://github.com/airamez/linux-helper)**

### Ways to help

- **Suggest a command** — open an [issue](https://github.com/airamez/linux-helper/issues) with the name, a short summary, and 1–3 real-world examples  
- **Improve existing entries** — fix typos, add common flags, or replace weak examples with better ones  
- **Extend the package cheatsheet** — add or refine update / install / remove lines in `Data/distros.json`  
- **Send a pull request** — JSON-only changes are welcome and usually do not require C# experience  

### Adding a command (PR)

**Option A — generator (handy for bulk edits)**

1. Add a `cmd(...)` entry in `scripts/generate_commands.py`  
2. Optionally add the name to `BASIC` and `PRIMARY_EXAMPLE`  
3. Run:

```bash
python3 scripts/generate_commands.py
```

4. Open a PR with the updated `Data/` files  

**Option B — hand-edit JSON**

1. Append an entry to `src/LinuxHelper/Data/commands.json`  
2. Create `src/LinuxHelper/Data/commands/<name>.json` with description, synopsis, options, and examples  
3. Open a PR — **no application code change required**  

### Package cheatsheet

Edit `src/LinuxHelper/Data/distros.json`:

```json
{
  "id": "arch",
  "name": "Arch / Manjaro",
  "packageManager": "pacman",
  "update": "sudo pacman -Syu",
  "install": "sudo pacman -S <pkg>",
  "remove": "sudo pacman -Rns <pkg>"
}
```

If you are unsure how to structure a PR, an issue with the raw content is enough — maintainers can help fold it into the catalog.

---

## Content model

```
src/LinuxHelper/Data/
  commands.json       # Index: name, summary, example, basic, tags, detailFile, aliases
  distros.json        # Package cheatsheet (per-distro update / install / remove)
  commands/
    ls.json           # Full detail for one command
    grep.json
    …
```

Roughly **100** commands are shipped, with a **basic** subset for the default home page and major distros in the package cheatsheet (Ubuntu, Debian, Fedora, RHEL family, Arch, openSUSE, Alpine).

---

## Project layout

```
linux-helper/
├── README.md
├── LICENSE
├── LinuxHelper.sln
├── scripts/
│   └── generate_commands.py    # Optional seed/regeneration helper
└── src/LinuxHelper/
    ├── Program.cs              # Single GET / endpoint
    ├── Models/
    ├── Services/
    │   └── CommandCatalogService.cs
    └── Data/                   # JSON catalog (copied to output on build)
```

Stack: **ASP.NET Core** (minimal hosting), **System.Text.Json**, in-process cache. No ORM, no Redis, no front-end build step.

---

## Design principles

- **Simplicity over completeness** — prefer the examples people actually use  
- **Terminal-native** — plain text, curl-friendly, greppable  
- **Data over code** — new help content should not require a feature branch of business logic  
- **Hosted and self-hostable** — [linux-helper.com](https://linux-helper.com) for convenience; your instance for control  
- **Discoverable** — home page lists commands; tags and search fill the gaps  
- **Community-extendable** — contributions of commands and examples are first-class  

---

## License

See [LICENSE](LICENSE).
