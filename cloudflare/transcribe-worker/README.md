# audiototext-transcribe (optional Worker)

Edge speech-to-text using Workers AI (`@cf/openai/whisper`).

```bash
cd cloudflare/transcribe-worker
npm install
npx wrangler login   # if needed
npx wrangler secret put TRANSCRIBE_API_KEY
npm run deploy
```

`POST /transcribe` with multipart `file` or raw audio bytes.
`GET /health` for liveness.
