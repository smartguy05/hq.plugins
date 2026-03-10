FROM ubuntu:22.04
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 python3-pip python3-venv nodejs npm \
    jq git curl wget ca-certificates zip unzip tree vim-tiny \
    && rm -rf /var/lib/apt/lists/*
RUN useradd -m -s /bin/bash -d /home/agent agent
RUN mkdir -p /workspace /shared && chown -R agent:agent /workspace /shared
USER agent
WORKDIR /workspace
CMD ["sleep", "infinity"]
