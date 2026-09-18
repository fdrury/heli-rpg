# Local LLM dialogue — feasibility study for D-006

*Research date: 2026-09-18 · Target: ROTORWASH (Godot 4.7.2 / .NET 8 / Windows) · Hardware floor: GTX 1080 8 GB, 32 GB RAM (D-003)*

---

## Bottom line: **BUILD IT DIFFERENTLY**

The latency-cover trick is sound, it is validated by the two best-received LLM-NPC products in the shipped record (§5.4), and the arithmetic works with room to spare — a 1.7B model at Q4_K_M on a GTX 1080 via Vulkan produces a 40-token coda in roughly **0.5–0.9 s**, against a 3–6 s baked-line delivery window. That is a 6–10× margin, and the graceful-degradation story (no model → nothing lost) is genuinely correct. Prior art backs the feasibility hard: inZOI shipped a **0.5B** on-device model in a major commercial game, PUBG Ally ships a quantised **2B** inside the VRAM left over by a AAA shooter on 8 GB cards, and KRAFTON's own postmortem describes the identical two-layer "System 1 / System 2" split with the same latency technique. But three things in D-006 as written will break it, and all three are fixable now and expensive later. **(1) You cannot validate a stream you are already displaying** — "streams the coda" and "fails validation" are mutually exclusive as stated; the coda must be generated-then-validated, or validated at sentence boundaries, which changes the budget from "time to first token" to "time to last token". **(2) The coda must not be a conversational reply.** If the player's input is a menu choice, a frontier model can pre-bake every coda offline for free and layer (b) earns nothing; the local model only justifies itself against *unbounded game state* (fuel, airframe damage, what is bolted to Hugh, time since last visit, faction standing) — a combinatorial space too large to bake. Make the coda a **state observation, never an answer**, and the tonal-mismatch and contradiction failure modes mostly evaporate because the coda sits on a different axis from the baked line. **(3) VRAM is the thing that kills it, not latency** — and not by crashing, by making the game stutter. On an 8 GB Pascal card a Godot Forward+ scene will want 4–5.5 GB; a 1.7B model wants ~1.5 GB and fits, a 4B model wants ~3 GB and does not. Overcommit on Windows does not fail loudly, it silently spills over PCIe and evicts *the game's* textures. Recommendation: **build layer (a) as a complete, shippable game behind an `ICodaProvider` interface that is allowed to return `null` forever; build layer (b) as a time-boxed spike against that interface, opt-in, off by default, CPU-capable, gated on a real VRAM probe.** Budget the spike at two weeks and be genuinely willing to ship the `NullCodaProvider`.

**One non-technical caveat that may outweigh all of the above:** shipping a runtime generative model has been disclosable on Steam since 16 Jan 2026, and disclosed games measurably take ~53% fewer reviews and 40–60% lower sales — though that penalty was concentrated on established studios and barely registered for unknown solo developers (§5.7). ROTORWASH is currently non-commercial, which sidesteps most of it. Decide this deliberately rather than discovering it at store-page time.

---

## 1. Which model

### 1.1 The field as of September 2026

| Model | Params | License | Non-thinking? | Text-only? | IFEval | Q4_K_M on disk | Notes |
|---|---|---|---|---|---|---|---|
| **Qwen3-1.7B** | 1.7B (1.4B non-emb) | Apache-2.0 | hybrid — must disable | yes | ~70 (est.) | **1.11 GB** | 28L, 16Q/8KV heads, 32K ctx. The size/quality knee. |
| **Qwen3-4B-Instruct-2507** | 4.0B (3.6B non-emb) | Apache-2.0 | **yes, structurally** | yes | **83.4** | **2.50 GB** | 36L, 32Q/8KV, 262K ctx. Creative Writing v3 **83.5**, WritingBench 83.4. |
| Qwen3.5-2B | 2B (MoE + GatedDeltaNet) | Apache-2.0 | thinking default | **no — vision** | 61.2 | ~1.5 GB | 24L. IFEval *worse* than Qwen3-4B-2507 despite being newer. |
| Qwen3.5-4B | 4B (MoE + GatedDeltaNet) | Apache-2.0 | thinking default | **no — vision** | **89.8** | ~2.5 GB | Best IFEval in class, but thinking-by-default and a novel arch. |
| Qwen3.5-0.8B | 0.8B | Apache-2.0 | thinking default | no | — | ~0.6 GB | Emergency floor only. |
| **Gemma 4 E2B-it** | 5.1B raw / **2.3B effective** | **Apache-2.0** | yes | no (text+image+audio) | n/a published | ~2.8–3.2 GB | PLE architecture — *disk size follows raw params, not effective*. 128K ctx. |
| Gemma 4 E4B-it | 8B raw / ~4B effective | Apache-2.0 | yes | no | n/a | ~4.5 GB | Too big for the floor. |
| **SmolLM3-3B** | 3B | Apache-2.0 | dual-mode | yes | **76.7** | ~1.9 GB | Fully open data/checkpoints. 6 languages. |
| Llama-3.2-3B-Instruct | 3B | **Llama 3.2 Community** | yes | yes | 77.4 | ~2.0 GB | See §1.2 — licence is the problem, not the model. |
| Llama-3.2-1B-Instruct | 1B | Llama 3.2 Community | yes | yes | ~59 | ~0.8 GB | Weak persona adherence at this size. |
| Phi-4-mini-instruct | 3.8B | **MIT** | yes | yes | not published | ~2.3 GB | Cleanest licence of all. Heavily STEM-tuned; the model card itself admits it "does not have the capacity to store too much factual knowledge". Stiff, assistant-flavoured prose. |
| Ministral-3-3B-Instruct | 3B | Apache-2.0 | yes | multimodal | — | ~1.9 GB | Dec 2025, edge-targeted. Under-documented for roleplay. |

