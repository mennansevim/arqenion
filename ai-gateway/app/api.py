"""Web servisi. Backend (AiFeedbackService) motor bir hata bulduğunda POST /feedback çağırır.

Çalıştırma:  uvicorn app.api:app --port 8090
Deneme:      http://localhost:8090/docs
"""
from typing import Optional, Union

from fastapi import FastAPI
from pydantic import BaseModel, Field

from .service import FeedbackService

app = FastAPI(title="Arqenion AI Gateway", version="0.1.0",
              description="Motorun bulduğu hatayı kaynak metne dayanarak anlatır. Karar vermez.")
_service: Optional[FeedbackService] = None


def service() -> FeedbackService:
    global _service
    if _service is None:
        _service = FeedbackService()
    return _service


class DeviationIn(BaseModel):
    """Motorun bulduğu sapma (backend'deki DeviationDto + adım bilgisi)."""
    sessionId: str = ""
    eventId: str = ""
    stepId: Union[int, str] = Field(..., examples=[52])
    deviationType: str = Field("WRONG_INSTRUMENT", examples=["WRONG_INSTRUMENT"])
    expected: Optional[str] = Field(None, examples=["SUTURE_POLYGLACTIN_0"])
    actual: Optional[str] = Field(None, examples=["SUTURE_POLYPROPYLENE_0"])
    severity: str = "MEDIUM"
    scenarioId: Optional[str] = Field(None, description="Biliniyorsa hata senaryosu (ör. S52-E6)")
    parameters: dict = Field(default_factory=dict, description="Hasta parametreleri (ör. SKIN_CLOSURE_TYPE)")
    procedureModel: Optional[str] = Field(None, description="'v1': backend eski 25 adımlı modelle çalışıyor; adım ve kodlar v2.4'e çevrilir")
    complication: Optional[dict] = Field(None, description="Açık komplikasyon (backend ActiveComplicationDto)")


class FeedbackOut(BaseModel):
    """Backend'deki AiFeedbackDto ile aynı alanlar + izlenebilirlik alanları."""
    sessionId: str
    eventId: str
    stepId: Union[int, str]
    deviationType: str
    possibleRisk: str
    explanation: str
    recommendedAction: str
    severity: str
    source: str = Field(..., description="llm: yapay zekâ yazdı ve kontrolden geçti · table: hazır klinik metin")
    modelName: Optional[str] = None
    promptTokens: Optional[int] = None
    completionTokens: Optional[int] = None
    scenarioId: Optional[str] = None
    sources: list
    checks: list = Field(..., description="Kontrolün bulduğu sorunlar (boşsa sorun yok)")
    procedureVersion: str
    mappedStepId: Optional[Union[int, str]] = Field(None, description="Eski modelden gelen istekte v2.4'teki adım")
    durationMs: float


@app.get("/health")
def health():
    s = service()
    return {"status": "ok", "procedureVersion": s.knowledge.version, "writer": s.writer_mode,
            "scenarios": len(s.knowledge.scenarios)}


@app.post("/feedback", response_model=FeedbackOut)
def feedback(dev: DeviationIn):
    return service().explain(dev.model_dump())
