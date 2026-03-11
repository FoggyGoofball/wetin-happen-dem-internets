import json
from huggingface_hub import HfApi, hf_hub_download

KEYWORDS = ["pidgin", "nigerian pidgin", "pcm", "english pidgin", "translation"]
MAX_RESULTS = 120

api = HfApi()
seen = set()
candidates = []

for kw in KEYWORDS:
    for model in api.list_models(search=kw, sort="downloads", limit=MAX_RESULTS):
        if model.id in seen:
            continue
        seen.add(model.id)
        candidates.append(model.id)


def repo_has_pcm_language_token(repo_id: str):
    token_hits = []

    # 1) shared_vocabulary.json (NLLB/CT2-style exports)
    try:
        p = hf_hub_download(repo_id=repo_id, filename="shared_vocabulary.json")
        with open(p, encoding="utf-8") as f:
            vocab = json.load(f)
        if isinstance(vocab, list):
            if any(isinstance(t, str) and ("pcm" in t.lower() or "pidgin" in t.lower()) for t in vocab):
                token_hits.append("shared_vocabulary.json")
    except Exception:
        pass

    # 2) tokenizer files (HF)
    for fname in ["tokenizer.json", "tokenizer_config.json", "special_tokens_map.json", "vocab.json"]:
        try:
            p = hf_hub_download(repo_id=repo_id, filename=fname)
            with open(p, encoding="utf-8", errors="ignore") as f:
                text = f.read().lower()
            if "pcm_latn" in text or "pidgin" in text or '"pcm"' in text:
                token_hits.append(fname)
        except Exception:
            pass

    # 3) config
    try:
        p = hf_hub_download(repo_id=repo_id, filename="config.json")
        with open(p, encoding="utf-8", errors="ignore") as f:
            text = f.read().lower()
        if "pcm_latn" in text or "pidgin" in text or '"pcm"' in text:
            token_hits.append("config.json")
    except Exception:
        pass

    return token_hits

matches = []
for repo in candidates:
    hits = repo_has_pcm_language_token(repo)
    if hits:
        matches.append((repo, sorted(set(hits))))

print("Potential model repos with PCM/Pidgin indicators:")
for repo, hits in matches:
    print(f"- {repo}  [{', '.join(hits)}]")

if not matches:
    print("No model found with explicit PCM/Pidgin indicators in inspected files.")
