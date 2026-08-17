# Browserless Mermaid validate-and-repair sidecar. No Chromium — validation is mermaid + jsdom.
FROM node:22-slim

WORKDIR /app
COPY package.json package-lock.json* ./
RUN npm ci --omit=dev || npm install --omit=dev
COPY tsconfig.json ./
COPY src ./src

ENV PORT=5177 HOST=0.0.0.0
EXPOSE 5177
# tsx runs the TypeScript entrypoint directly (small service; no separate build step needed).
RUN npm install -g tsx@4
CMD ["tsx", "src/server.ts"]