Sources: [Qwen3-4B-Instruct-2507 card](https://huggingface.co/Qwen/Qwen3-4B-Instruct-2507), [Qwen3-1.7B card](https://huggingface.co/Qwen/Qwen3-1.7B), [unsloth Qwen3-1.7B-GGUF file listing](https://huggingface.co/unsloth/Qwen3-1.7B-GGUF/tree/main), [unsloth Qwen3-4B-Instruct-2507-GGUF](https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF), [Qwen3.5-2B](https://huggingface.co/Qwen/Qwen3.5-2B), [Qwen3.5-4B](https://huggingface.co/Qwen/Qwen3.5-4B), [Gemma 4 E2B-it](https://huggingface.co/google/gemma-4-E2B-it), [Gemma 4 Apache 2.0 announcement](https://opensource.googleblog.com/2026/03/gemma-4-expanding-the-gemmaverse-with-apache-20.html), [SmolLM3-3B](https://huggingface.co/HuggingFaceTB/SmolLM3-3B), [Llama-3.2-3B-Instruct](https://huggingface.co/meta-llama/Llama-3.2-3B-Instruct), [Phi-4-mini-instruct](https://huggingface.co/microsoft/Phi-4-mini-instruct).

### 1.2 Licensing — the filter that eliminates half the field

You are redistributing ~1–3 GB of weights inside a game installer. That is *distribution*, and the terms matter.

| Licence | Redistribute weights in a game? | Obligations | Verdict |
|---|---|---|---|
| **Apache-2.0** (Qwen3, Qwen3.5, **Gemma 4**, SmolLM3, Ministral 3) | Yes, unambiguously | Ship `LICENSE` + `NOTICE`, retain attribution | ✅ Clean |
| **MIT** (Phi-4-mini) | Yes | Ship the licence text | ✅ Clean |
| **Llama 3.2 Community** | Yes, but | Must display **"Built with Llama"** prominently; any derivative model name must **begin with "Llama"**; must pass through the full agreement + Acceptable Use Policy to every recipient as an *enforceable term*; 700M MAU escape hatch (irrelevant here) | ⚠️ Viral-ish; forces branding onto your store page |
| **Gemma Terms of Use** (Gemma 3 and earlier) | Yes, but | Must propagate the Prohibited Use Policy as an enforceable provision in your EULA; Google reserves the right to restrict use remotely | ⚠️ Superseded — **Gemma 4 moved to Apache-2.0 on 2026-04-02**, so this only matters if you pin Gemma 3 |
| CC-BY-NC-4.0 (Tiny Aya) | Non-commercial only | — | ❌ Out if you ever charge |

**The Llama 3.2 licence is the practical disqualifier.** "Built with Llama" on a store page, plus an EULA clause binding your players to Meta's Acceptable Use Policy, is a real cost for a solo dev — and it is entirely avoidable given Apache-2.0 alternatives that score *better* on IFEval.

### 1.3 Recommendation

> **Primary: `Qwen3-1.7B` @ Q4_K_M (1.11 GB) or Q5_K_M (1.26 GB).**
> **Fallback / quality tier: `Qwen3-4B-Instruct-2507` @ Q4_K_M (2.50 GB), offered only when the VRAM probe says there is room.**
> **Emergency CPU floor: `Qwen3-1.7B` @ Q4_K_M with `n_gpu_layers=0` and a mandatory KV prefix cache.**

Reasoning:

- **Apache-2.0** — no branding obligations, no EULA pass-through, no Google reservation of rights.
- **Text-only.** Every Qwen3.5 and Gemma 4 small model is multimodal. You are paying disk, RAM and load time for a vision tower and (on Gemma 4 E2B) a ~300M audio encoder you will never call. For a 40-token text coda that is pure waste.
- **Qwen3-4B-Instruct-2507 is structurally non-thinking** — it *cannot* emit a `<think>` block. Every other high-IFEval candidate is thinking-by-default, which means one config mistake produces a 600-token reasoning dump and a 12-second stall. Removing that failure mode by construction is worth more than 6 IFEval points.
- **Creative Writing v3 83.5 / WritingBench 83.4** on the 4B is the only directly relevant published prose-quality number in the whole field. Qwen's post-2507 tuning is visibly less "assistant-brained" than Phi or Llama, which matters enormously for a dry, melancholy voice (see `00-vision.md` tone section).
- **1.7B is the knee.** It fits the VRAM budget (§4) with room for the renderer, it is fast enough to finish inside even a short baked line, and for the task as re-scoped in §6 — *render three supplied facts as one in-character observation* — it is sufficient. The 4B is better at voice, but 2× the VRAM and 2× the latency for a 40-token output.

**Do not use Qwen3-0.6B or Llama-3.2-1B.** Below ~1.5B, persona adherence collapses into generic wistful-NPC mush and the validator rejection rate becomes the dominant cost. Qwen3-0.6B is worth keeping only as a *classifier*, not a generator.

**Quantisation:** Q4_K_M is the right default (best quality-per-byte at this scale). At 1.7B, Q5_K_M costs only +150 MB and measurably reduces the "word salad under grammar constraint" failure — if the VRAM probe allows, prefer Q5_K_M for the 1.7B. Do **not** go below Q4: IQ3/IQ2 quants at 1.7B degrade instruction-following badly, which is precisely the capability you depend on.

---

## 2. Performance budget

### 2.1 Measured anchors (Llama-2 7B Q4_0, 3.83 GB — the standard llama-bench workload)

| GPU | Backend | pp512 (t/s) | tg128 (t/s) | Mem BW | FP32 |
|---|---|---|---|---|---|
| GTX 1080 Ti | CUDA | **1084.4** | **62.5** | 484 GB/s | 11.3 TF |
| GTX 1080 Ti | CUDA + FA | 1138.1 | 61.4 | | |
| GTX 1080 Ti | **Vulkan** | **585.5** | **67.8** | | |
| GTX 1070 Ti | CUDA | 714.4 | 37.8 | 256 GB/s | 8.2 TF |
| GTX 1070 | **Vulkan** | 321.6 | 41.5 | 256 GB/s | 6.5 TF |
| GTX 1060 | CUDA | 416.9 | 27.8 | 192 GB/s | 4.4 TF |
| **GTX 1080 (interpolated)** | **Vulkan** | **~450** | **~48** | **320 GB/s** | **8.9 TF** |
| GTX 1080 (interpolated) | CUDA | ~850 | ~41 | | |

Sources: [llama.cpp Vulkan scoreboard (disc. #10879)](https://github.com/ggml-org/llama.cpp/discussions/10879), [llama.cpp CUDA scoreboard (disc. #15013)](https://github.com/ggml-org/llama.cpp/discussions/15013), [GTX 1080 Ti for Local LLM (ariya.io, Feb 2026)](https://ariya.io/2026/02/gtx-1080-ti-for-local-llm/), [Vulkan vs CUDA (disc. #23109)](https://github.com/ggml-org/llama.cpp/discussions/23109).

**Pascal-specific finding, and it is counter-intuitive: on Pascal, Vulkan beats CUDA for token generation and loses badly at prompt processing.** On the 1080 Ti, Vulkan tg128 is 67.8 vs CUDA's 62.5 (+8%), while CUDA pp512 is 1084 vs Vulkan's 585 (+85%). Discussion #23109 reproduces the same split on a GT 1030. The reason is that GP104 has 1/64-rate FP16, so llama.cpp's CUDA path falls back to FP32 kernels tuned for tensor-core hardware that Pascal does not have, while the Vulkan backend's simpler FP32 shaders are closer to the memory-bandwidth roof. **Since a coda is a short prompt and a short generation, and since you will be prefix-caching the prompt anyway (§2.3), the generation-side win is the one that matters — Vulkan is the correct backend on the floor spec, and it has the enormous side benefit of also covering AMD and Intel with one binary.**

### 2.2 Derived small-model throughput on the floor spec

Token generation on GPU is memory-bandwidth bound: `tg ≈ (effective BW) / (bytes read per token)`. The GTX 1080 Vulkan anchor of 48 t/s × 3.83 GB gives an **effective bandwidth of ~184 GB/s** (57% of the 320 GB/s theoretical — typical for Vulkan on Pascal). Prompt processing is compute bound and scales roughly inversely with non-embedding parameter count. Small models lose some efficiency to kernel-launch overhead, so the figures below carry a derate (0.85 at 4B, 0.75 at 1.7B).

| Config | pp (t/s) | tg (t/s) | Basis |
|---|---|---|---|
| **GTX 1080 / Vulkan / Qwen3-1.7B Q4_K_M (1.11 GB)** | **900–1400** (est. 1250) | **100–140** (est. 124) | 184/1.11 × 0.75 |
| GTX 1080 / Vulkan / Qwen3-1.7B Q5_K_M (1.26 GB) | ~1150 | ~110 | |
| **GTX 1080 / Vulkan / Qwen3-4B-2507 Q4_K_M (2.50 GB)** | **450–650** (est. 610) | **55–70** (est. 63) | 184/2.50 × 0.85 |
| CPU 8-core / DDR4-3200 (51 GB/s th., ~32 GB/s eff.) / 1.7B Q4_K_M | 120–250 (est. 150) | **~29** | 32/1.11 |
| CPU 8-core / DDR5-5600 (90 GB/s th., ~55 GB/s eff.) / 1.7B Q4_K_M | 200–300 (est. 250) | **~50** | 55/1.11 |
| CPU 8-core / DDR4-3200 / 4B Q4_K_M | 50–110 (est. 70) | **~13** | 32/2.50 |
| CPU 8-core / DDR5-5600 / 4B Q4_K_M | 90–150 | **~22** | 55/2.50 |

> ⚠️ These are **derived, not measured**. The derivation is defensible and the anchors are real, but the very first thing the spike should do is run `llama-bench -m Qwen3-1.7B-Q4_K_M.gguf -p 512 -n 128 -ngl 99` on Fred's actual 1080 and replace this table with measurements. Expect ±30%.

### 2.3 How many tokens is a coda?

English averages ~1.33 BPE tokens per word for Qwen's tokeniser.

| Output | Words | Tokens |
|---|---|---|
| One short sentence | 8–12 | 11–16 |
| One sentence | 12–18 | 16–24 |
| Two sentences | 25–40 | **33–53** |
| **Design target** | ~30 | **40** (`n_predict` hard cap **48**) |

Prompt side:

| Block | Tokens | Cacheable? |
|---|---|---|
| System rules (voice, format, prohibitions) | 180–260 | ✅ static, cache once |
| NPC persona card | 120–200 | ✅ per-NPC, cache per slot |
| World-state fact block | 80–150 | ❌ changes every call |
| The baked line just delivered | 20–40 | ❌ |
| Player utterance / choice | 10–30 | ❌ |
| "Angle" directive + last 3 codas (anti-repeat) | 30–60 | ❌ |
| **Cold total** | **~600** | |
| **Warm suffix only** | **~180** | |

llama.cpp's server supports `--cache-prompt` (reuse KV from the previous request) and, better, **slot save/restore** via `POST /slots/{id}?action=save|restore`. The correct pattern is: on entering a conversation, build `system + persona`, run it once, `action=save` to a named slot; on every subsequent call, `action=restore` then submit only the ~180-token suffix. That turns prompt processing from a fixed 0.5 s tax into a rounding error. ([llama-server README](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md))

### 2.4 The arithmetic

**Warm path (persona prefix restored, 180 new prompt tokens, 40 output tokens):**

| Config | Prompt | Generate | Sampler+grammar (+12%) | IPC/JSON | **Total** |
|---|---|---|---|---|---|
| **1080 / Vulkan / 1.7B** | 180/1250 = 0.14 s | 40/124 = 0.32 s | 0.04 s | 0.02 s | **≈ 0.52 s** |
| 1080 / Vulkan / 4B | 180/610 = 0.30 s | 40/63 = 0.63 s | 0.08 s | 0.02 s | **≈ 1.03 s** |
| CPU DDR5 / 1.7B | 180/250 = 0.72 s | 40/50 = 0.80 s | 0.10 s | 0.02 s | **≈ 1.64 s** |
| CPU DDR4 / 1.7B | 180/150 = 1.20 s | 40/29 = 1.38 s | 0.17 s | 0.02 s | **≈ 2.77 s** |
| CPU DDR4 / 4B | 180/70 = 2.57 s | 40/13 = 3.08 s | 0.37 s | 0.02 s | **≈ 6.04 s** ❌ |

**Cold path (600 prompt tokens, no cache):** add 0.34 s (1080/1.7B), 0.69 s (1080/4B), 1.68 s (CPU-DDR5/1.7B), 2.80 s (CPU-DDR4/1.7B).

**Cover window:**

| Delivery | Rate | 9 words | 15 words | 25 words |
|---|---|---|---|---|
| Voiced speech | 150 wpm = 2.5 w/s | 3.6 s | 6.0 s | 10.0 s |
| Typewriter text | 40 chars/s (~7 w/s) | 1.3 s | 2.1 s | 3.6 s |
| Typewriter, slow | 25 chars/s (~4.3 w/s) | 2.1 s | 3.5 s | 5.8 s |

### 2.5 Is 3–6 s of cover enough? Yes — with two asterisks

| Path | Budget | 3 s cover | 6 s cover | Margin at 3 s |
|---|---|---|---|---|
| 1080 Vulkan, 1.7B, warm | 0.52 s | ✅ | ✅ | **5.8×** |
| 1080 Vulkan, 1.7B, warm, **+1 retry** | 1.04 s | ✅ | ✅ | 2.9× |
| 1080 Vulkan, 4B, warm | 1.03 s | ✅ | ✅ | 2.9× |
| CPU DDR5, 1.7B, warm | 1.64 s | ✅ | ✅ | 1.8× |
| CPU DDR4, 1.7B, warm | 2.77 s | ⚠️ marginal | ✅ | 1.1× |
| CPU DDR4, 1.7B, warm, +1 retry | 5.54 s | ❌ | ⚠️ | 0.5× |
| CPU DDR4, 4B, warm | 6.04 s | ❌ | ❌ | 0.5× |
| any path, **cold** | +0.3 to +2.8 s | varies | varies | |

**Asterisk 1 — short lines, not average lines, decide this.** A corpus of baked lines has a long left tail: "Yeah?", "Hm.", "Suit yourself." Those give you 0.6–1.5 s of cover, not 3–6 s. The margin table above is meaningless for them. **The implementable rule is: only request a coda when the selected baked line's estimated delivery duration ≥ (measured p95 coda latency × 1.5).** Compute the estimate at bake time and store it as a field on the line; measure p95 at runtime with a rolling window. This automatically means slow machines get codas only on long lines, which is exactly right, and it costs about fifteen lines of code.

**Asterisk 2 — first-token vs last-token.** D-006 says the model "streams" the coda during the baked line. If you intend to *display tokens as they arrive*, your budget is time-to-first-token (~0.2 s, trivially met) but you have forfeited validation. If you validate, your budget is time-to-**last**-token, which is what §2.4 computes. See §6.1 — the resolution is to validate at sentence boundaries, which recovers most of the streaming benefit without giving up the guardrails.

**Model load / warm-up is a separate, larger cost and must not touch the dialogue path.** Reading 1.11 GB off NVMe is ~0.5 s, but the Vulkan backend compiles its compute pipelines on first run, which on some Pascal drivers takes **10–30 s** (cached to disk afterwards). Start the subprocess at game boot behind the main menu or first loading screen, run one throwaway 8-token generation to force pipeline compilation and warm the allocator, and never unload. Budget **one** slow first-launch-after-install and say so in a loading tip.

---

## 3. Integration — how to run a GGUF model beside a Godot 4 C# game on Windows

### 3.1 The four options

| | **llama.cpp subprocess (HTTP)** | **LLamaSharp** | **Ollama** | **ONNX Runtime GenAI** |
|---|---|---|---|---|
| What ships | `llama-server.exe` + `ggml-*.dll` + model | NuGet `LLamaSharp` + `LLamaSharp.Backend.Vulkan` + model | **Nothing — the player installs it** | `Microsoft.ML.OnnxRuntimeGenAI` + EP native libs + ONNX model dir |
| Version (Sept 2026) | llama.cpp **v0.4.1** (14 Sep 2026; the project moved from `b####` builds to semver) | **v0.29.0**, tracking llama.cpp `815a2a59` | rolling | rolling |
| Licence | MIT | **MIT** | MIT | MIT |
| Player must install anything? | **No** | **No** | **Yes — dealbreaker** | No |
| Added install size | ~40–90 MB binaries + model | ~40–90 MB native + model | 0 (but ~1.5 GB external) | ~100–200 MB + model (ONNX weights are typically larger than equivalent GGUF) |
| Cap threads / GPU layers | **Yes** — `-t`, `-ngl`, `-c`, `-b`, `--cpu-mask`, `-dev` | **Yes** — same params via `ModelParams` | Partial — `num_gpu`, `OLLAMA_NUM_PARALLEL`, env vars only; it decides layer placement | Partial — EP options, no per-layer control |
| Constrained decoding | **Yes** — GBNF `grammar` + `json_schema` + `response_format` + `stop` over HTTP | **Yes** — `GrammarRule` / `SafeLLamaGrammarHandle` | JSON `format` only, **no GBNF** | **Weakest** — no GBNF |
| Prefix cache / slot save-restore | **Yes** — `--cache-prompt`, `POST /slots/{id}?action=save\|restore` | Manual (`StatefulExecutor`, state save/load) | `keep_alive` only; no slot control | Manual |
| **Crash isolation** | **Yes — separate process.** A native OOM or driver fault kills `llama-server.exe`, not the game | **No — in-process.** A native crash takes Godot down with it | Yes | No |
| Startup | Process spawn + model load + Vulkan pipeline build (one-off, then cached) | Same cost, in-process | Already running or ~2 s spawn | Model load + EP init |
| Pascal / GTX 1080 | ✅ Vulkan backend | ✅ `LLamaSharp.Backend.Vulkan` exists (Windows + Linux) | ✅ | ⚠️ DirectML works on Pascal but is the least-tested path; CUDA EP needs a matching toolkit |

### 3.2 Why each of the three losers loses

**Ollama is out on the first criterion.** D-006's premise is that the feature costs the player nothing and degrades invisibly. "Install a 1.5 GB third-party daemon, then `ollama pull` a model" is neither. It also cannot be redistributed inside the game, its model-eviction and `keep_alive` behaviour is outside your control (exactly the VRAM contention you most need to control, per §4), and it exposes no GBNF — which §6.8 depends on. Useful for *development* — it is the fastest way to A/B five models on a laptop — and nothing more.

**ONNX Runtime GenAI is out on constrained decoding.** No GBNF means §6.8's Layer 1 collapses to prompt engineering, and §5.5's Mantella evidence says prompt engineering does not hold at this model size (*"smaller language models struggle to follow instructions to avoid action descriptions"*). It also has a narrower model catalogue (official ONNX builds skew to Phi and a handful of Llama/Qwen conversions), larger on-disk weights, per-execution-provider native library packaging, and a DirectML path on Pascal that is the least-travelled road in the whole comparison. The one thing it would buy — a vendor-neutral GPU path on Windows via DirectML — Vulkan already buys you.

**LLamaSharp is the close second, and loses on one thing: it is in-process.** It is genuinely good — MIT, actively tracking upstream, a real Vulkan backend NuGet, full GBNF support, and the obvious choice for a C# project. But it loads `ggml` and the Vulkan driver into the *game's* address space. A native OOM, a driver reset, or an upstream regression is then a hard crash of Godot with a native stack trace, mid-dialogue, on a player's machine. For a feature whose entire justification is "if it fails, nothing is lost", giving it the ability to take the process down is the wrong trade. Secondary concerns compound it: native DLL resolution inside a Godot .NET export is a known source of friction (the backend's `runtimes/` folder must survive export and be found by `NativeLibrary` resolution), and you cannot cleanly unload or restart the backend without restarting the game.

### 3.3 Recommendation

> **Bundle `llama-server.exe` and talk to it over HTTP on `127.0.0.1` with an ephemeral port.**
> **Fallback: LLamaSharp with the Vulkan backend**, if the subprocess turns out to be a packaging or antivirus problem in practice.

The subprocess wins on exactly the axes this feature is judged on:

- **Process isolation is the whole argument.** `llama-server.exe` dying is a caught `HttpRequestException` and a `null` coda. That *is* D-006's graceful degradation, implemented for free by the operating system. Add a supervisor that restarts it at most twice per session and then gives up permanently and silently.
- **Full control of the knobs §4 needs:** `-ngl` set from the VRAM probe, `-t` from core count, `-c 2048`, `-b 256`, `--cpu-mask` to keep it off the render and physics cores, plus `SetPriorityClass(BELOW_NORMAL)` on the child.
- **Slot save/restore over HTTP** is the cheapest latency win available (§2.3) and the technique KRAFTON named in their postmortem (§5.3). LLamaSharp can do the equivalent but it is more code.
- **The dev loop is better.** You can point the game at a manually-started server, or at Ollama, or at a cloud endpoint, and iterate on prompts without rebuilding the game. The frontier-model bake pipeline (§7.1) and the coda harness (§6.6) can share one HTTP client with the runtime.
- **You can swap the whole backend by swapping an exe.** CPU build, Vulkan build, someone's CUDA build — same HTTP surface.

**Implementation notes that matter:**

| Concern | Handling |
|---|---|
| Port | Bind `127.0.0.1:0`, read the chosen port from stderr, or pre-probe a free port. Never a fixed port — two copies of the game, or a player already running something, must not collide. |
| Auth | `--api-key` with a per-launch random token, so nothing else on the machine can drive your model. |
| Orphan processes | Attach the child to a **Win32 Job Object** with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. If Godot crashes, Windows kills `llama-server.exe` for you. A `Process.Kill()` in a finaliser is not sufficient. |
| Console window | `CreateNoWindow = true`, `UseShellExecute = false`, redirect stdout/stderr to a rolling log under `user://`. |
| Antivirus / SmartScreen | An unsigned `.exe` spawning a child that opens a listening socket is a plausible false-positive. **Test this early** — it is the most likely reason to fall back to LLamaSharp. Sign the binaries if the project ever ships. |
| Startup | Spawn at boot behind the main menu, run one throwaway 8-token generation to force Vulkan pipeline compilation, and keep it alive for the session (§2.5). |
| HTTP client | One `HttpClient` for the session (never per-request), `HttpCompletionOption.ResponseHeadersRead` for streaming, `CancellationToken` wired to the abandon timer (§6.8 Layer 5) so cancellation actually aborts the request. |

**Not recommended: [NobodyWho](https://github.com/nobodywho-ooo/nobodywho).** It is the obvious "Godot LLM addon" answer and it is a real, well-made project — llama.cpp-backed, Godot 4.5+ via AssetLib, Vulkan/Metal, GGUF, grammar generation from function signatures, EUPL-1.2 (proprietary games are fine; modifications to NobodyWho itself must stay open). But its documented surface is **GDScript only**, with no C# bindings — which puts a GDScript interop layer between a C# game and the thing it depends on, for no benefit over calling `llama-server` directly. Worth watching; not worth adopting today.

---

## 4. VRAM contention — the thing that actually kills the feature

### 4.1 The budget on an 8 GB GTX 1080

| Consumer | Typical | Notes |
|---|---|---|
| Windows WDDM + desktop compositor + browser in background | 0.3–0.9 GB | Not yours to control. A player with Chrome open loses you ~500 MB. |
| Godot Forward+ render graph @ 1080p (HDR colour, depth, SSAO/SSIL subpasses, luminance reduction) | 0.15–0.30 GB | |
| Shadow atlas | 0.07 GB (4096²) – 0.27 GB (8192²) | Directly tunable via quality tier |
| Cluster builder / light + decal buffers | 0.05–0.10 GB | |
| **Textures, meshes, lightmaps — the real consumer** | **2.5–4.5 GB** | Scales with how dressed your scenes are |
| Godot pipeline/shader cache, staging buffers | 0.1–0.3 GB | |
| **Game subtotal (Medium tier, baked-lightmap path per D-003)** | **3.5–5.5 GB** | |
| **Headroom** | **2.5–4.5 GB** — *before anything goes wrong* | |

### 4.2 What the model costs

KV cache per token = `layers × kv_heads × head_dim × 2 (K+V) × bytes`.

| Model | Layers | KV heads × dim | KV/token | ctx 2048, f16 | ctx 2048, **q8_0** |
|---|---|---|---|---|---|
| Qwen3-1.7B | 28 | 8 × 128 | 112 KiB | 229 MB | **115 MB** |
| Qwen3-4B-2507 | 36 | 8 × 128 | 144 KiB | 295 MB | **148 MB** |

| Total VRAM, `-c 2048 -ctk q8_0 -ctv q8_0 -b 256 -np 1` | Weights | KV | Compute bufs | **Total** |
|---|---|---|---|---|
| **Qwen3-1.7B Q4_K_M** | 1.11 GB | 0.12 GB | ~0.25 GB | **≈ 1.5 GB** |
| Qwen3-1.7B Q5_K_M | 1.26 GB | 0.12 GB | ~0.25 GB | ≈ 1.6 GB |
| Qwen3-4B-2507 Q4_K_M | 2.50 GB | 0.15 GB | ~0.30 GB | **≈ 2.95 GB** |

**Verdict: 1.7B fits with ~1 GB genuinely spare on a Medium-tier scene. 4B does not fit on the floor spec and should only be offered on ≥12 GB cards.** Keep `-c` at 2048 — you are generating 40 tokens from a 600-token prompt; a 262K context window is dead weight you pay for in KV cache.

### 4.3 Why overcommit is worse than it sounds

On Windows, exceeding the VRAM budget does **not** produce an allocation failure you can catch. WDDM demotes resources to system memory and pages them over PCIe 3.0 x16 (~13 GB/s, versus 320 GB/s local). Two consequences, and the second is the dangerous one:

1. The model slows down 10–30×. Annoying but self-limiting — your abandon timer catches it.
2. **The driver may evict the game's textures instead of the model's weights.** You then get multi-frame hitches during camera movement in a dialogue scene, which the player will blame on your engine, not on a feature they never knew existed. This is the failure mode that ends the feature.

There is also a smaller, real risk: llama.cpp's Vulkan backend allocating a large device-local heap while Godot's renderer is mid-frame can cause a driver-level stall. Allocate **once, at boot, before the heavy scene loads**, and never resize.

### 4.4 Runtime detection — yes, there is a defensible way

Three options, in order of preference on the target hardware:

| Method | Scope | Availability | Verdict |
|---|---|---|---|
| **NVML** — `nvmlDeviceGetMemoryInfo` from `nvml.dll` (ships with every NVIDIA driver, in `System32`) | **Device-wide** total / free / used | NVIDIA only | ✅ Correct answer on the floor spec. P/Invoke, ~20 lines. |
| **DXGI** — `IDXGIAdapter3::QueryVideoMemoryInfo` → `Budget` and `CurrentUsage` | **Per-process** budget the OS is willing to give *you* | All vendors, Win10+ | ✅ Vendor-neutral fallback. Note it is per-process, so it does not directly tell you what a separate `llama-server.exe` will get — use `DedicatedVideoMemory − CurrentUsage − slack`. |
| `RenderingServer.GetRenderingInfo(VIDEO_MEM_USED / TEXTURE_MEM_USED / BUFFER_MEM_USED)` | What **Godot** is using | Everywhere | ⚠️ Useful as the numerator, but Godot does not report device free memory. Not sufficient alone. |
| llama.cpp `--list-devices` / `/props` (Vulkan `VK_EXT_memory_budget`) | Device heap budget/usage | Where the extension exists | ✅ Good cross-check, and it is the number the backend itself will act on. |

**The policy, concretely:**

```
ProbeAndDecide():
  1. Do NOT probe at boot. Probe after the heaviest representative scene
     has been loaded and rendered for ~10 s (streaming settled, atlases resident).
  2. free = NVML free  (or DXGI DedicatedVideoMemory - systemUsed)
  3. required = modelVram + 1.25 GB safety slack
       (slack covers: player alt-tabs to a browser, a later scene that is
        heavier than the probe scene, and driver-side overhead you cannot see)
  4. if free >= required(4B)  -> offer 4B, n_gpu_layers = all
     elif free >= required(1.7B) -> 1.7B, n_gpu_layers = all
     elif free >= 0.7 GB         -> 1.7B, n_gpu_layers = 14 of 28 (hybrid)
     else                        -> 1.7B, n_gpu_layers = 0 (CPU), threads = cores-2
  5. Persist the decision. Re-probe on quality-tier change or GPU change.
```

**And then add the safety net, because the probe will sometimes be wrong.** Sample Godot's frame time for 10 s with the model idle, then 10 s with it resident. If p99 frame time worsens by more than 2 ms, silently drop to `n_gpu_layers=0`; if it is still bad, disable the coda entirely for the session and log it. This is measurable, it is cheap, and it converts an invisible catastrophic failure into a visible graceful one — which is the whole premise of D-006 applied one level down.

### 4.5 Should it just run on CPU?

**On the floor spec, honestly: yes, default to CPU and treat GPU as an opt-in optimisation.** The costs:

| | GPU (1.7B) | CPU DDR4 (1.7B) | CPU DDR5 (1.7B) |
|---|---|---|---|
| Warm latency | 0.52 s | 2.77 s | 1.64 s |
| Cover needed | ~0.8 s | ~4.2 s | ~2.5 s |
| Fraction of baked lines long enough | ~95% | ~40% | ~70% |
| VRAM risk | **real** | **zero** | **zero** |
| RAM cost | — | ~1.6 GB of 32 GB | ~1.6 GB |
| Frame-time risk | texture eviction | 2 busy cores during a dialogue beat | same |

CPU costs you latency and therefore *coverage* (how often a coda appears at all), not correctness. Since the coda is strictly additive, degraded coverage is an acceptable outcome and VRAM-induced stutter is not. Pin the threads: `-t (cores - 2)`, and set the process to below-normal priority so the render and physics threads always win. With 32 GB of RAM the memory cost is irrelevant. **Default CPU, promote to GPU only when the probe is confident and the frame-time net stays clean.**

---

## 5. Prior art — what actually shipped, and what players said

This section matters more than the benchmarks, and it is mostly good news for D-006.

### 5.1 The record

| Product | Ship status | LLM location | Model / size | Player verdict |
|---|---|---|---|---|
| **inZOI "Smart Zoi"** (KRAFTON) | **Shipped** EA 27 Mar 2025. Game 79% / 14,307 reviews | **Local, on-device** | **Mistral NeMo Minitron 0.5B**, RTX-only | Feature widely disabled; "thought bubbles"; hallucinates events; **costs a graphics tier** |
| **PUBG "Ally"** (KRAFTON) | Beta / rollout H1 2026 | **Local, on-device** | **Mistral-NeMo-Minitron-2B**, further quantised, ≥**8 GB total VRAM** | Playtesters praised **memory** above all else |
| NARAKA: BLADEPOINT AI Teammate (NetEase) | **Shipped** Mar 2025 | Local | undisclosed | Gameplay assist, not dialogue. Little discourse. |
| Mecha BREAK (Amazing Seasun) | Shipped 1 Jul 2025 — **ACE dialogue does not appear to have shipped** | n/a | announced as Nemotron-4 4B | No AI disclosure on the Steam page; demo dialogue called *"underwhelming at best"* |
| **Whispers from the Star** (Anuttacon) | Shipped 14 Aug 2025. **Very Positive, 80% / 1,661** | Cloud (AWS) | undisclosed | **Best-received LLM-NPC game found.** Complaints: privacy, small talk, no agency |
| Suck Up! (Proxima) | 1.0 Oct 2025. **Mixed ~62% / 205** | Cloud (OpenAI) | GPT | Token system removed at 1.0 → visible quality regression → revolt |
| Vaudeville (Bumblebee) | 1.0 Nov 2025 after 2.5 yr EA. **Mixed 48% / 283** | Cloud | undisclosed | **Contradictions, hallucinated characters, forgetting** |
| Retail Mage (Jam & Tea) | Shipped Nov 2024. 79% / 44 | Cloud | undisclosed | Per-action latency; ~30 min of content |
| AI People (GoodAI) | Alpha Sep 2024 → **closed indefinitely Apr 2025** | Cloud, **$10/mo for 1,000 credits** | undisclosed | Never reached Steam. Dead. |
| Death by AI (Little Umbrella / Inworld) | **Shipped & profitable — ~20M players** | Cloud (Inworld) | Inworld stack | The one unambiguous commercial success — a party game where absurd output *is* the joke |
| Covert Protocol (NVIDIA × Inworld) | **Tech demo only**, GDC 2024; promised source release never appeared | Cloud | Inworld + Riva + Audio2Face | Press: key dramatic beats had to be scripted anyway |
| **Fortnite AI Darth Vader** (Epic) | Shipped 16 May 2025 | Cloud (Gemini 2.0 + ElevenLabs) | — | **Jailbroken within the hour.** See §5.6 |

**The honest read on NVIDIA ACE:** after three years and enormous marketing spend, its shipped footprint is **two features in two games from one publisher (KRAFTON), plus one gameplay-assist bot from NetEase.** Covert Protocol never shipped. Mecha BREAK's ACE dialogue appears not to have shipped. Treat ACE as a marketing programme with one genuinely useful engineering artefact attached (§5.3).

Also notable, and directly validating for §1: the **ACE Game Agent SDK's current default on-device SLM is Qwen 3.5 4B (GGUF)** — the same family this study recommends. NVIDIA's published footprint figure for Mistral-Nemo-Minitron-2B is *"fits in as little as 1.5 GB of VRAM"*, which matches the §4.2 estimate for Qwen3-1.7B almost exactly.

### 5.2 inZOI — the closest prior art, and its warnings

The only local LLM in a major commercial game. NVIDIA, verbatim: *"The NVIDIA ACE technology that powers Smart Zoi is a .5B billion parameter Mistral NeMo Minitron small language model."* Community-reported floor is effectively RTX 3000-series and up (works on RTX 3060 12 GB, unavailable on RTX 2060 Super).

**Enabling Smart Zoi auto-lowers every graphics setting by one tier.** Even on an RTX 4070 Ti Super, a player reports it dropping Ultra to High. Steam, verbatim:

> *"if I use SmartZOI I need to lower the graphic settings… I don't care what other Zois were thinking… I prefer it to play the game on ultra."*
> *"it really just gives you LLM based thought bubbles above their heads."*
> *"I've never noticed a difference in the actions."*
> *"It's a vendor locked solution."*

And the LLM-specific one: day-summary texts *"imagined things which never happen."*

**Four lessons, all of which D-006 should absorb:**

1. **0.5B was enough to ship.** Your coda as re-scoped in §6.3 is a *narrower* task than Smart Zoi's open-ended inner monologue. The §1 recommendation of 1.7B is, if anything, generous. This is strong evidence the feature is technically achievable.
2. **Never trade frames for the coda.** This is §4 restated by a shipped product's review section. inZOI's forced settings downgrade is the single most-complained-about aspect of the feature, and it is exactly the failure mode §4.3 predicts.
3. **Don't be vendor-locked.** RTX-exclusivity generated real resentment. A Vulkan GGUF backend with a CPU fallback avoids inZOI's biggest structural complaint for free.
4. **Never let it narrate world events.** *"Imagined things which never happen"* is the LLM-specific complaint. Constrain the coda to attitude and observation, never to *what happened*.

### 5.3 PUBG Ally — the best available engineering postmortem, and it describes D-006

NVIDIA/KRAFTON Q&A, 25 June 2026. Three findings transfer directly:

**(a) The architecture is a two-layer split, and it is structurally the same as D-006.** Verbatim:

> *"A System 1 layer, implemented as a behavior tree, handles fast, reactive gameplay… at game tick rate. A System 2 layer, the language model, handles the deliberate work."*

Your baked line is System 1. Your coda is System 2. And KRAFTON's stated hardest problem is the one this study keeps returning to: *"A significant amount of engineering effort went into defining the boundary between those two layers."* **Budget real design time for the boundary, not for the model.** §6.3 is that boundary.

**(b) KV-cache-aware prompt design is their named latency technique.** Verbatim: *"Stable instructions and gameplay context are kept as consistent as possible across turns, while only the most relevant real-time information is updated."* This is exactly the static-prefix / dynamic-suffix layout in §2.3, independently arrived at by a shipping team. It is the cheapest latency win available and it is not optional.

**(c) What playtesters actually loved was memory, not eloquence.** Verbatim: players *"asked Ally to look out for a Beryl in the first match, and from the next match onward Ally started finding it without being asked"*; one *"told Ally their name once, and Ally was already using it in the next match."*

> **This is the most actionable finding in the whole prior-art section.** The perceived magic is continuity, not prose quality. A coda that says *"still owe you for that filter"* three hours later beats a beautifully written generic line, and it costs a tiny per-NPC ledger of player deeds injected as 1–3 tokens of the dynamic suffix. Note that §7.4's "callbacks" finding says the same thing from the pre-baked side — **both layers agree that memory is where the value is.** Spend effort there.

**(d) Scope brutally.** KRAFTON shipped one map, one mode (Sanhok, AI Duo). Ship the coda on one settlement or one faction first.

### 5.4 Latency masking — D-006's central trick is under-exploited prior art, not untested

No GDC talk names the technique. Everything adjacent:

| Source | Technique | Outcome |
|---|---|---|
| **Whispers from the Star** | **Diegetic latency** — the round trip is written into the fiction as interstellar communication lag | **Very Positive, 80% / 1,661.** Reviewers note ~1.5 s delays and forgive them. Press: the game *"uses 'communication latency' to mask the time lag of AI response, incorporating this as a narrative device."* |
| **PUBG Ally** | Behaviour tree answers instantly; LLM catches up | Shipped |
| **Mantella** (Skyrim) | Streams **sentence-by-sentence to TTS** instead of waiting for full generation; moved the voice-model swap to run **in parallel with** the LLM request (issue #49, closed May 2025); exposes a `Wait Time Buffer` setting | The richest real-player-exposure system that exists, and it explicitly engineers for perceived latency |
| **arXiv 2511.10277** — *Fixed-Persona SLMs with Modular Memory* | Per-persona SLMs with runtime-swappable memory modules | Reports total latency ~5.5 s but **TTFT ~0.11 s**, and states the latency *"can be effectively masked by employing on-screen text rendering or TTS"* |
| **Convai** | **No latency masking documented at all** | The counterexample — savaged for exactly this: *"There's an all-too-noticeable pause after every question where the AI has to work up a response."* |
| Retail Mage | None | *"There is a pretty annoying loading time after each action while the AI calculates."* |

> **Verdict on the trick: sound, and validated by the two best-received systems in the record.** Every project that did a version of it survived its latency; every project that did not was specifically criticised for the pause. Note also that Mantella's sentence-by-sentence streaming is precisely the sentence-granular commit recommended in §6.2 — arrived at independently, under real player load.

### 5.5 Modded Skyrim — Mantella and CHIM/Herika

The deepest reservoir of real player experience. Mantella's pipeline is STT → LLM → TTS via SKSE, supporting local backends (KoboldCpp, text-generation-webui, LM Studio, Ollama) or any OpenAI-compatible API. Its documentation states the design priority plainly: **"long response times kill immersion."**

**The finding that should worry you most: Mantella's 2026 default is a cloud free tier** (OpenRouter with Gemma 4 26B A4B pre-configured). The flagship *local*-LLM mod ships pointing at the cloud. The CHIM community's repeated verdict: you can run it fully local for free, *"but your experience will be much better with paid API unless you're packing 64 GB VRAM."*

**The modding scene's lived conclusion after three years is that local models good enough for open-ended, in-character, lore-safe, long-memory RPG conversation do not fit in consumer VRAM.** D-006 is the correct *response* to that finding rather than a contradiction of it — you are not asking the local model to *be* the character. You are asking for two sentences of texture after a human-written line has already carried the semantic load. That is 10–50× easier and squarely inside 1.7B's competence. **But it only stays true if you hold the line on scope.** The moment the coda becomes a conversation, this evidence says you lose.

**Two failure modes from Mantella's own FAQ that you must engineer against, not prompt against:**

- *"NPCs keep describing actions out loud in-game"* — **"smaller language models struggle to follow instructions to avoid action descriptions."** Small models leak stage directions. Do not rely on prompt instructions at 1.7B. This is precisely why §6.8 Layer 1 uses a GBNF grammar that makes `*`, brackets and quotes lexically impossible rather than merely discouraged.
- *"NPCs keep repeating the same line of dialogue"* — caused by the next voiceline firing before the audio file is ready; the fix is raising `Wait Time Buffer`. **The baked-line → coda handoff is a race condition, and it will manifest as repeated or clipped lines.** Design it as an explicit state machine with a buffer, not as a callback that appends text whenever it happens to arrive.

### 5.6 The "second uncanny valley", and the strongest argument for keeping the coda short

Kotaku on the NVIDIA/Convai demo: an NPC asked about a tree replied *"The tree is the hologram of Yggdrasil, the Norse realm tree"* — delivered *"in the most boring manner possible"*; *"It's like no one in Neo City has ever heard of tone or pitch."* Aftermath found Convai NPCs behaved like **"glorified wiki entries, dispassionately dispensing info while displaying little in the way of personality"**, and recorded an NPC agreeing to follow the player and then simply not doing it.

That last detail produced the coinage: the **"second uncanny valley"** — once an NPC can converse freely, players expect it to *act* on what it says, and the gap between conversational competence and behavioural competence is more jarring than no conversation at all. CD Projekt Red's Paweł Sasko, on authored vs. generated dialogue: the disparity is *"like a canyon."*

> **This is the strongest external argument for the guardrails in §6.8.** A 1–2 sentence observation makes no promise the engine must keep. The moment your NPC says *"I'll meet you at the bridge"*, you have created a bug — and the proper-noun whitelist plus the commitment blocklist exist precisely to make that sentence unrepresentable.

**And the safety case — Fortnite's AI Darth Vader (16 May 2025).** Gemini 2.0 + ElevenLabs, voice cloned with the estate's authorisation, **jailbroken within the hour**: profanity on a streamer's video, then homophobic slurs ~40 minutes later. Epic hotfixed in ~30 minutes. SAG-AFTRA filed an unfair labour practice charge with the NLRB.

A *local* model is **more** jailbreakable than Gemini, not less, and it runs on hardware the player controls. But D-006 as re-scoped in §6.3 is **structurally immune**, because the player never types free text at the model — the coda is generated from game state. That removes the entire prompt-injection surface. It is worth being deliberate about keeping that property.

### 5.7 The commercial and cultural risk — possibly the biggest finding here

**Steam disclosure is mandatory and specifically catches this feature.** Valve rewrote the rules on **16 Jan 2026**: AI *development* tools no longer trigger disclosure, but **live generation of content during gameplay — images, audio, or text — requires explicit disclosure** on the store page. A runtime LLM coda is unambiguously in scope. (inZOI's disclosure covers only generative textures/3D/motion and does not mention Smart Zoi at all — do not copy that.)

**The measured penalty:**

| Finding | Figure |
|---|---|
| AI-disclosed games get fewer reviews | **~53% fewer**, and those skew negative |
| Sales drop, **established studios** | **40–60%** |
| Sales drop, **inexperienced devs with no marketing budget** | **"hardly any negative impact"** |
| Sample | 9,879 games, Jan–Oct 2025, spam & F2P filtered (Game Oracle, via PC Gamer) |
| Steam titles disclosing genAI | 1,000 (2024) → **7,818 (Jul 2025)** ≈ 7% of catalogue, ~1 in 5 of 2025 releases |
| Developer sentiment | **47%** of devs say genAI will *lower* game quality, up from 34% in 2024; only 11% positive |

Review-bombing precedents are real and do not require the AI to be any good, or even present: **Party Animals** took **828 negative reviews** merely for announcing an *AI-themed video contest*; **Shrine's Legacy** was review-bombed for *suspected* AI it says it did not use.

**What this means for ROTORWASH specifically.** `ATTRIBUTIONS.md` currently states the project is non-commercial, which sidesteps the sales question entirely — and the stigma penalty measured was concentrated on established studios, with little measurable effect on unknown solo developers. But reputational risk is not sales risk, and the framing still matters. The defensible position, which happens to be the literal truth of D-006:

> *"Every line of dialogue in this game is written by a human and checked by a human. An optional on-device model can add a short personalised reaction on top. It runs entirely on your PC, nothing is ever sent anywhere, and the game is complete with it switched off."*

That sentence answers the disclosure requirement, the privacy complaint (Whispers' top negative review is about data collection), and the stigma objection simultaneously. It is only honest if the feature is genuinely **off by default and genuinely optional** — which is another reason to build layer (a) as a complete game first.

### 5.8 Ranked player complaints, and whether D-006 dodges each

| # | Complaint | Evidence | Does the design dodge it? |
|---|---|---|---|
| 1 | **Contradiction / hallucinated facts / forgetting** | Vaudeville 48%: *"tell me one thing and then in the very next sentence… completely different and contradictory"*; inZOI *"imagined things which never happen"* | **Yes — if the coda is forbidden from asserting facts** (§6.3, §6.8). This is the core constraint. |
| 2 | **Performance cost / graphics trade-off** | inZOI auto-downgrades one tier; *"not worth the performance loose"* | **Only if** §4's probe + frame-time safety net are built. Otherwise no. |
| 3 | **Latency reading as a loading screen** | Retail Mage, Convai | **Yes — this is the entire point of the design.** |
| 4 | **Novelty decay / shallowness** | Retail Mage *"~30 mins"*; Suck Up! *"repetitive"*; Whispers *"small talk… very little value added to the plot"* | **Yes** — a garnish on an authored RPG, not a genre. |
| 5 | **Flat, generic, toneless prose** | Kotaku *"the most boring manner possible"*; Sasko *"like a canyon"* | **Partially.** Mitigate with per-NPC few-shot examples drawn from your own vetted baked lines, so the coda inherits your voice. |
| 6 | **Small models leak stage directions** | Mantella FAQ, verbatim | **No — engineer for it.** GBNF grammar, not prompting. |
| 7 | **Handoff race conditions → repeated/clipped lines** | Mantella `Wait Time Buffer` | **No — this is the highest-risk implementation bug.** Explicit state machine. |
| 8 | **Cost / tokens / subscriptions / shutdown** | Suck Up! revolt; AI People $10/mo → closed | **Yes — local eliminates it entirely.** This is D-006's hard constraint and it is correct. |
| 9 | **Vendor lock-in** | inZOI RTX-only | **Yes**, given Vulkan + CPU fallback. |
| 10 | **"AI in my game" backlash regardless of quality** | 53% review drop; Party Animals; Shrine's Legacy | **No. Unavoidable.** Disclose, default OFF, lead with the hand-written line. |
| 11 | **Safety / jailbreak** | Fortnite Darth Vader | **Yes, structurally** — no free-text player input reaches the model. |
| 12 | **Privacy** | Whispers top negative review | **Yes — local inference is a genuine marketing advantage.** Say so loudly. |

### 5.9 Research gaps, flagged honestly

Nexus Mods returns 403 to automated fetch and Reddit is hard-blocked in this environment, so the **Mantella/CHIM comment-level complaint corpus — the single richest source for this question — is under-sampled**. A manual pass over `nexusmods.com/skyrimspecialedition/mods/98631?tab=posts`, mod `126330`'s posts tab, and an `r/skyrimmods` search for "Mantella"/"CHIM" is worth an hour before committing to the spike. No GDC talk on LLM latency masking was located; GDC Vault's search is not machine-fetchable. KRAFTON's own engineering statements about inZOI's 0.5B model (quantisation, tokens/sec, frame-time budget) exist only in Korean-language sources as far as this search reached, and would be the highest-value follow-up.

---

## 6. The hybrid design itself — an honest critique

### 6.1 What is right about it

The core insight is correct and, as far as the prior art shows (§5), under-used. Every shipped LLM-NPC system's worst review line is about waiting. D-006 makes the wait structurally invisible instead of trying to make it short, and the fallback is *the thing you were going to ship anyway*. That is the right shape for a risky feature: it can only ever be additive, and it is killable at any point up to and including release day. Keep that property; it is the most valuable thing in the design.

The zero-token-cost constraint is also right, and it is a harder constraint than it looks — §5 shows that it is the constraint that killed most of the prior art commercially.

### 6.2 Flaw 1 — "streams the coda" and "fails validation" cannot both be true

You cannot un-display text. As written, the design either streams (and cannot validate) or validates (and cannot stream). The budget in §2.4 is time-to-**last**-token for exactly this reason.

**Fix: sentence-granular commit.** Generate with streaming on; buffer tokens; when a sentence-terminating token arrives, run the validator on that complete sentence; if it passes, hand it to the typewriter and keep generating the second; if it fails, abort the whole coda and let the baked line end naturally. You get most of the perceived responsiveness and you never display unvalidated text. Because the coda is capped at two sentences, worst case is one sentence displayed and the second discarded — which reads as a person choosing not to elaborate, not as a bug.

### 6.3 Flaw 2 — the coda must not be a reply, or layer (b) is redundant

This is the load-bearing critique. Ask what the coda is conditioned on:

- **If it is conditioned on the player's dialogue choice** (a menu enum, as in New Vegas): the space is `lines × choices`, which is finite, and a frontier model can bake every single one offline, hand-vetted, at zero runtime cost and infinitely better quality. Layer (b) earns nothing.
- **If it is conditioned on free-text player input**: you are building a parser game, which is a different and much larger design commitment, and a 1.7B model is the *worst* place to put the burden of understanding arbitrary input.
- **If it is conditioned on unbounded game state**: fuel remaining, hours since last visit, what is currently bolted to Hugh, which rotor blade is chewed, what you traded last time, faction standing deltas, whether you arrived at night in the rain with the doors off. **This space is genuinely combinatorial and cannot be baked.** This is where the local model earns its place, and nowhere else.

> **Recommendation: the coda is a *state observation*, never an answer.**
> The baked line answers. The coda notices.
> *"Depends what you're carrying."* → **"That tail boom's held together with wire and optimism, by the way."**

This single reframe is worth more than any guardrail, because it removes whole failure classes by construction:

- **Tonal mismatch** shrinks, because observation and reply are different registers anyway; a slight shift reads as the character glancing away mid-thought.
- **Contradiction of the baked line** becomes almost impossible, because they are on different axes — the coda is not asserting anything about the topic under discussion.
- **Quest promises** become off-task by definition; the model is not in the business of offering anything.
- **Validation becomes tractable**, because you know what facts were supplied and can check that nothing else appeared.
- **The task drops to 1.7B's weight class.** "Render one of these three supplied facts as a single dry in-character sentence" is a rendering task, not a conversation task. Small models are good at rendering and bad at conversing.

It also fits ROTORWASH specifically: D-011 makes the airframe's configuration player-visible and mechanically real, D-012 makes it a character, and D-007 makes fuel and wear constantly varying. You already have a rich, numerically-exact, combinatorially-huge state vector that NPCs should plausibly notice. That is the feature.

### 6.4 Flaw 3 — repetition is the failure mode you will actually ship with

Everything else on the risk list is a tail event. Repetition is a certainty. A 1.7B model at temp 0.8, given a tight persona and a 40-token cap, will converge on a handful of cadences per NPC within a dozen samples — "Stay sharp out there.", "Don't get yourself killed." The Skyrim bark problem, arrived at from the opposite direction. Three mitigations, in order of leverage:

1. **Forced angle rotation (highest leverage, nearly free).** Pass a designer-authored `angle` directive chosen by a shuffle-bag per NPC: `AIRCRAFT_CONDITION`, `WEATHER`, `WHAT_YOU_BROUGHT`, `TIME_SINCE_LAST_VISIT`, `SOMEONE_ELSE_IN_TOWN`, `THEIR_OWN_SITUATION`, `ABSTAIN`. The model cannot repeat itself across axes it is forbidden to use. Including `ABSTAIN` in the bag is important — it makes silence a *designed* outcome rather than a failure.
2. **DRY sampler with a long range** (`--dry-multiplier 0.8 --dry-base 1.75 --dry-allowed-length 2`), seeded with the last few codas from this NPC in context as text to be penalised.
3. **Trigram-Jaccard rejection** against the last 20 codas for that NPC, threshold ~0.5. Cheap, deterministic, and it catches what sampling does not.

### 6.5 Flaw 4 — the coda must never become canon

Hard architectural rule: **codas are write-only.** They are never persisted to the save, never fed back as game state, never referenced by later authored dialogue, and never re-entered into the model's own context except verbatim as text to avoid repeating. The moment a coda can influence anything, a 1.7B model is an authority on your fiction and your save files are non-deterministic. Enforce this in the type system: the coda returns a `string` to the presentation layer only, and the presentation layer has no write access to world state.

### 6.6 Flaw 5 — you cannot QA a non-deterministic feature by playing the game

You need an offline harness or this never ships with confidence:

- Enumerate the state cross-product (NPC × angle × discretised state buckets), generate 20–50k codas with fixed seeds, run the validator.
- Report: **validator rejection rate** (target < 15% — much higher means the prompt is wrong; near 0% means the validator is not doing anything), **abstain rate**, **mean length**, **trigram diversity**.
- Human-audit a random 500 of the *passing* outputs. Gate: **< 1% "this breaks the fiction"**. If you cannot hit that, the feature does not ship.
- Re-run the whole harness on every model, quant, prompt or grammar change. It is the regression test.

This harness is also, conveniently, the tool that generates and vets the baked corpus in §7. Build it once.

### 6.7 Flaw 6 — the seam is a presentation problem, not a model problem

Two concrete rules:

- **Never mix delivery channels.** If the baked line is voiced and the coda is text, the seam is a klaxon. Either both are typewriter text, or you need TTS for the coda — and local TTS adds 300–800 MB, another 0.3–1.0 s of latency, and a voice that will not match your VA. **For a solo dev the answer is: the whole game is typewriter text, and the coda is indistinguishable in presentation.**
- **Never break the box.** Append the coda into the same text box, after a full stop and a single space, with the same typewriter rate. No fade, no delay beyond the natural one, no separate bubble. The player should have no rendering cue that anything changed.

And accept that some players will notice anyway and dislike it on principle (§5). That is what the opt-in default is for.

### 6.8 The guardrail scheme, concretely

**Layer 1 — constrained decoding (GBNF).** This is the strongest tool and llama.cpp exposes it over HTTP via the `grammar` field on `/completion` and `json_schema` / `response_format` on `/v1/chat/completions`.

```gbnf
root     ::= "NONE" | coda
coda     ::= sentence (" " sentence)?
sentence ::= word (sep word){2,17} [.!?]
sep      ::= " " | ", " | " — "
word     ::= [A-Za-z] [A-Za-z'’-]{0,15}
```

What this makes *impossible*, not merely unlikely: markdown, bullet lists, emoji, stage directions in asterisks, quoted dialogue, newlines, code, URLs, more than two sentences, sentences longer than 18 words, **and all numerals** — which matters because invented numbers (distances, prices, times, headings) are the single most common small-model confabulation and the most immediately fiction-breaking in a game about navigation and fuel. Add `"NONE"` as a legal root so abstention is a first-class output rather than a malformed one.

**Layer 2 — sampling and stops.** `n_predict: 48`, `temperature: 0.75`, `top_p: 0.9`, `min_p: 0.05`, `repeat_penalty: 1.1`, DRY as in §6.4, `stop: ["\n", "Player:", "<|im_end|>"]`. Seed deterministically from `(npcId, conversationId, turnIndex)` so bug reports are reproducible.

**Layer 3 — post-validation (all must pass).**

| Check | Rule | Catches |
|---|---|---|
| **Proper-noun whitelist** | Every non-sentence-initial capitalised token must be in `AllowedNouns` = {NPCs the player has met, discovered place names, faction names, "Hugh", known part names}. Reject otherwise. | **Invented people, places and quests.** The strongest single check — a fabricated quest needs a name, and this forbids names. |
| Commitment blocklist | Regex on `meet me`, `I'?ll (mark|show|bring|pay|give|wait)`, `come (back|find me)`, `I have (a|some) (job|work)`, `reward`, `if you (bring|get) me` | Promised quests that do not exist |
| Fact grounding | Every claim-bearing noun phrase must trace to a supplied fact token. Cheap version: reject if the output contains a content word from a curated `WorldNoun` vocabulary that was *not* in the fact block. | State contradiction |
| Length | ≤ 48 tokens, ≤ 2 sentences (grammar enforces; re-check) | Runaways |
| Repetition | Trigram Jaccard ≤ 0.5 vs last 20 codas for this NPC | The bark problem |
| Register | Reject second-person imperatives outside a small allowed set; reject question marks if the baked line ended in a question | Coda hijacking the conversation |
| Profanity / content | Small blocklist tuned to the game's rating | Rating compliance |

**Layer 4 — retry policy.** At most **one** retry, only if `remainingCoverMs > p95LatencyMs × 1.3`, with a different `angle` from the bag and a different seed. On second failure, emit nothing and log. **Never** retry more than once: the expected value of a third attempt is near zero and the tail latency risk is real.

**Layer 5 — the abandon timer.** A hard wall-clock deadline set to `min(remainingCoverMs, 4000)`. On expiry, cancel the request, discard, log. The player never waits for this feature. Not once.

### 6.9 The handoff is a state machine, not a callback

Mantella's most-reported bug — *"NPCs keep repeating the same line of dialogue"* — is a race between the generated content arriving and the next line firing (§5.5). The same race exists here, and appending text "whenever it arrives" will reproduce it. Model the handoff explicitly:

```
Idle → BakedPlaying(lineId, startMs, estDurationMs, codaTask)
     → BakedDone            // coda not ready: close the box, cancel the task
     → CodaValidating       // coda arrived before baked line ended
     → CodaPlaying          // typewriter continues into the same box
     → Idle
```

Hard rules: **exactly one in-flight request per conversation**; advancing the conversation cancels the in-flight task *and discards its result* (never let a stale coda land on the next line); the coda may only be appended while the state is `BakedPlaying` or `CodaValidating`; and the typewriter owns the text buffer — the model's callback posts to a queue that the typewriter drains on the main thread. Cancellation must be real (`CancellationToken` through to an HTTP abort or a `llama_decode` interrupt), not a flag that is checked after the fact.

### 6.10 What to actually spend the quality budget on

Both halves of this study independently reached the same conclusion, from opposite directions. PUBG Ally's playtesters singled out **memory** — *"told Ally their name once, and Ally was already using it in the next match"* (§5.3). And the pre-baked research found that **callbacks** — one line in hour 20 referencing something you did in hour 3 — buy more perceived intelligence per word than any amount of variation (§7.4).

> **The perceived magic is continuity, not prose quality.** A grammatically plain coda that says *"still owe you for that filter"* beats a beautifully turned generic one, every time. Concretely: keep a small per-NPC ledger of player deeds (what you traded, what you promised, what state Hugh was in last time, how long ago), inject 1–3 entries into the dynamic suffix, and let the `angle` bag include `WHAT_YOU_DID_LAST_TIME`. This is cheap, it is the thing players notice, and — importantly — it is a capability the baked layer *cannot* easily reach, which is further justification for layer (b) existing at all.

---

## 7. The baked layer — the part that is actually the game

### 7.1 How big a corpus is realistic

Shipped-RPG reference points:

| Game | Words | Lines | Source quality |
|---|---|---|---|
| Baldur's Gate 3 (2023) | **2,503,837** (2,121,425 dialogue) | 173,642 voice files, ~2,068 speaking chars | [Guinness World Record](https://www.guinnessworldrecords.com/world-records/764696-longest-script-for-a-videogame) |
| Disco Elysium (2019) | **>1,000,000** | 1,501 conversations; 10,700+ skill checks; **45,000+ condition/script entries** | Dev-stated + [Disco Elysium Scribe](https://disco-elysium-scribe.pages.dev/) datamining |
| Planescape: Torment (1999) | ~800,000 | — | [Wikipedia](https://en.wikipedia.org/wiki/Planescape:_Torment) |
| Fallout 4 (2015) | — | 111,000 lines | Bethesda-stated |
| **Fallout: New Vegas (2010)** | — | **65,000+ recorded lines** | [Guinness / Wikipedia](https://en.wikipedia.org/wiki/Fallout:_New_Vegas) — in an **18-month** dev cycle |
| Left 4 Dead 2 | — | **~10,000 lines** | [Ruskin GDC 2012 slides](https://archive.org/stream/valve-publications/2012/GDC2012_Ruskin_Elan_DynamicDialog_djvu.txt) |
| Citizen Sleeper 1/2 | *no published figure* | — | Checked GDM, Rascal News, SuperJump interviews |

**The most important number in that table is not a word count. It is Disco Elysium's 45,000+ condition entries against 1,501 conversations — ~30 conditions per conversation.** The cost centre of a state-conditioned corpus is the metadata, not the prose. That is what a solo dev under-budgets.

**Generation is free; vetting is the ceiling.** Proofreading runs at ~180 wpm on a monitor ([Wikipedia, citing Trauzettel-Klosinski & Dietz 2012](https://en.wikipedia.org/wiki/Words_per_minute)), and professional developmental editing runs 750–3,375 words/hour ([EFA 2025 rate survey, n>1,100](https://www.the-efa.org/rates/)). But vetting frontier-model dialogue is *not* proofreading — the prose is already clean, and that is the trap. It is developmental editing + continuity QA + metadata authoring: does he sound like himself, does it contradict D-008, does he know something he shouldn't, is the condition tag right, is this line #4,412 again.

**Tier your corpus — the rates differ by an order of magnitude:**

| Tier | Content | Words/line | Effective vet rate |
|---|---|---|---|
| **A — Critical** | Named companions, quest-bearing, authored beats | 40–80 | **600–900 w/hr** (~10–20 lines/hr) |
| **B — Secondary** | Minor NPCs, reactive conversation, flavour scenes | 30–50 | **1,200–1,800 w/hr** (~30–50 lines/hr) |
| **C — Barks/ambient** | One-liners, radio chatter, reaction, idle | 6–15 | **2,500–4,000 w/hr** (~250–400 lines/hr) |

| Budget | Vetting hrs | Total words | Total lines |
|---|---|---|---|
| Conservative (1 hr/day × 9 mo) | ~270 | ~115,000 | ~6,500 |
| **Moderate (1 hr/day × 18 mo)** | **~540** | **~230,000** | **~13,000** |
| Aggressive (2 hr/day × 18 mo) | ~1,080 | ~455,000 | ~27,000 |

> **Practical ceiling: 150,000–300,000 vetted words / 10,000–18,000 lines.** That is ~10–25% of Disco Elysium and comfortably more than Left 4 Dead 2's entire corpus. It is plenty — *if* you optimise the right thing.

Three reframings that matter more than the raw number:

1. **Optimise reachable-words-per-playthrough, not total words.** Disco Elysium players see maybe 15–25% of the corpus per run. A 150k corpus where the player reaches 60% reads *bigger* than a 400k corpus where they reach 15%.
2. **Have the frontier model emit the conditions with the prose**, in your exact schema, so vetting is one pass over prose+tags rather than two.
3. **Batch-sample Tier C.** Generate barks in batches sharing one prompt/persona; read 20 of 200; if the batch's failure rate exceeds ~5%, reject the whole batch and re-prompt. Statistical QA. **Never do this for Tier A.**

### 7.2 Selection architecture — steal Valve's, not Bethesda's

**Elan Ruskin, "AI-driven Dynamic Dialog through Fuzzy Pattern Matching", GDC 2012** (shipped in HL2, TF2, L4D 1&2, Portal 2, Dota 2) is the single best fit for this design, and the slides are public.

The model: the world is *"a flat pile of facts"*; a **query** is an associative array of facts; a **rule** is a set of **criteria** over facts; **the matching rule with the most criteria wins**; the rule names a **response** (a group of interchangeable lines).

```
concept=SeeEnemy                                        → "Contact!"                   (1 criterion)
concept=SeeEnemy, who=Marek                             → "I see 'em."                 (2)
concept=SeeEnemy, who=Marek, region=junkyard            → "Not in my junkyard."        (3)
concept=SeeEnemy, who=Marek, region=junkyard, hp<0.25   → "...I can't do this again."  (4)
```

**Why most-specific-wins is the right choice here and first-match-wins is not:**

| | Valve rule DB | Creation Engine topic infos (New Vegas) |
|---|---|---|
| Winner | **Most criteria** | **First in author order** (persisted in the `PNAM` "previous info" field) |
| Specificity | Automatic, emergent | **Manual** — you hand-sort specific-before-general |
| Adding a line later | Append it anywhere | Find the right slot |
| Failure mode | Occasional tie to break | **A too-general info sorted too high silently shadows everything below it** |

That shadowing bug is the classic Bethesda-modding footgun and it gets monotonically worse as the corpus grows. For a corpus you intend to grow to 10k+ machine-generated lines, most-criteria-wins has no ordering to maintain, and it gives you free fallback layering: write the generic line once, bolt specific overrides on forever, never leave a silent hole.

**Implementation, which is genuinely ~300–500 lines of C#:**
- Intern every string key and value to `int` via a symbol table. Everything becomes numeric.
- Normalise every predicate to a numeric interval `a ≤ x ≤ b`. Equality is `a == b`; string equality works because strings are interned. Criterion evaluation is two float compares — branchless.
- A rule matches only if **all** criteria pass. *"If a criterion is missing from the query, reject."* Score = criteria count.
- **Sort** each rule's criteria and the query's facts, then walk them in parallel (skip merge) — avoids materialising a concatenated fact array.
- **Hash-partition** on a few keys present in every rule. Ruskin: *"'concept' and 'who' are good ones."* Add `region` for a post-apocalyptic map — Valve shipped exactly this as "regional databases".
- Sort each bucket **descending by criteria count** and early-out on first match: no remaining rule can beat it.

Ruskin's measured claim, verbatim: *"if you can get down to about fifty rules per bucket, and each rule has an average of eight criteria, you can do a lookup in less than a microsecond."*

> **Sub-microsecond selection over the whole corpus. Retrieval performance is not a design constraint.** Do not build a dialogue tree, do not add a vector database, do not make it async. It will not appear in a profile.

**Repetition control, built in:**
- **Response groups** — one rule owns several interchangeable lines, chosen at random. Variety lives *inside* a rule, so the table stays small.
- **Write-back facts with expiry** — a rule applies facts when it fires: `ApplyFacts "SaidC3M2SafeRoom:1:0,Talk:1:3.088"` (`name:value:expirySeconds`, `0` = never). Later rules add `SaidX == 0` as a criterion to suppress the replay.
  - **Valve's own admitted weakness:** *"Writers had to make a special-case variable for each one-off line."* **Do not replicate this.** Auto-generate the said-flag and the suppressing criterion from a `once: true` / `cooldown: 30` field at bake time. ~20 lines of authoring-time transform, removes the system's single biggest documented papercut.

**Multi-turn without a graph — `then` / followups.** When a line finishes it dispatches a followup concept to another character (or broadcasts to everyone in earshot; most specific reply wins). The rule that matters, verbatim:

> *"Each followup line is a new query! Query the followup line when the callback happens, not when the first character starts to speak. The situation may have changed during the time it took the first line to be said."*

**Re-query, don't resume.** Campo Santo converged on the same principle independently in *Firewatch* — their system began as an "interrupt-heavy bark system" and grew into "long, **restarting** conversations" ([GDC 2017](https://www.gamedeveloper.com/design/video-the-dialog-systems-and-tools-of-i-firewatch-i-)). Two teams, same answer. Adopt it — and note that it is *also* exactly the seam the coda lives in: **the coda is a followup that may or may not arrive.**

**Authoring format:** Valve shipped Dota 2's rules as an **Excel spreadsheet** exported by a macro, and describe the raw format as *"human readable but also easy to auto-generate."* That last phrase is the operative one — your frontier model writes the table directly.

### 7.3 Two ideas worth stealing outright

**Disco Elysium: skills as speakers.** DE's 24 skills are simply additional speaker identities in the same graph — Inland Empire and Electrochemistry are NPCs that live in your head. Mechanically this is nothing more than `who = "Inland Empire"` as a fact plus a level gate. Two consequences: it costs **zero new machinery** on a speaker-keyed rule table, and it is a **massive corpus multiplier**, because one authored situation gets N plausible commentators each with a distinct, easily-prompted voice. *"Generate this beat as seen by X"* is the single highest-leverage offline generation pattern in this research.

For ROTORWASH the mapping is obvious and better than DE's, because it is diegetic rather than psychological: **the aircraft and the avionics are the internal voices.** Hugh (D-012) has opinions. The radar warning receiver has opinions (D-010). The fuel state has opinions (D-007). The airframe's current refit configuration has opinions (D-011). Every one of those is already a real system with real numbers, so the "skill" never has to be faked — and each one you bolt on adds a commentator, which makes D-005's "the machine levels up" land in the writing as well as the mechanics.

**Citizen Sleeper: clock discipline.** Damian Martin's thesis is that abstraction *"is very powerful in videogames because it means you can have a massive variety of content with just a few mechanics"* — one interaction frame *"can describe anything from fighting a gang member to babysitting a child."* The corpus-economics consequence: because the **clock is the state variable** and scene text is keyed to clock position, 5 segments × 3 outcome bands = 15 authored beats that read as a storyline.

**This is the discipline that decides whether your corpus is vettable.** If your fact dictionary is dominated by a few dozen named clocks and meters rather than hundreds of ad-hoc booleans, your rule conditions stay short, the combinatorics stay legible, and you can actually hand-check the cross-product. If it grows to hundreds of one-off flags, you cannot, and the corpus becomes unvettable long before it becomes large.

### 7.4 Avoiding the canned feel

**The governing equation: perceived repetition ≈ trigger frequency ÷ pool size.** The "arrow to the knee" failure was not the line — it was that *only guards had it*, so one line owned an entire NPC class across a 100-hour game. **Trigger frequency is a level-design and AI-density variable, not a writing variable. You cannot out-write a bad trigger.** A 40-variant pool firing every 15 seconds reads as more repetitive than an 8-variant pool firing every 10 minutes. Budget your corpus against trigger rate, not NPC count.

The selection stack, cheapest-first, each compounding:

1. **Shuffle bag, not `Random.Next(n)`.** With 8 variants, naive random produces an immediate repeat ~12.5% of the time, and players notice doubles far more than they notice a missing variant. Deal without replacement, reshuffle when empty, **and on reshuffle, if the new first element equals the last one dealt, swap it with a random later element** — that one extra check kills seam-repeats, which are the ones people actually hear. (Same principle as Tetris's 7-bag.)
2. **Fractional recency penalty on *rules*, not just lines.** Shuffle bags give within-response variety; you also need across-rule variety or the NPC keeps making the same *kind* of remark in different words. Keep a per-speaker ring buffer of recently fired rule IDs: `effectiveScore = criteriaCount − 0.4 × recencyPenalty(ruleId)`. Fractional so it never inverts genuine specificity — a 4-criterion rule still beats a 3-criterion one, but two equally specific rules alternate.
3. **Mood and relationship as facts, not as parallel line sets.** Add `rel_tier` (hostile/wary/neutral/warm/bonded) and `mood` to the dictionary and let most-specific-wins do the work. **Few tiers (4–5) and sticky boundaries (hysteresis)** — players read a tier flip on a single interaction as the character being bipolar.
4. **Callbacks.** Tag memorable events with booleans and write a small number of lines that reference them. This is the highest perceived-intelligence-per-word technique available: one NPC line in hour 20 referencing something you did in hour 3 buys more "this game knows me" than 500 bark variants. Spend Tier-A vetting effort here preferentially.
5. **Silence is a valid response with a score.** If the best rule is exhausted or on cooldown, say nothing rather than falling back to a generic line. Players forgive a quiet NPC and punish a repetitive one — and a too-general fallback firing constantly *is* the arrow-to-the-knee failure, structurally.
6. **Combinatorics at the exchange level, never inside the sentence.** Ruskin's `then` followups give you N×M perceived content from N+M authored lines — use that heavily. Tracery-style fragment assembly *within* a sentence reads as templated almost immediately, because slot-filled sentences share rhythm and syntax even when the words differ. Even Wildermyth, the poster child for procedural narrative, hand-writes its per-personality variant lines. **Your offline frontier-model pipeline *is* your combinatorial generator — do the combinatorics at dev time, where a human can veto them.**

**What players actually complain about** — note that four of five are selection bugs, not corpus-size problems:

| Complaint | Real cause | Fix |
|---|---|---|
| "That line again" | One line owns an entire NPC class | Partition on `who` + faction + region, not role |
| "NPCs are goldfish" | No said-flags; they re-greet you forever | Expiring said-facts + callbacks |
| "It doesn't fit here" | Line fires in a state it wasn't written for | **More criteria, not more lines** |
| "Two NPCs said the same thing" | Shared pool, no speaker-local recency | Per-speaker recency + speaker-keyed partitioning |
| "It's obviously Mad Libs" | Runtime fragment assembly | Assemble offline, vet, ship flat |

> **Your retrieval logic buys more perceived variety per hour of your time than your corpus does.** Build the selection stack first, then generate against it.

### 7.5 Schema and runtime

```jsonc
{
  "id": "marek_greet_lowfuel_junkyard",
  "concept": "Greeting",          // partition key 1
  "who": "marek",                 // partition key 2
  "region": "junkyard",           // partition key 3
  "criteria": [
    { "k": "fuel_frac",  "op": "<",  "v": 0.15 },
    { "k": "rel_tier",   "op": ">=", "v": 3 },
    { "k": "said_marek_fuel_jab", "op": "==", "v": 0 }
  ],
  "lines": [                       // response group → shuffle bag
    "You coasted in on fumes again.",
    "I heard that thing cough on short final.",
    "One day you'll glide in here and I'll charge you for the landing."
  ],
  "apply": { "said_marek_fuel_jab": [1, 900] },   // value, expiry seconds (0 = never)
  "then":  { "concept": "OfferTrade" },
  "coda":  { "allowed": true, "angles": ["AIRCRAFT_CONDITION", "WEATHER", "ABSTAIN"] },
  "est_delivery_ms": 4200,         // ← the latency-cover gate from §2.5
  "tier": "B",
  "vetted": "2026-09-14"
}
```

Note the two fields that wire §7 to §6: **`est_delivery_ms`** (computed at bake time from character count and typewriter rate; the coda is requested only if it exceeds p95 latency × 1.5) and **`coda.angles`** (the designer-authored shuffle bag that prevents the model from converging on one cadence). The coda is authored *per rule*, which means you can simply switch it off for every line where you do not want it — a per-line kill switch that costs nothing.

**Load-time build:** intern strings → normalise criteria to intervals → sort each rule's criteria by key → hash-partition on `(concept, who, region)` → sort buckets descending by criteria count. **Runtime:** build the fact array into a reused buffer (zero alloc), hash to bucket, skip-merge walk, first full match wins after the recency adjustment, deal from the shuffle bag, apply write-back facts, schedule the `then`.

### 7.6 Off-the-shelf: use ink for set-pieces, write your own selector

| | **ink** (inkle) | **Yarn Spinner** |
|---|---|---|
| Core | C#, MIT | C#, MIT; **v3.1 shipped Dec 2025** |
| Godot C# binding | **GodotInk** (paulloz) — MIT, Godot 4, ~784★, stable | **YarnSpinner-Godot** — *"work-in-progress… We don't currently offer any official support for it"* |
| Godot GDScript | inkgd (pure GDScript, 4.2+, slower) | YarnSpinner-Godot-GDScript |
| 2026 trajectory | Stable, community-maintained | Actively invested — Unreal port, VS Code ReactFlow graph view, Story Solver debugger |

**Both are *flow* languages.** They are excellent at "the player is in a conversation and we walk a graph." Your problem is **retrieval**: "given world state, what should this NPC say right now?" Forcing retrieval into a flow language means a giant `if/elif` ladder at the top of a knot — which is precisely the Creation-Engine first-match-wins failure mode, silent shadowing included.

> **Recommended split: your own Ruskin-style rule table for selection (~300–500 lines of C#), plus ink via GodotInk for the 10–20 hand-crafted set-piece conversations** where you genuinely want tunnels, threads and once-only choices. Wire ink to the fact dictionary with `EXTERNAL` functions. Revisit Yarn Spinner when its Godot C# binding loses the "no official support" label.

### 7.7 Free-text input and embeddings — probably don't, but here's the cheap way

The rule table is exact and sub-microsecond; embeddings add nothing to *state-conditioned* selection. They only matter if the player types free text. Even then, dense embeddings are weak exactly where an RPG is demanding: **negation** ("I *don't* want the fuel") and **entity specificity** (Marek vs Mara) — they match topical gist, which is the BM25 case, not the embedding case.

If you do need it, the important finding is that **static embedding models need no inference runtime at all**:

| Model | Params | Dims | Size | Quality (NanoBEIR NDCG@10) | Speed |
|---|---|---|---|---|---|
| all-MiniLM-L6-v2 | 22.7M | 384 | ~80 MB ONNX | **0.5623** | needs ONNX Runtime; 256-token cap |
| **static-retrieval-mrl-en-v1** | `EmbeddingBag(30522,1024)` — **no active params** | 1024 → MRL 256 | **~31 MB @256d** | 0.5032 (~90% of MiniLM) | **107,419 sentences/sec on CPU**; unlimited length |
| potion-base-8M (Model2Vec) | 7.56M | 256 | ~30 MB | MTEB avg 51.32 | MIT, very fast |
| EmbeddingGemma-300m | 300M | 768 → MRL | ~1.2 GB fp32 | MTEB Eng 69.67 | no fp16; Gemma terms |

`static-retrieval-mrl-en-v1` is architecturally just `EmbeddingBag(30522, 1024, mode='mean')` — no transformer. Tokenize (WordPiece) → look up a row per token → average → normalise. **That is ~50 lines of C# over a `float[]`**, with the tokenizer available first-party as `Microsoft.ML.Tokenizers.BertTokenizer` (managed, no native dep) and the similarity search as `System.Numerics.Tensors.TensorPrimitives.CosineSimilarity` (hardware-accelerated; brute force over 20,000 × 256-dim vectors is ~5M FLOPs, well under a millisecond). **No ONNX Runtime, no per-platform native DLL, no Godot export-template packaging headache** — worth far more to a solo dev than MiniLM's ~12% relative quality edge. Pre-normalise the corpus vectors at bake time and it is a plain dot product. **Do not add a vector database.**

**Pipeline, cheapest-first:** (1) BM25 + a closed entity gazetteer over the line index — your corpus is small and your entity vocabulary is known, this handles most inputs at ~0 MB and is fully debuggable; (2) add static embeddings as a *reranker* only if step 1 visibly fails; (3) **gate everything through the rule table anyway** — free text selects a *concept*, and the rule table then picks the line given world state, so free-text matching can never produce a state-inappropriate line.

---

## Appendix A — an implementation order that keeps the feature killable

| Phase | Work | Kill point |
|---|---|---|
| **0** | `ICodaProvider { Task<string?> GetCodaAsync(CodaRequest, CancellationToken) }` + `NullCodaProvider` returning `null`. Wire the dialogue box's state machine (§6.9) to handle `null` as the normal case. | — |
| **1** | Ruskin rule table (§7.2), fact dictionary, shuffle bags, recency, said-flags. **This is the game.** | Ship here forever if you like. |
| **2** | Offline bake pipeline in `tools/` — frontier model emits rows in the §7.5 schema with conditions and `est_delivery_ms`; tiered vetting (§7.1). | Ship here. |
| **3** | `llama-bench` on the actual 1080 and on Fred's 1650 Ti. **Replace §2.2's derived table with measurements.** If the numbers are more than ~2× worse than derived, stop. | **Cheap, decisive kill point — do this first of the layer-(b) work.** |
| **4** | Offline coda harness (§6.6): 20–50k generations, validator, rejection/abstain/diversity stats, 500-sample human audit. **No game integration yet.** | If the audit fails the <1% gate, stop. Total sunk cost: a few days. |
| **5** | Subprocess runtime + VRAM probe + frame-time safety net (§3, §4.4). Opt-in, off by default. | Ship with it defaulted off. |
| **6** | Per-NPC memory ledger (§6.10) — the part players actually notice. | — |

The ordering matters: **phases 3 and 4 are both cheap and both decisive, and neither requires touching the game.** Do them before any integration work. If either fails, you have lost a week and learned something, and layers (a) and (b) were never coupled.

Note that Fred's current dev machine is a **GTX 1650 Ti (4 GB)** per `STATUS.md` — *below* the D-003 floor. That is a gift for this feature specifically: it is the harshest realistic VRAM case, so if the probe and the safety net behave correctly there, the 1080 is comfortable. Test on it deliberately.

---

## Appendix B — decision summary

| Question | Answer |
|---|---|
| Verdict | **Build it differently** — layer (a) is the product; layer (b) is a killable two-week spike behind `ICodaProvider` |
| Model | **Qwen3-1.7B Q4_K_M** (1.11 GB, Apache-2.0). Fallback **Qwen3-4B-Instruct-2507 Q4_K_M** (2.50 GB) on ≥12 GB cards |
| Quant | Q4_K_M floor, Q5_K_M if VRAM allows. Never below Q4 |
| Backend | **Vulkan** (faster tg than CUDA on Pascal, one binary covers AMD/Intel) |
| Runtime | **Bundled `llama-server.exe` subprocess over localhost HTTP** (process isolation = free graceful degradation). Fallback **LLamaSharp 0.29.0 + `LLamaSharp.Backend.Vulkan`**, both MIT. Ollama and ONNX Runtime GenAI are out |
| Context | `-c 2048`, KV cache `q8_0`, `-b 256`, `-np 1` |
| VRAM | ~1.5 GB for 1.7B. Probe with NVML/DXGI *after* a heavy scene, +1.25 GB slack, frame-time safety net |
| Default placement | **CPU** on the floor spec; GPU as a probed opt-in |
| Latency (1080/Vulkan/1.7B/warm) | **~0.5 s**; **~2.8 s** CPU DDR4; hard abandon at 4 s |
| Coda size | 40 tokens (`n_predict` 48), 1–2 sentences |
| Cover rule | Request a coda only if estimated line duration ≥ p95 latency × 1.5 |
| Biggest risk | **VRAM overcommit causing texture eviction and frame hitches the player blames on the engine** — invisible in testing on a dev machine, fatal on the floor spec |

---

## Appendix C — sources

**Models and licences**
- [Qwen3-4B-Instruct-2507](https://huggingface.co/Qwen/Qwen3-4B-Instruct-2507) · [Qwen3-1.7B](https://huggingface.co/Qwen/Qwen3-1.7B) · [unsloth/Qwen3-1.7B-GGUF file listing](https://huggingface.co/unsloth/Qwen3-1.7B-GGUF/tree/main) · [unsloth/Qwen3-4B-Instruct-2507-GGUF](https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF)
- [Qwen3.5-2B](https://huggingface.co/Qwen/Qwen3.5-2B) · [Qwen3.5-4B](https://huggingface.co/Qwen/Qwen3.5-4B) · [ollama qwen3.5 tag sizes](https://ollama.com/library/qwen3.5)
- [Gemma 4: Expanding the Gemmaverse with Apache 2.0](https://opensource.googleblog.com/2026/03/gemma-4-expanding-the-gemmaverse-with-apache-20.html) · [google/gemma-4-E2B-it](https://huggingface.co/google/gemma-4-E2B-it) · [Gemma Terms of Use (pre-4)](https://ai.google.dev/gemma/terms)
- [SmolLM3-3B](https://huggingface.co/HuggingFaceTB/SmolLM3-3B) · [Llama-3.2-3B-Instruct](https://huggingface.co/meta-llama/Llama-3.2-3B-Instruct) · [Phi-4-mini-instruct](https://huggingface.co/microsoft/Phi-4-mini-instruct)
- ['Open' AI model licenses often carry concerning restrictions (TechCrunch)](https://techcrunch.com/2025/03/14/open-ai-model-licenses-often-carry-concerning-restrictions/)

**Performance**
- [llama.cpp Vulkan performance scoreboard (disc. #10879)](https://github.com/ggml-org/llama.cpp/discussions/10879)
- [llama.cpp CUDA performance scoreboard (disc. #15013)](https://github.com/ggml-org/llama.cpp/discussions/15013)
- [Vulkan vs CUDA (disc. #23109)](https://github.com/ggml-org/llama.cpp/discussions/23109)
- [GTX 1080 Ti for Local LLM — ariya.io, Feb 2026](https://ariya.io/2026/02/gtx-1080-ti-for-local-llm/)
- [Comparing llama.cpp GPU performance: CUDA, ROCm, Vulkan](https://knightli.com/en/2026/04/23/llama-cpp-gpu-benchmark-cuda-rocm-vulkan-scoreboard/)
- [llama-server README — grammar, json_schema, slots, --cache-prompt, -ngl, -t](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md)

**Prior art — shipped products**
- [NVIDIA: ACE autonomous AI companions in PUBG and NARAKA](https://www.nvidia.com/en-us/geforce/news/nvidia-ace-autonomous-ai-companions-pubg-naraka-bladepoint/) · [ACE, NARAKA and inZOI launch](https://www.nvidia.com/en-us/geforce/news/nvidia-ace-naraka-bladepoint-inzoi-launch-this-month/)
- [**How KRAFTON built PUBG Ally** (NVIDIA dev blog, 25 Jun 2026)](https://developer.nvidia.com/blog/how-krafton-built-pubg-ally-a-co-playable-character-powered-by-nvidia-ace/) — the System 1 / System 2 postmortem
- [ACE Game Agent SDK + UE5 plugins](https://developer.nvidia.com/blog/build-on-device-ai-companions-with-the-nvidia-ace-game-agent-sdk-and-unreal-engine-5-plugins/) · [ACE adds Qwen3 SLM for on-device](https://developer.nvidia.com/blog/nvidia-ace-adds-open-source-qwen3-slm-for-on-device-deployment-in-pc-games/) · [Minimizing game runtime inference costs](https://developer.nvidia.com/blog/how-to-minimize-game-runtime-inference-costs-with-coding-agents/)
- [inZOI on Steam](https://store.steampowered.com/app/2456740/inZOI/) · [Smart Zoi discussion threads](https://steamcommunity.com/app/2456740/discussions/0/836123794066162014/) · [inZOI Smart Zoi explained](https://gamerant.com/inzoi-smart-zoi-function-explained/)
- [Whispers from the Star](https://store.steampowered.com/app/3730100/Whispers_from_the_Star/) · [AWS: how Anuttacon scaled it](https://aws.amazon.com/blogs/storage/how-anuttacon-scaled-ai-enhanced-gaming-workloads-for-whispers-from-the-star/) · [Vaudeville](https://store.steampowered.com/app/2240920/Vaudeville/) · [Suck Up!](https://store.steampowered.com/app/2726370/Suck_Up/) · [Retail Mage](https://store.steampowered.com/app/3224380/Retail_Mage/)
- [AI People entered the closed phase (GoodAI)](https://www.goodai.com/ai-people-entered-the-closed-phase/)
- [Mecha BREAK AI dialogue / NVIDIA ACE (Screen Rant)](https://screenrant.com/mecha-break-ai-dialouge-npcs-nvidia-ace/)

**Prior art — modded Skyrim**
- [Mantella docs](https://art-from-the-machine.github.io/Mantella/) · [Mantella issues & Q&A — the small-model failure modes](https://art-from-the-machine.github.io/Mantella/pages/issues_qna.html) · [Mantella GitHub](https://github.com/art-from-the-machine/Mantella) · [issue #49, parallel voice-model swap](https://github.com/art-from-the-machine/Mantella/issues/49)
- [HerikaServer / CHIM](https://github.com/abeiro/HerikaServer)

**Prior art — criticism, policy and sentiment**
- [Kotaku: NVIDIA/Convai AI NPC demo](https://kotaku.com/nvidia-convai-ai-demo-npc-dialogue-bad-1851345311) · [Aftermath: AI NPCs and the second uncanny valley](https://aftermath.site/ai-npcs-nvidia-unity-ubisoft-convai-inworld/)
- [PC Gamer: AI stigma on Steam can reduce reviews ~53%](https://www.pcgamer.com/software/ai/data-analyst-finds-ai-stigma-on-steam-can-reduce-the-number-of-reviews-a-game-gets-by-around-53-percent-and-the-reviews-it-does-get-are-more-negative/) · [Totally Human Media: genAI games on Steam](https://www.totallyhuman.io/blog/the-surprising-new-number-of-genai-games-on-steam) · [Valve rewrites Steam's AI disclosure rules (Jan 2026)](https://www.generationamiga.com/2026/01/17/valve-rewrites-steams-ai-disclosure-rules-for-developers/)
- [Game Developer: devs more worried than ever that genAI will lower quality](https://www.gamedeveloper.com/business/devs-are-more-worried-than-ever-that-generative-ai-will-lower-the-quality-of-games)
- [PC Gamer: Fortnite AI Darth Vader tricked into slurs](https://www.pcgamer.com/games/battle-royale/fortnite-added-an-ai-powered-darth-vader-and-surprise-players-immediately-tricked-him-into-saying-slurs/)
- [arXiv 2511.10277 — Fixed-Persona SLMs with Modular Memory](https://arxiv.org/abs/2511.10277) · [arXiv 2508.19288 — Tricking LLM-based NPCs into spilling secrets](https://arxiv.org/pdf/2508.19288)

**Dialogue architecture**
- [**Ruskin, GDC 2012 — AI-driven Dynamic Dialog through Fuzzy Pattern Matching (full slide text)**](https://archive.org/stream/valve-publications/2012/GDC2012_Ruskin_Elan_DynamicDialog_djvu.txt) · [GDC Vault](https://gdcvault.com/play/1015317/AI-driven-Dynamic-Dialog-through) · [Game Developer writeup](https://www.gamedeveloper.com/design/video-valve-s-system-for-creating-ai-driven-dynamic-dialog) · [Emily Short's notes](https://emshort.blog/2012/03/16/gdc-2012-talk-on-dynamic-dialogue/)
- [Firewatch dialogue systems and tools (GDC 2017)](https://www.gamedeveloper.com/design/video-the-dialog-systems-and-tools-of-i-firewatch-i-) · [talk video](https://www.youtube.com/watch?v=wj-2vbiyHnI)
- [Disco Elysium Scribe — datamined conversation/condition counts](https://disco-elysium-scribe.pages.dev/) · [articy: Disco Elysium showcase](https://www.articy.com/en/showcase/disco-elysium/) · [HN: Disco Elysium Explorer](https://news.ycombinator.com/item?id=42679679)
- [Game Developer: how Citizen Sleeper was inspired by tabletop RPGs and gig work](https://www.gamedeveloper.com/business/how-citizen-sleeper-was-inspired-by-tabletop-rpgs-and-gig-work) · [Rascal News: Gareth Damian Martin interview](https://www.rascal.news/flatmates-in-space-gareth-damian-martin-talks-genre-and-citizen-sleepers-tabletop-destiny/)
- [GECK Wiki: dialogue](https://geckwiki.com/index.php/Category:Dialogue) *(403s automated fetch)*
- [The Narrative Dept.: writing barks](https://www.thenarrativedept.com/blog/barks) · [Game Developer: dramatic dialogue is avoiding repetition](https://www.gamedeveloper.com/design/dramatic-dialogue-is-avoiding-repetition) · [Wikipedia: arrow in the knee](https://en.wikipedia.org/wiki/Arrow_in_the_knee)

**Corpus scale and vetting throughput**
- [Guinness: longest script for a videogame (BG3)](https://www.guinnessworldrecords.com/world-records/764696-longest-script-for-a-videogame) · [Fallout: New Vegas](https://en.wikipedia.org/wiki/Fallout:_New_Vegas) · [Fallout 4](https://en.wikipedia.org/wiki/Fallout_4) · [Planescape: Torment](https://en.wikipedia.org/wiki/Planescape:_Torment)
- [PC Gamer: Disco Elysium's narrator recorded 350,000 words](https://www.pcgamer.com/we-talk-to-disco-elysiums-incredible-narrator-who-recorded-350000-words-of-dialogue-and-has-never-acted-before/)
- [Editorial Freelancers Association — 2025 rate survey](https://www.the-efa.org/rates/) · [Wikipedia: words per minute (reading and proofreading rates)](https://en.wikipedia.org/wiki/Words_per_minute)

**Runtimes**
- [llama.cpp releases](https://github.com/ggml-org/llama.cpp/releases/latest) · [LLamaSharp](https://github.com/SciSharp/LLamaSharp) · [NobodyWho](https://github.com/nobodywho-ooo/nobodywho)

**Tooling**
- [ink](https://github.com/inkle/ink) · [GodotInk](https://github.com/paulloz/godot-ink) · [inkgd](https://github.com/ephread/inkgd) · [YarnSpinner-Godot](https://github.com/YarnSpinnerTool/YarnSpinner-Godot) · [Yarn Spinner in 2026](https://yarnspinner.dev/blog/yarn-spinner-in-2026)
- [NobodyWho — llama.cpp for Godot](https://github.com/nobodywho-ooo/nobodywho)
- [sentence-transformers/static-retrieval-mrl-en-v1](https://huggingface.co/sentence-transformers/static-retrieval-mrl-en-v1) · [HF blog: static embeddings](https://huggingface.co/blog/static-embeddings) · [all-MiniLM-L6-v2](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2) · [potion-base-8M](https://huggingface.co/minishlab/potion-base-8M)
- [Microsoft.ML.Tokenizers.BertTokenizer](https://learn.microsoft.com/en-us/dotnet/api/microsoft.ml.tokenizers.berttokenizer) · [TensorPrimitives.CosineSimilarity](https://learn.microsoft.com/en-us/dotnet/api/system.numerics.tensors.tensorprimitives.cosinesimilarity)
