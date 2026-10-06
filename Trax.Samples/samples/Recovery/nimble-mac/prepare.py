"""Downloads Bespoke-Nimble-9B and merges its LoRA adapter into the pinned Qwen3.5-9B base, once.

This is the "Download the model" step of Nimble's README as a script. Run it from a Nimble checkout, in the
environment that has requirements/training.txt:

  .cache/venvs/nimble/bin/python <Trax.Samples>/samples/Recovery/nimble-mac/prepare.py

It writes the merged weights to .cache/models/ and their location to .cache/nimble-model.json, which serve.py reads.
"""

import hashlib
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path.cwd()))

from huggingface_hub import snapshot_download  # noqa: E402

REPO = "bespokelabs/Bespoke-Nimble-9B"

snapshot = Path(snapshot_download(REPO, cache_dir=".cache/huggingface/hub"))
contract_file = snapshot / "schema_config.json"
contract = json.loads(contract_file.read_text()) if contract_file.exists() else {}
if contract:
    from transformers import AutoTokenizer
    from nimble.training.candidate_schema import validate_contract

    validate_contract(contract, AutoTokenizer.from_pretrained(snapshot))

model_path = snapshot
if (snapshot / "adapter_config.json").exists():
    import torch
    from peft import PeftModel
    from transformers import AutoTokenizer, Qwen3_5ForConditionalGeneration

    # The adapter names the base revision it was trained on.
    print(f"Downloading {contract['model']} at {contract['revision']} and merging...", flush=True)
    base = Qwen3_5ForConditionalGeneration.from_pretrained(
        contract["model"], revision=contract["revision"], dtype=torch.bfloat16, device_map="cpu",
        cache_dir=".cache/huggingface/hub",
    )
    merged = PeftModel.from_pretrained(base, snapshot).merge_and_unload(safe_merge=True)
    model_path = Path(".cache/models") / ("nimble-9b-" + snapshot.name)
    merged.save_pretrained(model_path)
    (model_path / "schema_config.json").write_text(json.dumps(contract, indent=2))
    AutoTokenizer.from_pretrained(snapshot).save_pretrained(model_path)
    # The scorer picks its probability temperature by the adapter's hash.
    (model_path / "READY.json").write_text(json.dumps({
        "model": REPO, "revision": snapshot.name,
        "adapter_sha256": hashlib.sha256((snapshot / "adapter_model.safetensors").read_bytes()).hexdigest(),
    }, indent=2))

Path(".cache/nimble-model.json").write_text(json.dumps({
    "model_path": str(model_path.resolve()),
    "model_id": REPO,
    "revision": snapshot.name,
    "max_input_tokens": contract.get("max_length", 2048),
}, indent=2))
print("Ready:", model_path, flush=True)
