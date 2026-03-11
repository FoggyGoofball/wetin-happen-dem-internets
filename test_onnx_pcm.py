import time
import numpy as np
import onnxruntime as ort
from transformers import AutoTokenizer

MODEL_DIR = "models/onnx_pcm_10mb_int8"
MODEL_PATH = f"{MODEL_DIR}/model.onnx"
TOKENIZER_ID = "goldfish-models/pcm_latn_10mb"

tokenizer = AutoTokenizer.from_pretrained(TOKENIZER_ID)
session = ort.InferenceSession(MODEL_PATH, providers=["CPUExecutionProvider"])
inputs_meta = {i.name: i for i in session.get_inputs()}

num_layers = 4
num_heads = 8
head_dim = 64

def empty_past():
    return {
        f"past_key_values.{i}.key": np.zeros((1, num_heads, 0, head_dim), dtype=np.float32)
        for i in range(num_layers)
    } | {
        f"past_key_values.{i}.value": np.zeros((1, num_heads, 0, head_dim), dtype=np.float32)
        for i in range(num_layers)
    }

def generate(prompt: str, max_new_tokens: int = 48):
    prompt_ids = tokenizer.encode(prompt, add_special_tokens=False)
    generated = list(prompt_ids)
    eos_id = tokenizer.eos_token_id

    past = empty_past()
    start = time.perf_counter()

    for step in range(max_new_tokens):
        if step == 0:
            input_ids = np.array([prompt_ids], dtype=np.int64)
            pos = np.arange(len(prompt_ids), dtype=np.int64).reshape(1, -1)
        else:
            input_ids = np.array([[generated[-1]]], dtype=np.int64)
            pos = np.array([[len(generated) - 1]], dtype=np.int64)

        attention_mask = np.ones((1, len(generated)), dtype=np.int64)

        feeds = {
            "input_ids": input_ids,
            "attention_mask": attention_mask,
            "position_ids": pos,
            **past,
        }

        outputs = session.run(None, feeds)
        logits = outputs[0]

        next_id = int(np.argmax(logits[0, -1, :]))
        generated.append(next_id)

        present_offset = 1
        for i in range(num_layers):
            past[f"past_key_values.{i}.key"] = outputs[present_offset + (2 * i)]
            past[f"past_key_values.{i}.value"] = outputs[present_offset + (2 * i) + 1]

        if eos_id is not None and next_id == eos_id:
            break

    text = tokenizer.decode(generated, skip_special_tokens=True)
    elapsed = (time.perf_counter() - start) * 1000
    return text, elapsed

prompt = "Translate this English sentence to Nigerian Pidgin: The government announced new measures to reduce food prices.\nPidgin:"
out, ms = generate(prompt)
print("\nOutput:\n", out)
print(f"Latency: {ms:.1f} ms")
