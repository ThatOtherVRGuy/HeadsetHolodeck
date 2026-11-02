# Provider audit — World Labs (Marble) and 3dAIStudio (Tripo)

Date: 2026-10-28. Requested by the owner: confirm we use the latest and best APIs/models, and that
every option (quality, size, cost) is reachable from the UI.

Method: our client was read directly; the published API surfaces were fetched live from both vendors'
documentation and OpenAPI references on the same day. Anything the docs contradict, or that could not
be verified, is called out explicitly rather than smoothed over.

**Headline:** both integrations still call valid, current endpoints with valid auth, and the Tripo
credit maths matches the vendor's published examples exactly. The problems are elsewhere: we are pinned
to an older Tripo model, we discard scale data that VR needs, our model picker is mislabelled and hides
cost, and most provider options never reach the UI.

---

## 1. World Labs Marble

### 1.1 Verified API surface

| Item | Value |
|---|---|
| Base host | `https://api.worldlabs.ai` |
| Version | `marble/v1` — the only published version; no v2 exists in the OpenAPI spec or docs index |
| Auth | `WLT-Api-Key` header |
| Generate | `POST /marble/v1/worlds:generate` |
| List | `POST /marble/v1/worlds:list` (cursor pagination, not offset) |
| Operation | `GET /marble/v1/operations/{operation_id}` |
| Export | `POST /marble/v1/worlds/{world_id}:export` — the **only** route to PLY |
| World | `GET /marble/v1/worlds/{world_id}` |
| Credits | `GET /marble/v1/credits` |
| Other | `DELETE /worlds/{id}`, `POST /media-assets:prepare_upload`, `POST /pano:depth_to_rgb` |

