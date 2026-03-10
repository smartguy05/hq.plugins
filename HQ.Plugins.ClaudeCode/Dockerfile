FROM ubuntu:22.04

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update && apt-get install -y \
    git curl wget ca-certificates build-essential \
    python3 python3-pip \
    iptables dnsutils \
    && rm -rf /var/lib/apt/lists/*

# Install Node.js 20 LTS
RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y nodejs \
    && rm -rf /var/lib/apt/lists/*

# Install Claude Code globally
RUN npm install -g @anthropic-ai/claude-code

# Pre-configure for non-interactive use
ENV CLAUDE_CODE_DISABLE_NONESSENTIAL=1

# Create non-root user
RUN useradd -m -s /bin/bash coder
RUN mkdir -p /workspace && chown coder:coder /workspace

WORKDIR /workspace

CMD ["sleep", "infinity"]
