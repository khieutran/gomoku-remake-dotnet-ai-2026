FROM --platform=linux/amd64 python:3.11.10-slim
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
WORKDIR /workspace
COPY requirements.txt /tmp/requirements.txt
RUN pip install --no-cache-dir -r /tmp/requirements.txt
COPY . /workspace/training
ENTRYPOINT ["python", "-m", "training.train"]
