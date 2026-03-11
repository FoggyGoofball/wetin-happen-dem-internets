from pathlib import Path
from onnxruntime.quantization import QuantType, quantize_dynamic

src_dir = Path("models/onnx_pcm_10mb")
out_dir = Path("models/onnx_pcm_10mb_int8")
out_dir.mkdir(parents=True, exist_ok=True)

for name in ["model.onnx", "model_with_past.onnx"]:
    src = src_dir / name
    if not src.exists():
        print(f"Skip missing {src}")
        continue

    dst = out_dir / name
    print(f"Quantizing {src} -> {dst}")
    quantize_dynamic(
        model_input=str(src),
        model_output=str(dst),
        weight_type=QuantType.QInt8,
        per_channel=False,
        reduce_range=False,
    )

for extra in ["config.json", "generation_config.json", "tokenizer.json", "tokenizer_config.json", "special_tokens_map.json"]:
    src = src_dir / extra
    if src.exists():
        (out_dir / extra).write_bytes(src.read_bytes())

print("Done")
