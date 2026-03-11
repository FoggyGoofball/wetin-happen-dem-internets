# Wetin Happen Dem Internets 📰

An Android news reader that fetches any article from the web and translates it into **Nigerian Pidgin English** on-device — no server, no subscription, no wahala.

---

## Features

- **Paste or share any news URL** — article is fetched, cleaned, and translated in seconds
- **Google News RSS feed** built-in with one-tap article loading
- **Rule-based Pidgin translator** with 300+ word mappings, progressive tense, possessives, and slang (`ogbonge`, `wahala`, `sharp sharp` …)
- **Side-by-side mode** — read English (bold) and Pidgin (italic) paragraph by paragraph
- **Hero image** extracted from `og:image` and displayed inline
- **Save & load** stories for offline reading (persisted across app restarts)
- **Share** translated story as plain text to any app
- **Export** to Google Docs (or any app) via Android share sheet — pick Google Docs and it lands straight into a new document
- **Robust article extraction** — WebView with 5-strategy JS harvester + HTTP fallback; adaptive polling for JS-heavy / SPA sites

---

## Architecture

```
MainActivity.cs          — UI, translation pipeline, story persistence
ArticleExtractor.cs      — Two-stage extractor: WebView (JS) → HTTP fallback
PidginTranslator.cs      — Rule-based EN → PCM translator (no model needed)
Resources/layout/        — Single-activity layout (ScrollView + cards)
Resources/values/        — String resources
Resources/drawable/      — Card + screen backgrounds
```

---

## Building

### Requirements
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) with Android workload
- Android SDK (API 24+)
- A physical device or emulator with USB debugging enabled

```bash
# Install Android workload if needed
dotnet workload install android

# Build and install to connected device
dotnet build "wetin happen dem internets.csproj" -t:Install -f net10.0-android -c Debug
```

---

## Python model scripts

The `*.py` scripts in the root are research tools used to explore and quantise
seq2seq Pidgin translation models (mT5, NLLB, CTranslate2). They are **not
required** to run the app — the app ships with a pure rule-based translator.
If you want to experiment with a neural smoothing pass, run the scripts in a
Python 3.12 venv:

```bash
python -m venv .venv312
.venv312\Scripts\activate
pip install -r requirements.txt   # create this from your own environment
python find_pcm_models.py
```

---

## Export to Google Docs

Tap **Export** on any translated story → Android share sheet opens → pick
**Google Docs** → a new document is created instantly. No API keys, no account
linking, no cost.

---

## Licence

MIT — do wetin you like with am.
