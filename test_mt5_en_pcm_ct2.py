import time
import ctranslate2
from transformers import AutoTokenizer

MODEL_DIR = "models/mt5_small_en_pcm_int8"
HF_MODEL = "Davlan/mt5-small-en-pcm"

samples = [
    "The government announced new measures to reduce food prices.",
    "Heavy rain is expected in Lagos this weekend.",
]

tokenizer = AutoTokenizer.from_pretrained(HF_MODEL, use_fast=False)
translator = ctranslate2.Translator(MODEL_DIR, device="cpu")

for sample in samples:
    src_ids = tokenizer.encode(sample, add_special_tokens=True)
    src_tokens = tokenizer.convert_ids_to_tokens(src_ids)

    t0 = time.perf_counter()
    result = translator.translate_batch(
        [src_tokens],
        beam_size=4,
        max_decoding_length=256,
    )[0].hypotheses[0]
    latency_ms = (time.perf_counter() - t0) * 1000

    out_ids = tokenizer.convert_tokens_to_ids(result)
    translated = tokenizer.decode(out_ids, skip_special_tokens=True)

    print(f"EN: {sample}")
    print(f"PCM: {translated}")
    print(f"Latency: {latency_ms:.1f} ms")
    print("-")
