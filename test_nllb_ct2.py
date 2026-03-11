import json
import time
import ctranslate2
import sentencepiece as spm

MODEL_DIR = "models/nllb200_distilled_600m_int8"
SRC_LANG = "eng_Latn"
TGT_LANG = "pcm_Latn"

sample = "The government announced new measures to reduce food prices."

with open(f"{MODEL_DIR}/shared_vocabulary.json", encoding="utf-8") as f:
    vocab = json.load(f)

if SRC_LANG not in vocab:
    raise ValueError(f"Source language token '{SRC_LANG}' is not supported by this model.")

if TGT_LANG not in vocab:
    west_africa_fallbacks = [tok for tok in ("hau_Latn", "ibo_Latn", "yor_Latn") if tok in vocab]
    raise ValueError(
        f"Target language token '{TGT_LANG}' is not supported by this model. "
        f"Available fallback examples: {west_africa_fallbacks or 'none found'}"
    )

translator = ctranslate2.Translator(MODEL_DIR, device="cpu")
sp = spm.SentencePieceProcessor(model_file=f"{MODEL_DIR}/sentencepiece.bpe.model")

src_tokens = sp.encode(sample, out_type=str) + ["</s>", SRC_LANG]

t0 = time.perf_counter()
result = translator.translate_batch(
    [src_tokens],
    target_prefix=[[TGT_LANG]],
    beam_size=4,
    max_decoding_length=256,
)[0].hypotheses[0]
latency_ms = (time.perf_counter() - t0) * 1000

decoded_tokens = [t for t in result if t not in {TGT_LANG, "</s>"} and not t.endswith("_Latn")]
translated = sp.decode(decoded_tokens)

print(f"EN: {sample}")
print(f"PCM: {translated}")
print(f"Latency: {latency_ms:.1f} ms")
