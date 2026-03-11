from huggingface_hub import HfApi

api = HfApi()
repos = [
    "goldfish-models/pcm_latn_10mb",
    "goldfish-models/pcm_latn_full",
    "goldfish-models/pcm_latn_5mb",
    "Davlan/mt5-small-en-pcm",
    "masakhane/mt5_en_pcm_news",
]

for repo in repos:
    print(f"\n{repo}")
    try:
        files = api.list_repo_files(repo)
        interesting = [f for f in files if any(x in f.lower() for x in ["onnx", "tflite", "gguf", "model", "bin", "spiece", "json", "tokenizer"])]
        for f in interesting[:40]:
            print(" -", f)
    except Exception as ex:
        print(" error:", ex)
