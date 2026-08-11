# MELSEC MC-3E binary simulator — dev tooling (not product scope).
# Standard-library only; no pip install required.
FROM python:3.12-slim
WORKDIR /app
COPY server.py .
EXPOSE 5007
ENV MELSEC_PORT=5007
CMD ["python", "server.py"]
