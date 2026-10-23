# Arqenion AI servisi. Derleme bağlamı depo kökü (model dosyaları ve alet niteleyicileri gerekir).
#   docker build -f ai-gateway/Dockerfile .
FROM python:3.12-slim
WORKDIR /app/ai-gateway
COPY ai-gateway/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY ai-gateway/app app
COPY procedure-model/dist /app/procedure-model/dist
COPY procedure-model/tools /app/procedure-model/tools
COPY backend/Procedures/instrument-descriptors.json /app/backend/Procedures/instrument-descriptors.json
EXPOSE 8090
CMD ["uvicorn", "app.api:app", "--host", "0.0.0.0", "--port", "8090"]
