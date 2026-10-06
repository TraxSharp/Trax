"""POST /v1/systemone on a Mac: Nimble's MLX scorer behind the request shape its SGLang server accepts.

nimble/serving/server.py needs SGLang on CUDA. This serves the same merged checkpoint through
ParallelScorer on Metal instead, mapping questions to the scorer's schema the way
nimble/serving/compiler.py does and answering in the System One shape that AddNimbleDecider reads:

  {"model": ..., "answers": {"<id>": {"type": "noul", "noul": p}
                                   | {"type": "choice", "choice": key, "probabilities": {...}, "confidence": c}
                                   | {"type": "score", "score": expected, "probabilities": {"0": ...}, "confidence": c}}}

Confidence for choice and score is one minus normalized entropy, as on the hosted server.

Run it from a Nimble checkout whose model prepare.py has merged (see the Recovery README):

  .venv-mlx/bin/python <Trax.Samples>/samples/Recovery/nimble-mac/serve.py   # http://127.0.0.1:8000/v1/systemone
"""

import asyncio
import json
import math
import os
import sys
import time
from pathlib import Path

import uvicorn
from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse

# The Nimble checkout this runs from: its package and its prepared model.
ROOT = Path.cwd()
sys.path.insert(0, str(ROOT))
from nimble.scoring.parallel_scorer import ParallelScorer  # noqa: E402

MAX_QUESTIONS = 64
MAX_OPTIONS = 26


def serialize(value):
    return value if isinstance(value, str) else json.dumps(value, ensure_ascii=False, allow_nan=False)


class BadRequest(Exception):
    pass


def to_schema(questions):
    """The scorer's field schema for System One questions, as nimble/serving/compiler.py builds it."""
    if not isinstance(questions, dict) or not questions:
        raise BadRequest("'questions' must be a nonempty object")
    if len(questions) > MAX_QUESTIONS:
        raise BadRequest(f"at most {MAX_QUESTIONS} questions")
    schema, kinds = {}, {}
    for name, q in questions.items():
        kind, criteria = q.get("type"), q.get("criteria")
        field = {"description": serialize(q.get("instructions") or name)}
        if kind == "noul":
            criteria = criteria or {}
            field.update(type="boolean", choices=[False, True], choice_descriptions={
                "false": str(criteria.get("false", criteria.get("no", "No"))),
                "true": str(criteria.get("true", criteria.get("yes", "Yes"))),
            })
        elif kind == "choice":
            if not isinstance(criteria, dict) or not 2 <= len(criteria) <= MAX_OPTIONS:
                raise BadRequest(f"{name}: a choice needs 2 to {MAX_OPTIONS} options")
            field.update(type="enum", choices=list(criteria), choice_descriptions={
                key: str(text) if text is not None else key for key, text in criteria.items()
            })
        elif kind == "score":
            if not isinstance(criteria, list) or not 2 <= len(criteria) <= MAX_OPTIONS:
                raise BadRequest(f"{name}: a score needs 2 to {MAX_OPTIONS} levels")
            field.update(type="enum", choices=[str(i) for i in range(len(criteria))],
                         choice_descriptions={str(i): str(text) for i, text in enumerate(criteria)})
        else:
            raise BadRequest(f"{name}: type must be noul, choice or score")
        schema[name] = field
        kinds[name] = kind
    return schema, kinds


def concentration(probabilities):
    """One minus normalized entropy: 1 when every bit of mass is on one candidate, 0 when spread evenly."""
    values = [p for p in probabilities if p > 0]
    if len(probabilities) < 2:
        return 1.0
    entropy = -sum(p * math.log(p) for p in values)
    return max(0.0, min(1.0, 1 - entropy / math.log(len(probabilities))))


def to_answer(kind, field):
    scores = field["scores"]
    if kind == "noul":
        return {"type": "noul", "noul": scores["true"]}
    if kind == "choice":
        return {"type": "choice", "choice": field["value"], "probabilities": scores,
                "confidence": concentration(list(scores.values()))}
    levels = [scores[str(i)] for i in range(len(scores))]
    return {"type": "score", "score": sum(i * p for i, p in enumerate(levels)),
            "probabilities": {str(i): p for i, p in enumerate(levels)},
            "confidence": concentration(levels)}


config = json.loads((ROOT / ".cache/nimble-model.json").read_text())
scorer = ParallelScorer(model_path=config["model_path"], model_id=config["model_id"],
                        revision=config["revision"],
                        max_input_tokens=int(os.environ.get("NIMBLE_MAX_PROMPT_TOKENS", "8192")))
print(f"temperature {scorer.temperature}", file=sys.stderr, flush=True)
# MLX evaluates one request at a time; concurrent requests wait their turn.
lock = asyncio.Lock()
app = FastAPI(title="Nimble (local MLX)")


@app.get("/health")
async def health():
    return {"status": "ok"}


@app.post("/v1/systemone")
async def systemone(request: Request):
    try:
        body = await request.json()
        state = body.get("state")
        if state is None or (isinstance(state, str) and not state.strip()):
            raise BadRequest("'state' is required")
        schema, kinds = to_schema(body.get("questions"))
        context = serialize(state)
        started = time.perf_counter()
        async with lock:
            result = await asyncio.to_thread(scorer.score, context, schema)
        answers = {name: to_answer(kinds[name], field) for name, field in result["fields"].items()}
        print(f"{len(answers)} question(s) in {time.perf_counter() - started:.2f} s: "
              + ", ".join(f"{k}={v.get('choice', v.get('noul', v.get('score')))}" for k, v in answers.items()),
              file=sys.stderr, flush=True)
        return {"model": body.get("model") or config["model_id"], "answers": answers}
    except (BadRequest, ValueError) as error:
        return JSONResponse({"error": str(error)}, status_code=422)


if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=int(os.environ.get("PORT", "8000")), access_log=False)