Sources: [OpenAPI spec](https://docs.worldlabs.ai/api/reference/openapi.md),
[quickstart](https://docs.worldlabs.ai/api.md), [generate](https://docs.worldlabs.ai/api/reference/worlds/generate.md),
[operations](https://docs.worldlabs.ai/api/reference/operations/get.md),
[export](https://docs.worldlabs.ai/api/reference/worlds/export.md).

**We are current.** Host, version, verbs and the POST-not-GET list are all correct.

### 1.2 Models and cost — our labels are misleading

Accepted `model` values ([models](https://docs.worldlabs.ai/api/models.md)):

| Marble | API value | Credits |
|---|---|---|
| Marble 1.1 Plus | `marble-1.1-plus` | 1,500 + 0–1,500 |
| Marble 1.1 | `marble-1.1` | 1,500 |
| Marble 1.0 | `marble-1.0` | 1,500 |
| Marble 1.0 Draft | `marble-1.0-draft` | 150 |

Our `MarbleModel` enum (Draft / Fast / Standard / High) is **our own vocabulary**, not the API's, mapped
in `WorldLabsModels.cs` to valid IDs. Nothing is broken, but the UI consequence is bad:

- **"Fast" resolves to legacy Marble 1.0 at 1,500 credits — ten times Draft (150).** A user choosing
  "Fast" pays standard price for an older model. This is the single most expensive UI mistake in the app.
- No cost is shown anywhere, so the 10× difference is invisible.
- The docs contradict themselves on the default (models page says `marble-1.0`, schema says
  `marble-1.1`). We always send an explicit model, which is the safe path — keep doing that.

### 1.3 Request options — what exists vs what we send

Generate accepts: `world_prompt` (required), `display_name` (≤64), `model`, `seed` (0–4294967295),
`tags` (≤10, ≤32 chars each), `permission {public, allow_id_access, allowed_readers, allowed_writers}`.

Prompt types: `text`, `image` (with `is_pano`: auto/true/false), `multi-image` (azimuth + up to 8 when
`reconstruct_images`), `video` (≤100 MB). Text and image prompts also take `disable_recaption`.

We send: `world_prompt` (text), `display_name`, `model`, and hardcoded
`tags: null, seed: null, isPublic: false` (`VoiceToWorldLabsPluginCoordinator.cs:321-323`).

**Verified absent from the API** (so we are not missing them, they do not exist): `resolution`,
`quality`, output format selection, panorama-vs-3D output mode, `negative_prompt`, `style`, `size`,
`aspect`, `upscaling`. Format is chosen at *export*; SPZ (100k / 500k / full_res) always comes back.

### 1.4 Response handling — two real defects

1. **`metadata.progress` was the wrong type.** Declared `float?`, but the API sends an object
   `{status: "IN_PROGRESS"|"SUCCEEDED", description}`. Our progress reporting could not work, and
   deserialization would fail as soon as the field appeared. **Fixed today** (`OperationProgress`),
   including the editor window and the browser status line, which now shows the real description.
2. **`semantics_metadata` is dropped.** `assets.splats.semantics_metadata` carries
   `metric_scale_factor` and `ground_plane_offset` — the documented way to get metric scale and a
   grounded world ([rendering SPZ](https://docs.worldlabs.ai/api/rendering-spz.md)). We ignore it, so
   worlds render at arbitrary scale and float. For a VR product this is a visible quality gap, and it is
   a small change: read two numbers, apply scale + Y offset to the render root.
   Related: Marble SPZ is `marble_raw_opencv` and Marble's own viewer applies a 180° X rotation — worth
   checking our axis convention against the same reference.

Smaller omissions: `MeshAssets` drops `hq_mesh_url` / `full_res_mesh_url`; `World` drops
`world_prompt`; `Permission` drops `allow_id_access`.

### 1.5 Limits and cost model

- Start rate: **~3/min and 60/hour** default (approved accounts ~30/min standard, ~90/min draft).
  Per account, not per key. `429` + `Retry-After`, and a 429 start is not charged.
- **~5 minutes per generation**; no per-model latency published.
- Operation `expires_at` ≈ 1 hour — after that the operation is gone, so `world_id` must be persisted
  and `GET /worlds/{id}` used instead. We already persist the config, which is the right shape.
- Credits: **$1 per 1,250**; draft world ≈ 150 credits (≈$0.12), standard ≈ 1,500 (≈$1.20),
  1.1-plus ≈ 1,500–3,000 (≈$1.20–2.40). Pano events 0/80/100. HQ mesh export 3,500 credits, max
  1 hour, 4/hour. PLY export free.
- **No free API tier** — Marble's consumer "Free" plan is a different balance.
- Our 600 s generation timeout is comfortable for standard, and may be tight for 1.1-plus (unverified).

### 1.6 SDK reality check

There is **no official Unity package** and no C# SDK. `com.worldlabs.gaussian-splatting` 1.1.0 is a
**third-party** wrapper (`github.com/nigelhartm/worldlabs_unity`, MIT) despite `author: WorldLabs` in its
manifest. The official Python client is explicitly "for experimentation, not production". Treat our
vendored layer as ours to maintain — which is what we have been doing.

---

## 2. 3dAIStudio (Tripo)

### 2.1 Verified API surface

| Purpose | Endpoint |
|---|---|
| Balance | `GET /account/user/wallet/` → `{"balance": "150.00"}` (string, not under `/v1`) |
| Text→3D | `POST /v1/3d-models/tripo/text-to-3d/` + optional `/3.0/`, `/3.1/`, `/3.2/` |
| Image→3D | `POST /v1/3d-models/tripo/image-to-3d/` |
| Multiview→3D | `POST /v1/3d-models/tripo/multiview-to-3d/` |
| P1 / P2 | `…/text-to-3d/p1/`, `…/p2/` and equivalents |
| Status | `GET /v1/generation-request/{task_id}/status/` |
| Download | `results[].asset` (plain GET of a storage URL) |
| Convert | `POST /v1/tools/convert/` — the only route to FBX/OBJ/USDZ/STL, 10 credits |

Auth `Authorization: Bearer <key>`. Source:
[REST reference](https://www.3daistudio.com/Platform/API/Documentation/3d-generation/tripo),
[credit balance](https://www.3daistudio.com/Platform/API/Documentation/credit-balance),
[convert](https://www.3daistudio.com/Platform/API/Documentation/tools/convert).

**We are current on endpoints and auth**, and our credit estimate matches the vendor's own worked
examples exactly:
- text→3D default = 20 base + 20 standard texture = **40** (we compute 40 ✓)
- image→3D default = 40 + 20 = **60** (we compute 60 ✓)

### 2.2 Model version — we are pinned to 3.0

The version is a **URL suffix**; omitting it means **3.0**. Vendor docs: *"new integrations should use
3.2"*; 3.2 keeps 3.1 geometry, adds the newest texture model, the `fast` texture tier and
`lighting_removal` ([tripo docs](https://www.3daistudio.com/Platform/API/Documentation/3d-generation/tripo)).

So we are silently generating with the previous-generation model. This is a **one-line fix** with a real
quality gain for the same credit cost.

Newer tiers exist (P1 premium, P2 native-quad) at 60–200 base credits. P2 launched ~September 2026 and
is the "game-ready" tier — relevant for a headset app, but at 5× the credits it is a product decision,
not an obvious switch.

There is also a **Prism 3.1** model documented on the vendor's newer docs site with a
`POST /v1/generate` example — that endpoint **404s** (verified). Prism is MCP/web-app only; do not
migrate on the strength of that example.

### 2.3 Options we never send

Text→3D accepts (all documented):

`prompt` (≤1024), `negative_prompt` (≤1024), `texture`, `pbr`, `texture_quality`
(`fast` 3.2-only / `standard` / `detailed` / `extreme`), `lighting_removal` (3.2), `texture_seed`,
`geometry_quality` (`standard` / `detailed`), `face_limit` (3.1/3.2: 500–1,500,000), `quad` (+10),
`smart_low_poly` (+20), `generate_parts` (+40), `export_uv`, `auto_size`, `compress`.

Image→3D adds `image` or `image_url`, `texture_alignment` (`original_image` / `geometry`),
`enable_image_autofix`.

We send only: `prompt`/`image`, `texture`, `pbr`, `texture_quality`, `geometry_quality` — and the last
two are **inspector fields, not UI**, fixed at `standard`.

Consequences worth fixing for a headset:

1. **No `face_limit`.** Budgets are adaptive and can be heavy for Quest. A sane headset cap
   (e.g. 10k–20k) is a one-field change with a large effect on frame cost.
2. **Quality is invisible and unchangeable** in the UI, despite the API exposing four texture tiers and
   two geometry tiers with known credit deltas.
3. **No output-format path.** Everything is GLB; FBX/OBJ/USDZ need `/v1/tools/convert/` (10 credits).

### 2.4 Operational risk — polling vs the rate limit

Published default rate limit is **3 requests/minute** (dashboard-configurable). The vendor FAQ also
recommends polling every 3–5 s — i.e. 12–20 requests/minute, which contradicts the limit. Whether
status polls are exempt is unverified. Our 5 s poll with a 600 s cap would 429 immediately under a
literal 3/min limit.

**Mitigation regardless of the truth:** treat `429` as retryable with backoff (honour `Retry-After`),
never as a fatal generation failure, and confirm the key's configured limit in the dashboard.

Also: **API results expire after 24 h**; the status endpoint then returns `410 Gone`. Our download
happens immediately, so this only matters if a task is resumed later.

### 2.5 Errors

`400 VALIDATION_FAILED`, `401 INVALID_API_KEY` (also `invalid_api_key` / `api_key_expired` /
`api_key_revoked`), `402 INSUFFICIENT_CREDITS`, `429 RATE_LIMITED`, `410` for expired results. Real
response shape observed: `{"error": "...", "error_code": "INVALID_AUTHORIZATION_HEADER"}` — a missing
header gives a *different* code from a bad key, and casing varies across pages, so compare
case-insensitively and match on both `error` and `error_code`.

Credits are auto-refunded on `FAILED`.

### 2.6 Documentation quality warning

The vendor runs **two** doc sites that disagree: `www.3daistudio.com/Platform/API/Documentation` is the
real REST reference (Tripo-centric, matches our client); `docs.3daistudio.com` is newer, Prism-centric,
and contains at least one example for an endpoint that 404s. Code against the former. Note the newer
site's capability table omits Tripo entirely — expect migration pressure toward Prism (MCP/OAuth, not an
API key) eventually.

---

## 3. UI exposure — the owner's question, answered

| Option | API supports | Our UI |
|---|---|---|
| Marble model tier | 4 models, 150–3,000 credits | Radio group exists, but labelled Draft/Fast/Standard/High with **no cost shown** and "Fast" = 10× Draft |
| Marble seed | yes | never sent, no UI |
| Marble public/private | yes | hardcoded private |
| Marble tags | yes | hardcoded null |
| Marble image / multi-image / video prompts | yes | not used (no camera-prompt path) |
| Marble export (PLY) | yes | not implemented |
| Tripo model version (3.0/3.1/3.2/P1/P2) | yes (URL suffix) | pinned to 3.0, no UI |
| Tripo texture quality (fast/standard/detailed/extreme) | yes | inspector-only, `standard` |
| Tripo geometry quality | yes | inspector-only, `standard` |
| Tripo face_limit / quad / smart_low_poly / parts | yes | not sent, no UI |
| Tripo negative prompt | yes | not sent, no UI |
| Tripo output format | via convert endpoint | not implemented |
| Credit balance / cost estimate | `/credits`, `/wallet` | a credit service exists and reports to the Ops status panel, but nothing shows cost *before* spending |

---

## 4. Recommended order of work

**P0 — before Friday's hardware session**
1. ✅ Fix `metadata.progress` typing (done today) — progress reporting was dead.
2. Pin Tripo to **`/3.2/`**, made configurable, defaulting to 3.2.
3. Send a **`face_limit`** suited to a headset.
4. Make Tripo **429 retryable with backoff**; confirm the key's rate limit.
5. Fix the model picker: show the real model and its credit cost; stop calling Marble 1.0 "Fast".

**P1 — Thursday, if time allows**
6. Apply `semantics_metadata` (`metric_scale_factor`, `ground_plane_offset`) so VR worlds are correctly
   scaled and grounded.
7. Surface Tripo quality/format options in the UI, with a credit estimate shown before generating.
8. Expose Marble `seed` and `permission`, and add the image-prompt path (the "camera prompt" idea).

**P2 — after the ship**
9. Marble export endpoint (PLY/GLB) and `GET /worlds/{id}` for `model` / `world_prompt`.
10. Complete the response models (`hq_mesh_url`, `full_res_mesh_url`, `world_prompt`, `allow_id_access`).
11. Provider fallback on auth failure; WorldLabs start-rate queue (3/min).
12. Revisit the `nonce` cache-buster on `worlds:list` (not in the schema; rejection vs ignore unverified).

---

## 5. Unverified — do not present these as facts

- Asset URL expiry/signing for Marble (`spz_urls`, `pano_url`, `thumbnail_url`); only the operation's
  `expires_at` is documented.
- Per-model Marble latency; concurrency caps for either provider.
- Whether unknown request fields are rejected or ignored by either API.
- Whether Tripo status polling counts against the 3/min limit.
- Whether Marble `GET /marble/v1/` (our health check) is a supported endpoint — it is not in the spec.
- Tripo P1 pricing is internally inconsistent in the vendor's own docs.
- Tripo credit expiry conflicts between two vendor pages (365 days vs never).
