from huggingface_hub import HfApi

api = HfApi()
terms = ["pidgin translation", "pcm translation", "nigerian pidgin translation", "en pcm"]
seen = set()

for term in terms:
    for model in api.list_models(search=term, limit=200):
        if model.id in seen:
            continue
        seen.add(model.id)
        try:
            files = api.list_repo_files(model.id)
            onnx_files = [f for f in files if f.lower().endswith('.onnx') or '.onnx' in f.lower()]
            if onnx_files:
                print(model.id)
                for f in onnx_files[:5]:
                    print(' -', f)
        except Exception:
            pass
