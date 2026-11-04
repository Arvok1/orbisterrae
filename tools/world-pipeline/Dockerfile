FROM python:3.12-slim
WORKDIR /workspace
COPY tools/world-pipeline/requirements.txt /tmp/requirements.txt
RUN pip install --no-cache-dir -r /tmp/requirements.txt
ENV PYTHONDONTWRITEBYTECODE=1
ENTRYPOINT ["python", "tools/world-pipeline/world.py"]
